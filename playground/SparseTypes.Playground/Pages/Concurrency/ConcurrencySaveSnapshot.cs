namespace SparseTypes.Playground.Pages.Concurrency;

internal sealed class ConcurrencySaveSnapshot
{
    private ConcurrencySaveSnapshot(
        ConcurrentOrderDto.ChangeSet submitted,
        ConcurrentOrderDto submittedDraft,
        int baseRevision
    )
    {
        Submitted = submitted;
        SubmittedDraft = submittedDraft;
        BaseRevision = baseRevision;
    }

    internal ConcurrentOrderDto.ChangeSet Submitted { get; }

    internal ConcurrentOrderDto SubmittedDraft { get; }

    internal int BaseRevision { get; }

    internal static ConcurrencySaveSnapshot Capture(
        ConcurrentOrderDto.EditSession session,
        ConcurrentOrderDto liveDraft,
        int baseRevision
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(liveDraft);
        return new ConcurrencySaveSnapshot(
            session.CreateChangeSet(),
            liveDraft.DeepClone(),
            baseRevision
        );
    }
}
