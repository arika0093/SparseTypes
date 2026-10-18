using SparseTypes;
using SparseTypes.CompilerServices;

namespace SparseTypes.Tests.Provenance;

/// <summary>Live origin-merger helpers exercised through the public runtime facade.</summary>
/// <remarks>
/// Covers no-materialization inputs (collections with known counts), single-
/// enumeration lazy sources, duplicate positions, missing/short origin
/// entries, comparer mismatch, and empty inputs.
/// </remarks>
public sealed class OriginMergerRuntimeTests
{
    [Test]
    public void MergeAppendOriginsUsesCollectionCountsWithoutEnumerating()
    {
        var lower = new NonEnumeratingCollection { "a", "b" };
        var higher = new NonEnumeratingCollection { "c" };

        var result = SparseFragmentRuntime.MergeAppendOrigins(
            lower,
            ["l0", "l1"],
            "lower",
            higher,
            null,
            "higher"
        );

        result.ShouldBe(["l0", "l1", "higher"]);
    }

    [Test]
    public void MergeAppendOriginsTracksLargeStructPayloadsByPosition()
    {
        var lower = new List<WideStruct> { new(1), new(2) };
        var higher = new LinkedList<WideStruct>();
        higher.AddLast(new WideStruct(3, 34, 35, 36, 3));

        var result = SparseFragmentRuntime.MergeAppendOrigins(
            lower,
            null,
            "l",
            higher,
            new[] { "h" },
            "?"
        );

        result.ShouldBe(["l", "l", "h"]);
    }

    [Test]
    public void MergeAppendOriginsCountsLazySourcesOnceWithoutRetention()
    {
        var lower = new EnumerateOnce(new[] { "a", "b" });
        var higher = new EnumerateOnce(new[] { "c" });

        var result = SparseFragmentRuntime.MergeAppendOrigins(
            lower,
            ["l0", "l1"],
            "lower",
            higher,
            null,
            "higher"
        );

        result.ShouldBe(["l0", "l1", "higher"]);
        lower.EnumerationCount.ShouldBe(1);
        higher.EnumerationCount.ShouldBe(1);
    }

    [Test]
    public void MergeSetUnionOriginsAttributesFirstAcceptedPositionsWithDuplicates()
    {
        // Duplicate elements advance the position even though nothing is
        // emitted, so origin entries stay aligned with source positions.
        var lower = new EnumerateOnce(new[] { "a", "b", "a" });
        var higher = new EnumerateOnce(new[] { "b", "c" });

        var result = SparseFragmentRuntime.MergeSetUnionOrigins(
            lower,
            ["l0", "l1", "l2"],
            "lower",
            higher,
            ["h0", "h1"],
            "higher",
            StringComparer.Ordinal
        );

        result.ShouldBe(["l0", "l1", "h1"]);
        lower.EnumerationCount.ShouldBe(1);
        higher.EnumerationCount.ShouldBe(1);
    }

    [Test]
    public void MergeSetOriginsFollowsEffectiveOrderWithKnownCounts()
    {
        var lower = new HashSet<string>(["a", "b"]);
        var higher = new HashSet<string>(["b", "c"]);
        var effective = new HashSet<string>(["a", "b", "c"]);

        var result = SparseFragmentRuntime.MergeSetOrigins(
            lower,
            ["l0", "l1"],
            "lower",
            higher,
            null,
            "higher",
            effective
        );

        result.ShouldBe(
            ["l0", "l1", "higher"],
            "primary order must match a HashSet insertion-order snapshot"
        );
    }

    [Test]
    public void MergeSetOriginsSkipsPayloadCopyOnComparerMismatch()
    {
        var lower = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
        var higher = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "b" };
        var effective = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };

        // Effective and active comparers disagree: attribution becomes
        // unknown for every effective element without inspecting payloads.
        var result = SparseFragmentRuntime.MergeSetOrigins(
            lower,
            ["l0"],
            "lower",
            higher,
            null,
            "higher",
            effective
        );

        result.ShouldBe([null, null]);
    }

    [Test]
    public void MergeSetOriginsBuffersOriginsOnlyForCountUnknownEffective()
    {
        var lower = new EnumerateOnce(new[] { "a", "b" });
        var higher = new EnumerateOnce(new[] { "b", "c" });
        var effective = new EnumerateOnce(new[] { "a", "b", "c" });

        var result = SparseFragmentRuntime.MergeSetOrigins(
            lower,
            ["l0", "l1"],
            "lower",
            higher,
            ["h0", "h1"],
            "higher",
            effective
        );

        result.ShouldBe(["l0", "l1", "h1"]);
        lower.EnumerationCount.ShouldBe(1);
        higher.EnumerationCount.ShouldBe(1);
        // Count discovery plus the effective pass must not rely on a second
        // forward pass over the same source.
        effective.EnumerationCount.ShouldBe(1);
    }

    [Test]
    public void MergeSetUnionOriginsHandlesEmptyAndShortOriginEntries()
    {
        var lower = new HashSet<string>(["a", "b"]);
        var higher = new HashSet<string>();

        var result = SparseFragmentRuntime.MergeSetUnionOrigins(
            lower,
            Array.Empty<string?>(),
            "fallback",
            higher,
            null,
            "higher",
            StringComparer.Ordinal
        );

        result.ShouldBe(["fallback", "fallback"]);
    }

    [Test]
    public void MergeAppendOriginsHandlesEmptyInputs()
    {
        var result = SparseFragmentRuntime.MergeAppendOrigins(
            new List<string>(),
            null,
            "l",
            new HashSet<string>(),
            null,
            "h"
        );

        result.ShouldBe([]);
    }

    [Test]
    public void MergeSetOriginsUsesDefaultComparerWithoutDeclaredComparer()
    {
        var lower = new List<string> { "a" };
        var higher = new List<string> { "b" };
        var effective = new LinkedList<string>(["a", "b"]);

        var result = SparseFragmentRuntime.MergeSetOrigins(
            lower,
            ["l0"],
            "lower",
            higher,
            ["h0"],
            "higher",
            effective
        );

        result.ShouldBe(["l0", "h0"]);
    }

    /// <summary>A collection whose element enumeration is forbidden for parsers that only need counts.</summary>
    private sealed class NonEnumeratingCollection : ICollection<string>
    {
        private readonly List<string> _items = [];

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(string item) => _items.Add(item);

        public void Clear() => _items.Clear();

        public bool Contains(string item) => _items.Contains(item);

        public void CopyTo(string[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public bool Remove(string item) => _items.Remove(item);

        public IEnumerator<string> GetEnumerator() =>
            throw new InvalidOperationException(
                "Append origin discovery must not enumerate known-count inputs."
            );

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    /// <summary>A lazy source that permits exactly one forward pass, guarding reproducibility.</summary>
    private sealed class EnumerateOnce : IEnumerable<string>
    {
        private readonly string[] _items;

        public EnumerateOnce(string[] items)
        {
            _items = items;
        }

        public int EnumerationCount { get; private set; }

        public IEnumerator<string> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Only one forward pass is allowed.");
            }

            return _items.AsReadOnly().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private readonly record struct WideStruct(long A, long B, long C, long D, int Tag)
    {
        public WideStruct(int tag)
            : this(tag, tag, tag, tag, tag) { }
    }
}
