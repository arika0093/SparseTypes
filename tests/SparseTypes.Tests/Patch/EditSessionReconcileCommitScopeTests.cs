using SparseTypes.Generated;

namespace SparseTypes.Tests;

/// <summary>Observer failures after a reconcile fast path commits must surface instead of re-running reconciliation.</summary>
public sealed class EditSessionReconcileCommitScopeTests
{
    private const string ObserverFailure = "Observer failed after commit.";

    private static (NeutralSessionModel Model, NeutralSessionModel.EditSession Session) CreateEditedSession()
    {
        var model = new NeutralSessionModel { Name = "base", Version = 1 };
        var session = model.CreateEditSession();
        session.Observable.Name = "local";
        return (model, session);
    }

    [Test]
    public void ObserverFailureAfterFirstFastPathCommitPropagatesWithCommittedBaseline()
    {
        var (_, session) = CreateEditedSession();
        var submitted = session.CreateChangeSet();
        var persisted = new NeutralSessionModel { Name = "local", Version = 1 };
        session.PropertyChanged += (_, _) => throw new InvalidOperationException(ObserverFailure);

        Should
            .Throw<InvalidOperationException>(() => session.TryReconcile(submitted, persisted, out _))
            .Message.ShouldBe(ObserverFailure);

        // The baseline advanced exactly once; reconstruction did not run against it.
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ObserverFailureAfterAlreadyReconciledRetryPropagates()
    {
        var (_, session) = CreateEditedSession();
        var submitted = session.CreateChangeSet();
        var persisted = new NeutralSessionModel { Name = "local", Version = 1 };
        session.TryReconcile(submitted, persisted, out var firstError).ShouldBeTrue();
        firstError.ShouldBeNull();
        session.PropertyChanged += (_, _) => throw new InvalidOperationException(ObserverFailure);

        Should
            .Throw<InvalidOperationException>(() => session.TryReconcile(submitted, persisted, out _))
            .Message.ShouldBe(ObserverFailure);

        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }
}
