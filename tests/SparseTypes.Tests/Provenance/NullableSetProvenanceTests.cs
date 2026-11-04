using SparseTypes;
using SparseTypes.CompilerServices;

namespace SparseTypes.Tests.Provenance;

/// <summary>Set-union provenance must accept null elements like <see cref="HashSet{T}"/> does.</summary>
public sealed class NullableSetProvenanceTests
{
    [Test]
    public void MergeSetOriginsAttributesNullToFirstContribution()
    {
        var lower = new HashSet<string?> { null, "a" };
        var higher = new HashSet<string?> { "b", null };
        var effective = new HashSet<string?> { null, "a", "b" };

        var result = SparseFragmentRuntime.MergeSetOrigins<string?>(lower, ["l0", "l1"], "l", higher, ["h0", "h1"], "h", effective);

        result.ShouldBe(["l0", "l1", "h0"]);
    }

    [Test]
    public void MergeSetOriginsSupportsLazyEffectiveSequenceWithNull()
    {
        string?[] lower = ["a"];
        string?[] higher = [null, "a"];
        var effective = new EffectiveOnce(["a", null]);

        var result = SparseFragmentRuntime.MergeSetOrigins<string?>(lower, null, "l", higher, null, "h", effective);

        result.ShouldBe(["l", "h"]);
    }

    [Test]
    public void MergeSetOriginsKeepsComparerThatEquatesNullWithValue()
    {
        var comparer = new NullEqualsEmptyComparer();
        var lower = new HashSet<string?>(comparer) { "x" };
        var higher = new HashSet<string?>(comparer) { "" };
        var effective = new HashSet<string?>(comparer) { "x", "" };

        var result = SparseFragmentRuntime.MergeSetOrigins<string?>(lower, null, "l", higher, null, "h", effective);

        result.ShouldBe(["l", "h"]);
    }

    [Test]
    public void TryExplainSetProvenanceAttributesNullToFirstContribution()
    {
        var contributions = new[]
        {
            Optional<IEnumerable<string?>?>.Present(new List<string?> { "a", null }),
            Optional<IEnumerable<string?>?>.Present(new List<string?> { null, "b" }),
        };
        var effective = Optional<IEnumerable<string?>?>.Present(new List<string?> { "a", null, "b" });

        SparseFragmentRuntime.TryExplainSetProvenance(contributions, effective, out var origins, out var reason).ShouldBeTrue(reason);

        origins.ShouldBe([0, 0, 1]);
    }

    [Test]
    public void TryExplainSetProvenanceUsesEffectiveSetComparerWithNullElements()
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var lower = new HashSet<string?>(comparer) { null, "a" };
        var higher = new HashSet<string?>(comparer) { "A", "b", null };
        var effective = new HashSet<string?>(comparer) { null, "a", "b" };
        var contributions = new[]
        {
            Optional<IEnumerable<string?>?>.Present(lower),
            Optional<IEnumerable<string?>?>.Present(higher),
        };

        SparseFragmentRuntime.TryExplainSetProvenance(
            contributions,
            Optional<IEnumerable<string?>?>.Present(effective),
            out var origins,
            out var reason
        ).ShouldBeTrue(reason);

        origins.ShouldBe([0, 0, 1]);
    }

    [Test]
    public void TryExplainSetProvenanceRejectsEffectiveMissingNull()
    {
        var contributions = new[]
        {
            Optional<IEnumerable<string?>?>.Present(new List<string?> { "a" }),
            Optional<IEnumerable<string?>?>.Present(new List<string?> { null }),
        };
        var effective = Optional<IEnumerable<string?>?>.Present(new List<string?> { "a", "b" });

        SparseFragmentRuntime.TryExplainSetProvenance(contributions, effective, out _, out var reason).ShouldBeFalse();

        reason.ShouldNotBeNull();
    }

    [Test]
    public void TryExplainCollectionProvenanceSetUnionAttributesNullAndChecksOrder()
    {
        var contributions = new[]
        {
            Optional<IReadOnlyList<string?>?>.Present(new List<string?> { null, "a" }),
            Optional<IReadOnlyList<string?>?>.Present(new List<string?> { "a", null, "b" }),
        };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Optional<IReadOnlyList<string?>?>.Present(new List<string?> { null, "a", "b" }),
                null,
                out var origins,
                out var reason
            )
            .ShouldBeTrue(reason);
        origins.ShouldBe([0, 0, 1]);

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Optional<IReadOnlyList<string?>?>.Present(new List<string?> { "a", null, "b" }),
                null,
                out _,
                out reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void TryExplainCollectionProvenanceSetUnionSupportsNullableValueTypes()
    {
        var contributions = new[]
        {
            Optional<IReadOnlyList<int?>?>.Present(new List<int?> { null, 1 }),
            Optional<IReadOnlyList<int?>?>.Present(new List<int?> { 2, null }),
        };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Optional<IReadOnlyList<int?>?>.Present(new List<int?> { null, 1, 2 }),
                null,
                out var origins,
                out var reason
            )
            .ShouldBeTrue(reason);

        origins.ShouldBe([0, 0, 1]);
    }

    private sealed class EffectiveOnce(string?[] values) : IEnumerable<string?>
    {
        public IEnumerator<string?> GetEnumerator() => ((IEnumerable<string?>)values).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class NullEqualsEmptyComparer : IEqualityComparer<string?>
    {
        public bool Equals(string? x, string? y) => (x ?? "") == (y ?? "");

        public int GetHashCode(string? obj) => 0;
    }
}
