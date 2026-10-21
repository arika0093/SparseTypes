using System.ComponentModel;

namespace SparseTypes;

/// <summary>Deterministic mixed-operation algebra over value-free descriptors.</summary>
/// <remarks>The rules decide composition shapes, rollback plans, and projection gates from member paths and history kinds only.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class MixedChangeAlgebra
{
    /// <summary>The path used for whole-root operations.</summary>
    public const string WholeRootPath = "$root";

    /// <summary>Composes two operations on the same path applied in order.</summary>
    /// <remarks>Transition then transition keeps continuity checks.</remarks>
    public static MixedComposeResult Compose(MixedMemberOperation first, MixedMemberOperation second)
    {
        if (!first.IsWholeRoot && !second.IsWholeRoot && !string.Equals(first.Path, second.Path, StringComparison.Ordinal))
        {
            return new MixedComposeResult(
                false,
                first.Path,
                MixedHistoryKind.Transition,
                first.After,
                false,
                false,
                "Mixed composition of '" + first.Path + "' and '" + second.Path + "' requires both operations to target the same path."
            );
        }

        var path = first.IsWholeRoot || second.IsWholeRoot ? WholeRootPath : first.Path;

        // A trailing blind whole-root set overwrites everything before it.
        if (second.IsWholeRoot && second.History == MixedHistoryKind.BlindSet)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.BlindSet, second.After, false, true, null);
        }

        // A leading blind whole-root set absorbs memberwise operations that
        // follow.
        if (first.IsWholeRoot && first.History == MixedHistoryKind.BlindSet && !second.IsWholeRoot)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.BlindSet, first.After, false, true, null);
        }

        if (first.IsWholeRoot || second.IsWholeRoot)
        {
            return WholeNeedsValues(first, second, path);
        }

        if (first.History == MixedHistoryKind.Transition && second.History == MixedHistoryKind.Transition)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.Transition, second.After, true, false, null);
        }

        if (first.History == MixedHistoryKind.Transition)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.Transition, second.After, false, false, null);
        }

        if (second.History == MixedHistoryKind.Transition)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.BlindSet, second.After, true, false, null);
        }

        return new MixedComposeResult(true, path, MixedHistoryKind.BlindSet, second.After, false, false, null);
    }

    /// <summary>Composes two operation sequences applied in order.</summary>
    /// <remarks>Disjoint paths merge without checks.</remarks>
    public static MixedSequenceComposition ComposeSequences(IEnumerable<MixedMemberOperation> first, IEnumerable<MixedMemberOperation> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var foldPending = new List<MixedComposeResult>();
        var firstByPath = IndexByPath(first, foldPending);
        var secondByPath = IndexByPath(second, foldPending);
        var firstRoot = TakeRoot(firstByPath, out var firstMembers);
        var secondRoot = TakeRoot(secondByPath, out var secondMembers);

        // Trailing blind whole-root overwrites everything before it, including
        // same-sequence members that arrived alongside it.
        if (secondRoot.HasValue && secondRoot.Value.History == MixedHistoryKind.BlindSet)
        {
            MixedMemberOperation root = secondRoot.Value;
            var failures = new List<MixedComposeResult>();
            var pending = new List<MixedComposeResult>(foldPending);
            if (firstRoot.HasValue)
            {
                var outcome = Compose(firstRoot.Value, secondRoot.Value);
                if (!outcome.Succeeded)
                {
                    failures.Add(outcome);
                    return new MixedSequenceComposition(false, [.. firstMembers.Values, .. secondMembers.Values], failures, pending);
                }

                if (outcome.RequiresContinuityCheck)
                {
                    pending.Add(outcome);
                }

                root = new MixedMemberOperation(outcome.Path, outcome.ResultHistory, outcome.ResultAfter, outcome.ResultIsWholeRoot);
            }

            return new MixedSequenceComposition(true, [root], failures, pending);
        }

        // Leading blind whole-root absorbs following memberwise operations.
        if (firstRoot.HasValue && firstRoot.Value.History == MixedHistoryKind.BlindSet)
        {
            if (secondRoot.HasValue)
            {
                var outcome = Compose(firstRoot.Value, secondRoot.Value);
                if (!outcome.Succeeded)
                {
                    return new MixedSequenceComposition(false, [.. firstMembers.Values, .. secondMembers.Values], [outcome], foldPending);
                }

                // Whole-whole blind composition stays a single blind root;
                // members on both sides are absorbed.
                var root = new MixedMemberOperation(outcome.Path, outcome.ResultHistory, outcome.ResultAfter, outcome.ResultIsWholeRoot);
                var absorbedPending = new List<MixedComposeResult>(foldPending);
                if (outcome.RequiresContinuityCheck)
                {
                    absorbedPending.Add(outcome);
                }

                return new MixedSequenceComposition(true, [root, .. firstMembers.Values], [], absorbedPending);
            }

            // Second-sequence members are absorbed; first-sequence entries
            // stay.
            var retained = new List<MixedMemberOperation>(firstByPath.Count + secondByPath.Count);
            foreach (var entry in firstByPath)
            {
                retained.Add(entry.Value);
            }

            return new MixedSequenceComposition(true, retained, [], foldPending);
        }

        // Any remaining whole-root transition overlaps every member path.
        if (firstRoot.HasValue || secondRoot.HasValue)
        {
            return ComposeWithWholeTransition(firstRoot, firstMembers, secondRoot, secondMembers, foldPending);
        }

        return MixedSequenceComposer.Compose(firstMembers, secondMembers, foldPending);
    }

    /// <summary>Whether either path is a strict segment ancestor of the other.</summary>
    /// <remarks>Segments split on '.' with bracket suffixes kept on their element (so <c>Items["k"]</c> is one segment).</remarks>
    public static bool IsAncestorOrDescendant(string first, string second)
    {
        var firstSegments = MixedPathSegments.Split(first);
        var secondSegments = MixedPathSegments.Split(second);
        return MixedPathSegments.IsStrictAncestor(firstSegments, secondSegments) || MixedPathSegments.IsStrictAncestor(secondSegments, firstSegments);
    }

    private static MixedMemberOperation? TakeRoot(
        Dictionary<string, MixedMemberOperation> indexed,
        out Dictionary<string, MixedMemberOperation> members
    )
    {
        members = new Dictionary<string, MixedMemberOperation>(StringComparer.Ordinal);
        MixedMemberOperation? root = null;
        foreach (var entry in indexed)
        {
            if (string.Equals(entry.Key, WholeRootPath, StringComparison.Ordinal))
            {
                root = entry.Value;
            }
            else
            {
                members[entry.Key] = entry.Value;
            }
        }

        return root;
    }

    private static MixedSequenceComposition ComposeWithWholeTransition(
        MixedMemberOperation? firstRoot,
        Dictionary<string, MixedMemberOperation> firstMembers,
        MixedMemberOperation? secondRoot,
        Dictionary<string, MixedMemberOperation> secondMembers,
        List<MixedComposeResult> seedPending
    )
    {
        var failures = new List<MixedComposeResult>();
        var pending = new List<MixedComposeResult>(seedPending);
        if (firstRoot.HasValue && secondRoot.HasValue)
        {
            var outcome = Compose(firstRoot.Value, secondRoot.Value);
            if (!outcome.Succeeded)
            {
                failures.Add(outcome);
                return new MixedSequenceComposition(false, [.. firstMembers.Values, .. secondMembers.Values], failures, pending);
            }

            if (outcome.RequiresContinuityCheck)
            {
                pending.Add(outcome);
            }

            // Members collapse into the whole-root transition; value continuity
            // is verified by the typed ChangeSet API.
            var root = new MixedMemberOperation(outcome.Path, outcome.ResultHistory, outcome.ResultAfter, outcome.ResultIsWholeRoot);
            return new MixedSequenceComposition(true, [root], failures, pending);
        }

        // Exactly one whole-root transition: it consumes the other side's
        // members, while same-sequence members accompany their own root.
        if (secondRoot.HasValue)
        {
            if (firstMembers.Count == 0)
            {
                return new MixedSequenceComposition(true, [secondRoot.Value, .. secondMembers.Values], failures, pending);
            }

            var probe = Compose(firstMembers.Values.First(), secondRoot.Value);
            if (!probe.Succeeded)
            {
                failures.Add(probe);
                return new MixedSequenceComposition(false, [.. firstMembers.Values, .. secondMembers.Values], failures, pending);
            }

            if (probe.RequiresContinuityCheck)
            {
                pending.Add(probe);
            }

            var root = new MixedMemberOperation(probe.Path, probe.ResultHistory, probe.ResultAfter, probe.ResultIsWholeRoot);
            return new MixedSequenceComposition(true, [root], failures, pending);
        }

        if (firstMembers.Count == 0)
        {
            return new MixedSequenceComposition(true, [firstRoot!.Value, .. secondMembers.Values], failures, pending);
        }

        var firstProbe = Compose(firstRoot!.Value, secondMembers.Values.First());
        if (!firstProbe.Succeeded)
        {
            failures.Add(firstProbe);
            return new MixedSequenceComposition(false, [.. firstMembers.Values, .. secondMembers.Values], failures, pending);
        }

        if (firstProbe.RequiresContinuityCheck)
        {
            pending.Add(firstProbe);
        }

        return new MixedSequenceComposition(true, [firstRoot.Value, .. firstMembers.Values], failures, pending);
    }

    /// <summary>Plans a rollback: transitions invert, blind sets are skipped and reported.</summary>
    public static MixedRollbackPlan CreateRollbackPlan(IEnumerable<MixedMemberOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var reversible = new List<string>();
        var skipped = new List<string>();
        foreach (var operation in IndexByPath(operations).Values)
        {
            var path = operation.IsWholeRoot ? WholeRootPath : operation.Path;
            if (operation.History == MixedHistoryKind.Transition)
            {
                reversible.Add(path);
            }
            else
            {
                skipped.Add(path);
            }
        }

        return new MixedRollbackPlan(reversible, skipped);
    }

    /// <summary>Whether the operations can form a baseline-aware change set.</summary>
    /// <remarks>Returns false and reports the blind paths when any operation lacks a before-state.</remarks>
    public static bool CanFormChangeSet(IEnumerable<MixedMemberOperation> operations, out IReadOnlyList<string> blindPaths)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var blind = IndexByPath(operations)
            .Values.Where(static operation => operation.History == MixedHistoryKind.BlindSet)
            .Select(static operation => operation.IsWholeRoot ? WholeRootPath : operation.Path)
            .ToList();

        blindPaths = Array.AsReadOnly(blind.ToArray());
        return blind.Count == 0;
    }

    /// <summary>Guards the rebase-policy seam for future strict handling.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for any policy besides pass-through.</exception>
    public static void EnsurePassthrough(RedactedBeforePolicy policy, string? parameterName = null)
    {
        if (policy != RedactedBeforePolicy.Passthrough)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(policy),
                "Only redacted-before pass-through is supported. Strict rejection belongs to the rebase-policy follow-up."
            );
        }
    }

    private static MixedComposeResult WholeNeedsValues(MixedMemberOperation first, MixedMemberOperation second, string path)
    {
        // Whole-root transition then whole-root transition: history survives
        // once the value-level endpoints line up.
        if (first.IsWholeRoot && second.IsWholeRoot && first.History == MixedHistoryKind.Transition && second.History == MixedHistoryKind.Transition)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.Transition, second.After, true, true, null);
        }

        // Memberwise then whole-root transition: the whole before-state is
        // real, so the merged whole operation keeps history once values line
        if (!first.IsWholeRoot && second.IsWholeRoot && second.History == MixedHistoryKind.Transition)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.Transition, second.After, true, true, null);
        }

        // Whole-root transition then memberwise: the whole after-state kind
        // survives.
        if (first.IsWholeRoot && first.History == MixedHistoryKind.Transition && !second.IsWholeRoot)
        {
            return new MixedComposeResult(true, path, MixedHistoryKind.Transition, first.After, true, true, null);
        }

        return new MixedComposeResult(
            false,
            path,
            MixedHistoryKind.Transition,
            second.After,
            false,
            true,
            "Whole-root transitions need value-level continuity. Compose them through the typed ChangeSet API."
        );
    }

    private static Dictionary<string, MixedMemberOperation> IndexByPath(
        IEnumerable<MixedMemberOperation> operations,
        List<MixedComposeResult>? pending = null
    )
    {
        // Compose repeated paths in order to preserve history; reject invalid
        // folds and retain value-verification obligations.
        var indexed = new Dictionary<string, MixedMemberOperation>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            var key = KeyOf(operation);
            if (!indexed.TryGetValue(key, out var existing))
            {
                indexed[key] = operation;
                continue;
            }

            var outcome = Compose(existing, operation);
            if (!outcome.Succeeded)
            {
                throw new ArgumentException(
                    "Duplicate operations on path '" + key + "' cannot be composed: " + outcome.FailureReason,
                    nameof(operations)
                );
            }

            if (outcome.RequiresContinuityCheck)
            {
                pending?.Add(outcome);
            }

            indexed[key] = new MixedMemberOperation(outcome.Path, outcome.ResultHistory, outcome.ResultAfter, outcome.ResultIsWholeRoot);
        }

        return indexed;
    }

    private static string KeyOf(MixedMemberOperation operation) => operation.IsWholeRoot ? WholeRootPath : operation.Path;
}
