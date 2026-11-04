using System.Collections;

namespace SparseTypes.Tests;

public sealed class SparseObservableListNullLookupTests
{
    private static SparseObservableList<TItem, TItem> Create<TItem>(IList<TItem> model) =>
        new(model, static (value, _) => value, static view => view, static () => { });

    [Test]
    public void NonGenericLookup_FindsAndRemovesNullForNullableViews()
    {
        var list = Create<string?>(new List<string?> { "a", null });
        IList raw = list;

        raw.Contains(null).ShouldBeTrue();
        raw.IndexOf(null).ShouldBe(1);
        raw.Remove(null);
        list.Count.ShouldBe(1);

        var numbers = Create<int?>(new List<int?> { 1, null });
        IList rawNumbers = numbers;
        rawNumbers.IndexOf(null).ShouldBe(1);
    }

    [Test]
    public void NonGenericLookup_RejectsNullForNonNullableValueViews()
    {
        var list = Create<int>(new List<int> { 0, 1 });
        IList raw = list;

        raw.Contains(null).ShouldBeFalse();
        raw.IndexOf(null).ShouldBe(-1);
        raw.Remove(null);
        raw.Contains("x").ShouldBeFalse();
        list.Count.ShouldBe(2);
    }
}
