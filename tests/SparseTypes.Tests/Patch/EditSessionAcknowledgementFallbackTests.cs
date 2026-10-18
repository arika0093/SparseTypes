using System.Threading;
using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Acknowledgement fallback for registrations without a baseline revision.</summary>
/// <remarks>
/// The synthetic registration reproduces the legacy-setup surface (no
/// <see cref="ISparseEditSessionWithBaselineRevision"/>) while controlling
/// pending-path enumeration directly, which lets the tests build structured
/// key collisions that rendered text alone cannot distinguish.
/// </remarks>
public sealed class EditSessionAcknowledgementFallbackTests
{
    [Test]
    public void EmptyPendingCountsAsAcknowledgedAndPreservesOriginalException()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Segment("Name")];

        var submission = group.Prepare();

        session.FreshPending = [];

        var failure = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        failure.Message.ShouldBe("simulated commit failure");
    }

    [Test]
    public void DisjointPendingPathsStillCountAsAcknowledged()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Segment("Name")];

        var submission = group.Prepare();

        session.FreshPending = [Segment("Version")];

        // No structured overlap: the submitted transition is acknowledged and
        // the original failure surfaces instead of a stale classification.
        var failure = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        failure.Message.ShouldBe("simulated commit failure");
    }

    [Test]
    public void StillPendingExactPathClassifiesAsStale()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Segment("Name")];

        var submission = group.Prepare();

        // The commit failed without landing; the same path still pends.
        session.FreshPending = [Segment("Name")];

        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );
    }

    [Test]
    public void DisplayCollidingTypedKeysDoNotOverlapStructured()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        // Structured string key "1"; the fresh pending key is the typed int 1
        // rendering the identical display text under the same member.
        session.FreshPending = [Keyed("Map", "1")];

        var submission = group.Prepare();

        session.FreshPending = [IntKeyed("Map", 1)];

        // Structured distinctness wins: acknowledged, original failure kept.
        var failure = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        failure.Message.ShouldBe("simulated commit failure");
    }

    [Test]
    public void DisplayCollidingSameTypedKeysStillOverlap()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Keyed("Map", "1")];

        var submission = group.Prepare();

        session.FreshPending = [Keyed("Map", "1")];

        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );
    }

    [Test]
    public void DifferentRootTypesDoNotOverlap()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Keyed("Map", "1")];

        var submission = group.Prepare();

        // Same member/key text as the typed-collision case, but the fresh
        // root type differs, so display equality alone would misclassify.
        session.FreshPending = [Keyed("Map", "1", RootType: typeof(string))];

        var failure = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        failure.Message.ShouldBe("simulated commit failure");
    }

    [Test]
    public void NullFreshCaptureStaysConservative()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Segment("Name")];

        var submission = group.Prepare();

        session.FreshPending = [Segment("Version")];
        session.FreshPathsIsNull = true;

        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );
    }

    [Test]
    public void NullFreshChangeSetStaysConservative()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Segment("Name")];

        var submission = group.Prepare();

        session.FreshCaptureIsNull = true;

        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );
    }

    [Test]
    public void CancellationPropagatesOutOfCommitClassification()
    {
        var session = new LegacySession();
        var group = new EditSessionGroup();
        var handle = group.Register("legacy", Register(session));
        session.FreshPending = [Segment("Name")];

        var submission = group.Prepare();

        session.CommitFailure = new OperationCanceledException();

        Should.Throw<OperationCanceledException>(() => group.AcceptChanges(submission, handle));
    }

    private static EditSessionGroupRegistration<NeutralSessionModel, object> Register(
        LegacySession session
    ) =>
        new()
        {
            Session = session,
            CreateChangeSet = () => session.CreateChangeSet()!,
            IsEmpty = session.IsEmpty,
            EnumerateChangedPaths = changes => session.EnumeratePaths(changes)!,
        };

    private static SparsePath Segment(string member) =>
        new(typeof(NeutralSessionModel), SparsePathSegment.Member(member));

    private static SparsePath Keyed(string member, string key, Type? RootType = null) =>
        new(
            RootType ?? typeof(NeutralSessionModel),
            SparsePathSegment.Member(member),
            SparsePathSegment.KeyOf(key)
        );

    private static SparsePath IntKeyed(string member, int key) =>
        new(
            typeof(NeutralSessionModel),
            SparsePathSegment.Member(member),
            SparsePathSegment.KeyOf(key)
        );

    /// <summary>Synthetic legacy session: no baseline revision interface, throwing accept.</summary>
    private sealed class LegacySession : ISparseEditSession<NeutralSessionModel, object>
    {
        public NeutralSessionModel Model { get; } = new();

        public bool HasChanges => true;

        public OperationCanceledException? CommitFailure { get; set; }

        public bool FreshCaptureIsNull { get; set; }

        public bool FreshPathsIsNull { get; set; }

        public IReadOnlyList<SparsePath> FreshPending { get; set; } = [];

        public void AcceptChanges()
        {
            // The registration path calls the typed overload; this barrier
            // exists on the legacy interface only.
        }

        public void AcceptChanges(object changes)
        {
            if (CommitFailure is not null)
            {
                throw CommitFailure;
            }

            throw new InvalidOperationException("simulated commit failure");
        }

        public object? CreateChangeSet() => FreshCaptureIsNull ? null : new();

        public bool IsEmpty(object changes) => false;

        public IReadOnlyList<SparsePath>? EnumeratePaths(object changes) =>
            FreshPathsIsNull ? null : FreshPending;
    }
}
