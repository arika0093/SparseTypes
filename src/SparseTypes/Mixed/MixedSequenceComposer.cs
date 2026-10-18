namespace SparseTypes;

/// <summary>Member-level composition stage of <see cref="MixedChangeAlgebra.ComposeSequences"/>.</summary>
/// <remarks>
/// Paths are parsed into segments once per stage and overlapping lookups run
/// through a segment trie, so disjoint compositions avoid the historical
/// per-pair parsing and second-side rescans while preserving first-match
/// selection, output ordering, and failure/pending ordering exactly.
/// </remarks>
internal static class MixedSequenceComposer
{
    internal static MixedSequenceComposition Compose(
        Dictionary<string, MixedMemberOperation> firstMembers,
        Dictionary<string, MixedMemberOperation> secondMembers,
        List<MixedComposeResult> seedPending
    )
    {
        var staged = new List<MixedMemberOperation>(firstMembers.Count + secondMembers.Count);
        var removed = new List<int>();
        var failures = new List<MixedComposeResult>();
        var pending = new List<MixedComposeResult>(seedPending);
        var consumedSecond = new HashSet<string>(StringComparer.Ordinal);
        var secondIndex = new MixedSegmentIndex();
        var composedIndex = new MixedSegmentIndex();

        // Ordinals mirror dictionary insertion order, which is the historical
        // "first overlapping entry in enumeration order" selection rule.
        foreach (var entry in secondMembers)
        {
            var segments = MixedPathSegments.Split(entry.Key);
            secondIndex.Insert(entry.Key, segments, entry.Value);
        }

        foreach (var entry in firstMembers)
        {
            var segments = MixedPathSegments.Split(entry.Key);
            if (secondMembers.TryGetValue(entry.Key, out var following))
            {
                ComposeExactly(
                    entry.Value,
                    following,
                    pending,
                    failures,
                    staged,
                    composedIndex,
                    entry.Key,
                    segments
                );
                secondIndex.Remove(entry.Key, segments);
                consumedSecond.Add(entry.Key);
                continue;
            }

            // A whole member operation overlaps its nested children and keyed
            // entries; the first eligible second-side entry in insertion order
            // wins and is consumed.
            if (secondIndex.FindOverlap(segments) is { } overlap)
            {
                consumedSecond.Add(overlap.Path);
                var resolution = ResolveAncestorOverlap(
                    entry.Value,
                    segments,
                    overlap.Value,
                    overlap.Segments
                );
                if (resolution.Failure is { } failure)
                {
                    failures.Add(failure);
                }

                if (resolution.KeepFirst)
                {
                    Stage(staged, composedIndex, entry.Key, segments, entry.Value);
                }

                if (resolution.KeepSecond)
                {
                    Stage(staged, composedIndex, overlap.Path, overlap.Segments, overlap.Value);
                }

                continue;
            }

            Stage(staged, composedIndex, entry.Key, segments, entry.Value);
        }

        foreach (var entry in secondMembers)
        {
            if (firstMembers.ContainsKey(entry.Key) || consumedSecond.Contains(entry.Key))
            {
                continue;
            }

            var segments = MixedPathSegments.Split(entry.Key);
            // Second-side entries overlapping an already-retained first entry
            // were resolved above.
            if (composedIndex.FindOverlap(segments) is { } retained)
            {
                var resolution = ResolveAncestorOverlap(
                    retained.Value,
                    retained.Segments,
                    entry.Value,
                    segments
                );
                if (resolution.Failure is { } failure)
                {
                    failures.Add(failure);
                }

                if (resolution.KeepSecond)
                {
                    if (!resolution.KeepFirst)
                    {
                        // Replace the retained ancestor when the trailing side
                        // wins: tombstone its slot; compaction preserves order.
                        removed.Add(retained.Ordinal);
                        composedIndex.RemoveNode(retained);
                    }

                    Stage(staged, composedIndex, entry.Key, segments, entry.Value);
                }

                continue;
            }

            Stage(staged, composedIndex, entry.Key, segments, entry.Value);
        }

        var composed = new List<MixedMemberOperation>(staged.Count - removed.Count);
        for (var ordinal = 0; ordinal < staged.Count; ordinal++)
        {
            if (removed.Count == 0 || !removed.Contains(ordinal))
            {
                composed.Add(staged[ordinal]);
            }
        }

        return new MixedSequenceComposition(failures.Count == 0, composed, failures, pending);
    }

    private static void ComposeExactly(
        MixedMemberOperation first,
        MixedMemberOperation following,
        List<MixedComposeResult> pending,
        List<MixedComposeResult> failures,
        List<MixedMemberOperation> staged,
        MixedSegmentIndex composedIndex,
        string path,
        string[] segments
    )
    {
        var outcome = MixedChangeAlgebra.Compose(first, following);
        if (!outcome.Succeeded)
        {
            failures.Add(outcome);
            return;
        }

        if (outcome.RequiresContinuityCheck)
        {
            pending.Add(outcome);
        }

        // Member/member composition of equal paths always keeps that member
        // path as the result path, so the parsed segments stay valid.
        Stage(
            staged,
            composedIndex,
            path,
            segments,
            new MixedMemberOperation(
                outcome.Path,
                outcome.ResultHistory,
                outcome.ResultAfter,
                outcome.ResultIsWholeRoot
            )
        );
    }

    private static void Stage(
        List<MixedMemberOperation> staged,
        MixedSegmentIndex composedIndex,
        string path,
        string[] segments,
        MixedMemberOperation operation
    )
    {
        staged.Add(operation);
        if (!operation.IsWholeRoot)
        {
            composedIndex.Insert(path, segments, operation);
        }
    }

    private sealed record OverlapResolution
    {
        public bool KeepFirst { get; init; }

        public bool KeepSecond { get; init; }

        public MixedComposeResult? Failure { get; init; }
    }

    private static OverlapResolution ResolveAncestorOverlap(
        MixedMemberOperation first,
        string[] firstSegments,
        MixedMemberOperation second,
        string[] secondSegments
    )
    {
        var firstAncestor = MixedPathSegments.IsStrictAncestor(firstSegments, secondSegments);
        // Any transition side needs value-level continuity that descriptors
        // cannot verify; never invent before values.
        if (
            first.History == MixedHistoryKind.Transition
            || second.History == MixedHistoryKind.Transition
        )
        {
            return new OverlapResolution
            {
                KeepFirst = false,
                KeepSecond = false,
                Failure = new MixedComposeResult(
                    false,
                    firstAncestor ? first.Path : second.Path,
                    MixedHistoryKind.Transition,
                    second.After,
                    false,
                    false,
                    "Overlapping operations on '"
                        + first.Path
                        + "' and '"
                        + second.Path
                        + "' need value-level continuity. Compose them through the typed ChangeSet API."
                ),
            };
        }

        // Both blind: whole-member granularity wins.
        if (firstAncestor)
        {
            return new OverlapResolution
            {
                KeepFirst = true,
                KeepSecond = false,
                Failure = null,
            };
        }

        return new OverlapResolution
        {
            KeepFirst = false,
            KeepSecond = true,
            Failure = null,
        };
    }
}
