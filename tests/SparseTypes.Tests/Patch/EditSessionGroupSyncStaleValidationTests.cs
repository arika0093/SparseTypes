using SparseTypes.Generated;

namespace SparseTypes.Tests;

public sealed class EditSessionGroupSyncStaleValidationTests
{
    [Test]
    public void PrepareFailsWhenReadinessValidatorAcceptsSessionBaseline()
    {
        var session = new NeutralSessionModel { Name = "before" }.CreateEditSession();
        var group = new EditSessionGroup();
        group.Register("profile", Registration(session, validate: () =>
        {
            session.AcceptChanges();
            return null;
        }));
        session.Observable.Name = "submitted";

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());

        failure.Issues.ShouldHaveSingleItem().Message.ShouldContain("baseline changed");
    }

    [Test]
    public void PrepareFailsWhenChangeValidatorUnregistersSection()
    {
        var session = new NeutralSessionModel { Name = "before" }.CreateEditSession();
        var group = new EditSessionGroup();
        EditSessionSectionHandle<NeutralSessionModel, NeutralSessionModel.ChangeSet>? handle = null;
        handle = group.Register("profile", Registration(session, validateChanges: _ =>
        {
            group.Unregister(handle!, EditSessionGroupRemovalPolicy.DetachWithPendingChanges);
            return null;
        }));
        session.Observable.Name = "submitted";

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());

        failure.Issues.ShouldHaveSingleItem().Message.ShouldContain("unregistered or replaced");
    }

    [Test]
    public void PrepareFailsWhenReadinessValidatorReplacesSection()
    {
        var session = new NeutralSessionModel { Name = "before" }.CreateEditSession();
        var replacementSession = new NeutralSessionModel { Name = "other" }.CreateEditSession();
        var group = new EditSessionGroup();
        EditSessionSectionHandle<NeutralSessionModel, NeutralSessionModel.ChangeSet>? handle = null;
        handle = group.Register("profile", Registration(session, validate: () =>
        {
            group.Replace(handle!, Registration(replacementSession), EditSessionGroupRemovalPolicy.DetachWithPendingChanges);
            return null;
        }));
        session.Observable.Name = "submitted";

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());

        failure.Issues.ShouldHaveSingleItem().Message.ShouldContain("unregistered or replaced");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void PrepareStillSucceedsWhenValidatorOnlyAddsLaterEdits()
    {
        var session = new NeutralSessionModel { Name = "before", Version = 1 }.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session, validate: () =>
        {
            session.Observable.Version = 2;
            return null;
        }));
        session.Observable.Name = "submitted";

        var submission = group.Prepare();

        submission.Get(handle).Changes.Name.After.Value.ShouldBe("submitted");
        submission.Get(handle).Changes.Version.IsChanged.ShouldBeFalse();
    }

    private static EditSessionGroupRegistration<NeutralSessionModel, NeutralSessionModel.ChangeSet> Registration(
        NeutralSessionModel.EditSession session,
        Func<string?>? validate = null,
        Func<NeutralSessionModel.ChangeSet, string?>? validateChanges = null
    ) =>
        new()
        {
            Session = session,
            CreateChangeSet = session.CreateChangeSet,
            IsEmpty = static changes => changes.IsEmpty,
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            Validate = validate,
            ValidateChanges = validateChanges,
        };
}
