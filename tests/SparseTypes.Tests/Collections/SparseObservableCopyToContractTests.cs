using System.Collections;

namespace SparseTypes.Tests;

public sealed class SparseObservableCopyToContractTests
{
    private static SparseObservableList<TItem, TItem> Create<TItem>(IList<TItem> model) =>
        new(model, static (value, _) => value, static view => view, static () => { });

    [Test]
    public void CopyTo_RejectsInvalidArgumentsWithoutMutatingDestination()
    {
        var empty = Create<string>(new List<string>());
        Should.Throw<ArgumentOutOfRangeException>(() => empty.CopyTo([], -1));
        Should.Throw<ArgumentOutOfRangeException>(() => ((ICollection)empty).CopyTo(Array.Empty<string>(), -1));

        var list = Create<string>(new List<string> { "a", "b", "c" });
        var destination = new[] { "x", "y", "z", "w" };
        Should.Throw<ArgumentException>(() => list.CopyTo(destination, 2));
        Should.Throw<ArgumentException>(() => ((ICollection)list).CopyTo(destination, 2));
        destination.ShouldBe(["x", "y", "z", "w"]);
    }

    [Test]
    public void NonGenericCopyTo_RejectsInvalidRankLowerBoundAndType()
    {
        var list = Create<string>(new List<string> { "a" });
        ICollection raw = list;

        Should.Throw<RankException>(() => raw.CopyTo(new string[1, 1], 0));
        Should.Throw<ArgumentException>(() => raw.CopyTo(Array.CreateInstance(typeof(string), [1], [1]), 1));
        var wrongType = new int[1];
        Should.Throw<ArgumentException>(() => raw.CopyTo(wrongType, 0));
        wrongType[0].ShouldBe(0);

        var objects = new object?[1];
        raw.CopyTo(objects, 0);
        objects[0].ShouldBe("a");
    }

    [Test]
    public void NonGenericCopyTo_CopiesIntoExactViewArray()
    {
        var list = Create<string>(new List<string> { "a", "b" });
        var destination = new[] { "x", "y", "z" };

        ((ICollection)list).CopyTo(destination, 1);

        destination.ShouldBe(["x", "a", "b"]);
    }

    [Test]
    public void NonGenericCopyTo_DoesNotMutateDestinationWhenWrappingFails()
    {
        var list = new SparseObservableList<string, string>(
            new List<string> { "a", "b" },
            static (value, _) => value == "b" ? throw new InvalidOperationException() : value,
            static view => view,
            static () => { }
        );
        var destination = new[] { "x", "y" };

        Should.Throw<InvalidOperationException>(() => ((ICollection)list).CopyTo(destination, 0));

        destination.ShouldBe(["x", "y"]);
    }
}
