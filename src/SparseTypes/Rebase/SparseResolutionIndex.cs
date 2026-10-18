using System;
using System.Collections.Generic;

namespace SparseTypes;

/// <summary>The kind of resolution decision stored at one path.</summary>
internal enum SparseResolutionDecisionKind
{
    /// <summary>Writes the declared incoming (local) value.</summary>
    UseIncoming,

    /// <summary>Writes the current value (keep).</summary>
    UseCurrent,

    /// <summary>Writes an explicit presence-aware custom value.</summary>
    Custom,
}

/// <summary>The effective decision stored at one path.</summary>
internal readonly record struct SparseResolutionDecision
{
    /// <summary>Creates an effective decision.</summary>
    /// <param name="kind">The decision kind.</param>
    /// <param name="customValue">The presence-aware value for custom decisions.</param>
    public SparseResolutionDecision(
        SparseResolutionDecisionKind kind,
        Optional<object?> customValue
    )
    {
        Kind = kind;
        CustomValue = customValue;
    }

    /// <summary>Gets the decision kind.</summary>
    public SparseResolutionDecisionKind Kind { get; }

    /// <summary>Gets the presence-aware value for custom decisions.</summary>
    public Optional<object?> CustomValue { get; }
}

/// <summary>Indexed conflict and decision lookups for one rebase resolution.</summary>
/// <remarks>
/// The conflict graph is built lazily once from the immutable conflict snapshot
/// so one-off small resolutions never pay for it. Decisions live in an exact
/// dictionary plus a segment trie: an exact decision is necessarily the nearest
/// covering one, so exact hits skip the ancestor walk entirely, while ancestor
/// queries scale with path depth rather than decision or conflict counts. Both
/// graphs branch on path identity (root type plus ordered segments) instead of
/// comparing unrelated paths.
/// </remarks>
internal sealed class SparseResolutionIndex
{
    private readonly IReadOnlyList<SparseConflict> _conflicts;

    private readonly Dictionary<SparsePath, SparseResolutionDecision> _exact = new();

    private Dictionary<Type, Node>? _conflictRoots;

    private Dictionary<Type, Node>? _decisionRoots;

    internal SparseResolutionIndex(IReadOnlyList<SparseConflict> conflicts) =>
        _conflicts = conflicts;

    /// <summary>Gets the number of explicit path decisions stored.</summary>
    internal int DecisionCount => _exact.Count;

    /// <summary>Snapshots the paths carrying an explicit decision.</summary>
    internal SparsePath[] DecidedPaths() => _exact.Keys.ToArray();

    /// <summary>Finds the first conflict exactly at <paramref name="path"/>.</summary>
    internal SparseConflict? FindExact(SparsePath path)
    {
        EnsureConflictGraph();
        return Descend(_conflictRoots!, path, out _)?.Conflict;
    }

    /// <summary>Whether any conflict descends strictly below <paramref name="path"/>.</summary>
    internal bool HasUnder(SparsePath path)
    {
        EnsureConflictGraph();
        var node = Descend(_conflictRoots!, path, out _);
        return node is not null && node.SubtreeConflicts - node.OwnConflicts > 0;
    }

    /// <summary>Whether a redacted conflict sits at or below <paramref name="path"/>.</summary>
    internal bool HasRedactedAtOrUnder(SparsePath path)
    {
        EnsureConflictGraph();
        return Descend(_conflictRoots!, path, out _)?.SubtreeRedacted > 0;
    }

    /// <summary>Whether any conflict sits at, above, or below <paramref name="path"/>.</summary>
    internal bool HasAffecting(SparsePath path)
    {
        EnsureConflictGraph();
        var node = Descend(_conflictRoots!, path, out var conflictAbove);
        return conflictAbove
            || node is not null
                && (node.Conflict is not null || node.SubtreeConflicts - node.OwnConflicts > 0);
    }

    /// <summary>Finds the deepest decision covering <paramref name="path"/>.</summary>
    /// <remarks>An exact decision is necessarily the deepest covering one, so it wins before any ancestor walk.</remarks>
    internal SparseResolutionDecision? Nearest(SparsePath path)
    {
        if (_exact.TryGetValue(path, out var exact))
        {
            return exact;
        }

        var roots = _decisionRoots;
        if (roots is null || !roots.TryGetValue(path.RootType, out var node))
        {
            return null;
        }

        var nearest = node.HasDecision ? node.Decision : null;
        var segments = path.Segments;
        for (var depth = 0; depth < segments.Count; depth++)
        {
            if (
                node.Children is null
                || !node.Children.TryGetValue(new SegmentSlot(segments[depth]), out node!)
            )
            {
                return nearest;
            }

            if (node.HasDecision)
            {
                nearest = node.Decision;
            }
        }

        return nearest;
    }

    /// <summary>Records the decision at <paramref name="path"/> (upsert).</summary>
    internal void Set(SparsePath path, SparseResolutionDecision decision)
    {
        _exact[path] = decision;
        var node = EnsureDecisionGraph(path.RootType);
        var segments = path.Segments;
        for (var depth = 0; depth < segments.Count; depth++)
        {
            var slot = new SegmentSlot(segments[depth]);
            node.Children ??= [];
            if (!node.Children.TryGetValue(slot, out var child))
            {
                child = new Node();
                node.Children[slot] = child;
            }

            node = child;
        }

        node.Decision = decision;
        node.HasDecision = true;
    }

    /// <summary>Drops the decision stored exactly at <paramref name="path"/>.</summary>
    /// <returns><see langword="true"/> when a decision was stored there.</returns>
    internal bool Clear(SparsePath path)
    {
        if (!_exact.Remove(path))
        {
            return false;
        }

        // The trie always mirrors exact registrations; a missing node only
        // guards against inconsistent reuse.
        if (
            _decisionRoots is not null
            && _decisionRoots.TryGetValue(path.RootType, out var node)
            && DescendDecisions(node, path) is Node target
        )
        {
            target.HasDecision = false;
            target.Decision = null;
        }

        return true;
    }

    /// <summary>Walks the decision trie to the node for <paramref name="path"/>.</summary>
    /// <returns>The node, or <see langword="null"/> when the path was never registered.</returns>
    private static Node? DescendDecisions(Node root, SparsePath path)
    {
        var node = root;
        var segments = path.Segments;
        for (var depth = 0; depth < segments.Count; depth++)
        {
            if (
                node.Children is null
                || !node.Children.TryGetValue(new SegmentSlot(segments[depth]), out var child)
            )
            {
                return null;
            }

            node = child;
        }

        return node;
    }

    /// <summary>Builds the immutable conflict graph on first use.</summary>
    private void EnsureConflictGraph()
    {
        if (_conflictRoots is not null)
        {
            return;
        }

        var roots = new Dictionary<Type, Node>();
        foreach (var conflict in _conflicts)
        {
            var path = conflict.Path;
            var node = EnsureRoot(roots, path.RootType);
            // The root node counts every conflict at or under its root type.
            node.SubtreeConflicts++;
            if (conflict.Kind == SparseConflictKind.RedactedBefore)
            {
                node.SubtreeRedacted++;
            }

            var segments = path.Segments;
            for (var depth = 0; depth < segments.Count; depth++)
            {
                var slot = new SegmentSlot(segments[depth]);
                node.Children ??= [];
                if (!node.Children.TryGetValue(slot, out var child))
                {
                    child = new Node();
                    node.Children[slot] = child;
                }

                node = child;
                node.SubtreeConflicts++;
                if (conflict.Kind == SparseConflictKind.RedactedBefore)
                {
                    node.SubtreeRedacted++;
                }
            }

            // First-record wins for shared paths; the ordered list stays
            // authoritative for enumeration and build choices.
            node.Conflict ??= conflict;
            node.OwnConflicts++;
        }

        _conflictRoots = roots;
    }

    private Node EnsureDecisionGraph(Type rootType)
    {
        _decisionRoots ??= [];
        if (!_decisionRoots.TryGetValue(rootType, out var node))
        {
            node = new Node();
            _decisionRoots[rootType] = node;
        }

        return node;
    }

    private static Node EnsureRoot(Dictionary<Type, Node> roots, Type rootType)
    {
        if (!roots.TryGetValue(rootType, out var node))
        {
            node = new Node();
            roots[rootType] = node;
        }

        return node;
    }

    /// <summary>Walks as deep as the graph reaches along <paramref name="path"/>.</summary>
    /// <param name="roots">The root nodes to search.</param>
    /// <param name="path">The query path.</param>
    /// <param name="conflictAbove">True when a visited ancestor node holds a conflict.</param>
    private static Node? Descend(
        Dictionary<Type, Node> roots,
        SparsePath path,
        out bool conflictAbove
    )
    {
        conflictAbove = false;
        if (!roots.TryGetValue(path.RootType, out var node))
        {
            return null;
        }

        var segments = path.Segments;
        for (var depth = 0; depth < segments.Count; depth++)
        {
            // Visited nodes sit strictly above the query path; record any
            // conflict held there so subtree and ancestor queries stay exact.
            if (node.Conflict is not null)
            {
                conflictAbove = true;
            }

            if (
                node.Children is null
                || !node.Children.TryGetValue(new SegmentSlot(segments[depth]), out node!)
            )
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>One branch point of a segment trie.</summary>
    private sealed class Node
    {
        internal Dictionary<SegmentSlot, Node>? Children;

        internal SparseConflict? Conflict;

        internal int OwnConflicts;

        internal int SubtreeConflicts;

        internal int SubtreeRedacted;

        internal SparseResolutionDecision? Decision;

        internal bool HasDecision;
    }

    /// <summary>Dictionary key derived from one path segment.</summary>
    /// <remarks>
    /// Mirrors <see cref="SparsePathSegment"/> identity: member names compare
    /// ordinally, keyed entries compare key type plus key, and temporary
    /// entries compare their stable identities. The public
    /// <c>Key</c>/<c>KeyType</c> accessors stay null for temporary segments,
    /// so those read the temporary identity explicitly; otherwise every
    /// temporary entry would share one slot.
    /// </remarks>
    private readonly struct SegmentSlot : IEquatable<SegmentSlot>
    {
        internal SegmentSlot(SparsePathSegment segment)
        {
            Kind = segment.Kind;
            Name = segment.Name;
            if (segment.Kind == SparsePathSegmentKind.TemporaryKey)
            {
                Key = segment.TemporaryKey;
                KeyType = typeof(Guid);
            }
            else
            {
                Key = segment.Key;
                KeyType = segment.KeyType;
            }

            Index = segment.Index;
        }

        internal SparsePathSegmentKind Kind { get; }

        internal string Name { get; }

        internal object? Key { get; }

        internal Type? KeyType { get; }

        internal int Index { get; }

        public bool Equals(SegmentSlot other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            return Kind switch
            {
                SparsePathSegmentKind.Member => string.Equals(
                    Name,
                    other.Name,
                    StringComparison.Ordinal
                ),
                SparsePathSegmentKind.Index => Index == other.Index,
                _ => KeyType == other.KeyType && Equals(Key, other.Key),
            };
        }

        public override bool Equals(object? obj) => obj is SegmentSlot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return Kind switch
                {
                    SparsePathSegmentKind.Member => ((int)Kind * 397)
                        ^ StringComparer.Ordinal.GetHashCode(Name),
                    SparsePathSegmentKind.Index => ((int)Kind * 397) ^ Index,
                    _ => ((int)Kind * 397)
                        ^ (KeyType?.GetHashCode() ?? 0)
                        ^ (Key?.GetHashCode() ?? 0),
                };
            }
        }
    }
}
