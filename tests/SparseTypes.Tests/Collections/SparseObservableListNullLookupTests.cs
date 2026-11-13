using System.Collections;

namespace SparseTypes.Tests;

public sealed class SparseObservableListNullLookupTests
{
    private sealed class StringView(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class IntView(int value)
    {
        public int Value { get; } = value;
    }

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
    public void NullViewLookup_DoesNotInvokeNullUnsafeUnwrap()
    {
        var list = new SparseObservableList<string?, StringView?>(
            new List<string?> { null, "a" },
            static (value, _) => value is null ? null : new StringView(value),
            static view => view!.Value,
            static () => { }
        );
        IList raw = list;

        list.IndexOf(null).ShouldBe(0);
        list.Contains(null).ShouldBeTrue();
        raw.IndexOf(null).ShouldBe(0);
        raw.Contains(null).ShouldBeTrue();

        list.Remove(null).ShouldBeTrue();
        list.Count.ShouldBe(1);
    }

    [Test]
    public void NullViewLookup_DoesNotMatchDefaultOfNonNullableModel()
    {
        var list = new SparseObservableList<int, IntView?>(
            new List<int> { 0, 1 },
            static (value, _) => new IntView(value),
            static view => view!.Value,
            static () => { }
        );

        list.IndexOf(null).ShouldBe(-1);
        list.Contains(null).ShouldBeFalse();
        list.Remove(null).ShouldBeFalse();
        list.Count.ShouldBe(2);
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
