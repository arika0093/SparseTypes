namespace SparseTypes;

/// <summary>Segment-level path utilities for the mixed descriptor algebra.</summary>
/// <remarks>
/// Segments split on '.' with bracket keys kept on their element (quoted content
/// is never a boundary), matching the historical <c>SplitPathSegments</c> rules.
/// Segment boundaries are computed in two passes so splitting allocates exactly
/// one array plus one string per segment.
/// </remarks>
internal static class MixedPathSegments
{
    /// <summary>Splits a mixed member path into one string per segment.</summary>
    public static string[] Split(string path)
    {
        var segments = new string[CountSegments(path) + 1];
        var segmentIndex = 0;
        var segmentStart = 0;
        var depth = 0;
        var inQuotes = false;
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '"' && (i == 0 || path[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                }
                else if (c == '.' && depth == 0)
                {
                    segments[segmentIndex++] = path.Substring(segmentStart, i - segmentStart);
                    segmentStart = i + 1;
                }
            }
        }

        segments[segmentIndex] = path.Substring(segmentStart, path.Length - segmentStart);
        return segments;
    }

    /// <summary>Whether <paramref name="ancestor"/> is a strict segment prefix of <paramref name="descendant"/>.</summary>
    public static bool IsStrictAncestor(string[] ancestor, string[] descendant)
    {
        if (ancestor.Length >= descendant.Length)
        {
            return false;
        }

        for (var i = 0; i < ancestor.Length; i++)
        {
            if (!string.Equals(ancestor[i], descendant[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountSegments(string path)
    {
        var count = 0;
        var depth = 0;
        var inQuotes = false;
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '"' && (i == 0 || path[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                }
                else if (c == '.' && depth == 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}

/// <summary>Segment trie over mixed member operations for overlap lookups.</summary>
/// <remarks>
/// Entries keep their insertion ordinal so "first overlapping entry in
/// enumeration order" stays deterministic. Strict-descendant queries read a
/// per-node lazy minimum heap; tombstoned ordinals are dequeued when peeked,
/// so removals stay O(1) and disjoint-path lookups stay proportional to the
/// queried path's own segment chain.
/// </remarks>
internal sealed class MixedSegmentIndex
{
    private readonly Node _root = new();
    private readonly HashSet<int> _removed = new();
    private readonly Dictionary<int, Node> _byOrdinal = new();
    private int _nextOrdinal;

    internal sealed class Node
    {
        public Node? Parent;

        public Dictionary<string, Node>? Children;

        /// <summary>Ordinals of strict-descendant entries, minimum-first.</summary>
        public OrdinalHeap? Descendants;

        public MixedMemberOperation Value;

        public string Path = "";

        public string[] Segments = [];

        public int Ordinal = -1;
    }

    /// <summary>Minimum heap of entry ordinals with lazy stale skipping by the owner.</summary>
    internal sealed class OrdinalHeap
    {
        private int[] _items = new int[8];
        private int _count;

        public void Enqueue(int ordinal)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, checked(_count * 2));
            }

            _items[_count] = ordinal;
            SiftUp(_count++);
        }

        public bool TryPeek(out int ordinal)
        {
            if (_count == 0)
            {
                ordinal = 0;
                return false;
            }

            ordinal = _items[0];
            return true;
        }

        public void Dequeue()
        {
            _count--;
            _items[0] = _items[_count];
            SiftDown(0);
        }

        private void SiftUp(int start)
        {
            var position = start;
            while (position > 0)
            {
                var parent = (position - 1) >> 1;
                if (_items[parent] <= _items[position])
                {
                    break;
                }

                (_items[parent], _items[position]) = (_items[position], _items[parent]);
                position = parent;
            }
        }

        private void SiftDown(int start)
        {
            var position = start;
            while (true)
            {
                var child = 2 * position + 1;
                if (child >= _count)
                {
                    break;
                }

                if (child + 1 < _count && _items[child + 1] < _items[child])
                {
                    child++;
                }

                if (_items[position] <= _items[child])
                {
                    break;
                }

                (_items[position], _items[child]) = (_items[child], _items[position]);
                position = child;
            }
        }
    }

    /// <summary>Registers one member operation under its rendered path.</summary>
    public int Insert(string path, string[] segments, MixedMemberOperation value)
    {
        var node = WalkOrBuild(segments);
        var ordinal = _nextOrdinal++;
        node.Ordinal = ordinal;
        node.Value = value;
        node.Path = path;
        node.Segments = segments;
        _byOrdinal[ordinal] = node;
        for (var ancestor = node.Parent; ancestor is not null && !ReferenceEquals(ancestor, _root); ancestor = ancestor.Parent)
        {
            var heap = ancestor.Descendants ??= new OrdinalHeap();
            heap.Enqueue(ordinal);
        }

        return ordinal;
    }

    /// <summary>Removes the entry registered at an exact path, if present.</summary>
    public void Remove(string path, string[] segments)
    {
        var node = Walk(segments);
        if (node is not null)
        {
            RemoveNode(node);
        }
    }

    /// <summary>Tombstones one indexed entry, idempotent also for prefix-only nodes.</summary>
    public void RemoveNode(Node node)
    {
        if (node.Ordinal < 0)
        {
            return;
        }

        _removed.Add(node.Ordinal);
        node.Ordinal = -1;
    }

    /// <summary>Returns the terminal entry node at an exact path, or null.</summary>
    public Node? GetTerminal(string path) => Walk(MixedPathSegments.Split(path)) is { } node && node.Ordinal >= 0 ? node : null;

    /// <summary>
    /// Finds the first inserted entry that is a strict ancestor or strict
    /// descendant of the rendered segments.
    /// </summary>
    /// <remarks>The full-path entry itself is excluded, mirroring ordinal path equality.</remarks>
    public Node? FindOverlap(string[] segments)
    {
        Node? best = null;
        var bestOrdinal = int.MaxValue;
        var node = _root;
        var last = segments.Length - 1;
        for (var depth = 0; depth <= last; depth++)
        {
            var children = node.Children;
            if (children is null || !children.TryGetValue(segments[depth], out var child))
            {
                return best;
            }

            node = child;
            if (depth < last && node.Ordinal >= 0 && node.Ordinal < bestOrdinal)
            {
                bestOrdinal = node.Ordinal;
                best = node;
            }
        }

        if (node.Descendants is not null)
        {
            while (node.Descendants.TryPeek(out var ordinal))
            {
                var heap = node.Descendants;
                if (_removed.Contains(ordinal))
                {
                    heap.Dequeue();
                    continue;
                }

                if (ordinal < bestOrdinal && _byOrdinal.TryGetValue(ordinal, out var winner))
                {
                    best = winner;
                }

                break;
            }
        }

        return best;
    }

    private Node WalkOrBuild(string[] segments)
    {
        var node = _root;
        foreach (var segment in segments)
        {
            var children = node.Children;
            if (children is null)
            {
                children = new Dictionary<string, Node>(StringComparer.Ordinal);
                node.Children = children;
            }

            if (!children.TryGetValue(segment, out var child))
            {
                child = new Node { Parent = node };
                children.Add(segment, child);
            }

            node = child;
        }

        return node;
    }

    private Node? Walk(string[] segments)
    {
        var node = _root;
        foreach (var segment in segments)
        {
            var children = node.Children;
            if (children is null || !children.TryGetValue(segment, out var child))
            {
                return null;
            }

            node = child;
        }

        return node;
    }
}
