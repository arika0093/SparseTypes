using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SparseTypes.Tests;

public sealed class SparseEditControllerTests
{
    [Test]
    public async Task PreparationRejectsStaleAsyncValidationWithoutApplyingItsErrors()
    {
        var model = new HistoryScalarModel { Name = "Base" };
        var session = model.CreateEditSession();
        var validationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validationResult = new TaskCompletionSource<IReadOnlyList<SparseValidationError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var editor = session.CreateEditor(
            new SparseEditControllerOptions<HistoryScalarModel.ChangeSet>
            {
                ValidateAsync = async (_, _) =>
                {
                    validationStarted.SetResult();
                    return await validationResult.Task;
                },
            }
        );
        session.Observable.Name = "First";

        var preparing = editor.PrepareSaveAsync();
        await validationStarted.Task;
        session.Observable.Name = "Second";
        validationResult.SetResult([
            new SparseValidationError
            {
                Path = HistoryScalarModel.SparsePath.Name,
                Origin = "Server",
                Error = "Stale validation",
            },
        ]);

        await Should.ThrowAsync<EditSessionGroupValidationException>(async () => await preparing);

        editor.Validation.HasErrors.ShouldBeFalse();
        editor.SaveState.ShouldBe(SparseEditSaveState.Idle);
    }

    [Test]
    public async Task PreparationStoresTypedErrorsAndClearsThemAfterInputChanges()
    {
        var model = new HistoryScalarModel { Name = "Base" };
        var session = model.CreateEditSession();
        using var editor = session.CreateEditor(
            new SparseEditControllerOptions<HistoryScalarModel.ChangeSet>
            {
                Validate = _ =>
                    [
                        new SparseValidationError
                        {
                            Path = HistoryScalarModel.SparsePath.Name,
                            Origin = "Other",
                            Error = "Name is invalid",
                        },
                    ],
            }
        );
        session.Observable.Name = "Changed";

        await Should.ThrowAsync<EditSessionGroupValidationException>(async () => await editor.PrepareSaveAsync());

        editor.Validation.HasPathErrors(HistoryScalarModel.SparsePath.Name.Path).ShouldBeTrue();
        editor.Validation.GetPathErrors(HistoryScalarModel.SparsePath.Name.Path).ShouldContain("Name is invalid");
        session.Observable.Name = "Corrected";
        editor.Validation.HasErrors.ShouldBeFalse();
    }

    [Test]
    public async Task SaveUsesCapturedChangesAndReconcilesLaterEdits()
    {
        var model = new HistoryScalarModel { Name = "Base" };
        var session = model.CreateEditSession();
        using var editor = session.CreateEditor();
        session.Observable.Name = "Submitted";
        var prepared = await editor.PrepareSaveAsync();

        prepared.ChangedPaths.ShouldContain(HistoryScalarModel.SparsePath.Name.Path);
        var state = await editor.SaveAsync(
            prepared,
            (changes, _) =>
            {
                changes.Name.After.Value.ShouldBe("Submitted");
                session.Observable.Name = "Edited while saving";
                return Task.FromResult(SparsePersistResult<HistoryScalarModel>.Success(new HistoryScalarModel { Name = "Submitted" }));
            }
        );

        state.ShouldBe(SparseEditSaveState.Saved);
        model.Name.ShouldBe("Edited while saving");
        session.HasChanges.ShouldBeTrue();
        editor.CanSave.ShouldBeTrue();
    }

    [Test]
    public async Task SaveRefusesAStalePreparedSubmissionBeforeCallingPersistence()
    {
        var model = new HistoryScalarModel { Name = "Base" };
        var session = model.CreateEditSession();
        using var editor = session.CreateEditor();
        session.Observable.Name = "Prepared";
        var prepared = await editor.PrepareSaveAsync();
        var persistCalls = 0;

        session.Observable.Name = "Changed after preparation";
        await Should.ThrowAsync<EditSessionStaleSubmissionException>(async () =>
            await editor.SaveAsync(
                prepared,
                (_, _) =>
                {
                    persistCalls++;
                    return Task.FromResult(SparsePersistResult<HistoryScalarModel>.Success(new HistoryScalarModel()));
                }
            )
        );

        persistCalls.ShouldBe(0);
    }

    [Test]
    public async Task ConcurrentSaveAttemptCannotSubmitTwice()
    {
        var session = new HistoryScalarModel { Name = "Base" }.CreateEditSession();
        using var editor = session.CreateEditor();
        session.Observable.Name = "Changed";
        var prepared = await editor.PrepareSaveAsync();
        var persistStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistResult = new TaskCompletionSource<SparsePersistResult<HistoryScalarModel>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistCalls = 0;
        var firstSave = editor.SaveAsync(
            prepared,
            (_, _) =>
            {
                persistCalls++;
                persistStarted.SetResult();
                return persistResult.Task;
            }
        );
        await persistStarted.Task;

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await editor.SaveAsync(prepared, (_, _) => Task.FromResult(SparsePersistResult<HistoryScalarModel>.Unknown()))
        );

        persistResult.SetResult(SparsePersistResult<HistoryScalarModel>.Success(new HistoryScalarModel { Name = "Changed" }));
        (await firstSave).ShouldBe(SparseEditSaveState.Saved);
        persistCalls.ShouldBe(1);
    }

    [Test]
    public async Task PersistenceExceptionsRetainUnknownSubmissionAndAreNotRetried()
    {
        var session = new HistoryScalarModel { Name = "Base" }.CreateEditSession();
        using var editor = session.CreateEditor();
        session.Observable.Name = "Changed";
        var prepared = await editor.PrepareSaveAsync();
        var persistCalls = 0;

        await Should.ThrowAsync<IOException>(async () =>
            await editor.SaveAsync(
                prepared,
                (_, _) =>
                {
                    persistCalls++;
                    throw new IOException("Connection lost after request dispatch.");
                }
            )
        );

        editor.SaveState.ShouldBe(SparseEditSaveState.OutcomeUnknown);
        ReferenceEquals(prepared, editor.UnknownSubmission).ShouldBeTrue();
        persistCalls.ShouldBe(1);
        editor.ResolveUnknownOutcomeAsNotPersisted();
        editor.UnknownSubmission.ShouldBeNull();
    }

    [Test]
    public async Task ExplicitRejectionStoresServerErrorsAndUnknownResultBlocksResubmission()
    {
        var session = new HistoryScalarModel { Name = "Base" }.CreateEditSession();
        using var editor = session.CreateEditor();
        session.Observable.Name = "Changed";
        var rejected = await editor.PrepareSaveAsync();
        var rejection = await editor.SaveAsync(
            rejected,
            (_, _) =>
                Task.FromResult(
                    SparsePersistResult<HistoryScalarModel>.Reject([
                        new SparseValidationError
                        {
                            Path = HistoryScalarModel.SparsePath.Name.Path,
                            Origin = "Remote",
                            Error = "Already used",
                        },
                    ])
                )
        );

        rejection.ShouldBe(SparseEditSaveState.Rejected);
        editor.Validation.GetPathErrors(HistoryScalarModel.SparsePath.Name.Path).ShouldContain("Already used");
        session.Observable.Name = "Corrected";
        editor.Validation.HasErrors.ShouldBeFalse();

        var uncertain = await editor.PrepareSaveAsync();
        var outcome = await editor.SaveAsync(uncertain, (_, _) => Task.FromResult(SparsePersistResult<HistoryScalarModel>.Unknown()));

        outcome.ShouldBe(SparseEditSaveState.OutcomeUnknown);
        ReferenceEquals(uncertain, editor.UnknownSubmission).ShouldBeTrue();
        editor.CanSave.ShouldBeFalse();
    }

    [Test]
    public async Task PendingChildDraftPreventsSubmissionPreparation()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a" }] };
        var session = model.CreateEditSession();
        using var editor = session.CreateEditor();
        using var child = session.OpenChildSession(CollectionEditRoster.SparsePath.Quests.Key("a"));
        child.Session.Observable.Title = "Unaccepted";

        await Should.ThrowAsync<EditSessionGroupValidationException>(async () => await editor.PrepareSaveAsync());

        editor.HasPendingDrafts.ShouldBeTrue();
        model.Quests[0].Title.ShouldBeEmpty();
    }

    [Test]
    public async Task CleanChildSessionDoesNotPreventSubmissionPreparation()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a" }] };
        var session = model.CreateEditSession();
        using var editor = session.CreateEditor();
        using var child = session.OpenChildSession(CollectionEditRoster.SparsePath.Quests.Key("a"));

        var submission = await editor.PrepareSaveAsync();

        submission.IsEmpty.ShouldBeTrue();
        editor.HasPendingDrafts.ShouldBeFalse();
    }
}
