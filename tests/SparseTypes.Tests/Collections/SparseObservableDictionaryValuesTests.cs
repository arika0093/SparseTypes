namespace SparseTypes.Tests;

public sealed class SparseObservableDictionaryValuesTests
{
    private static SparseObservableDictionary<string, string, string> Create(
        IDictionary<string, string> model
    ) => new(model, static (value, _) => value, static view => view, static () => { });

    [Test]
    public void Values_ReusesOneViewAcrossBothDictionaryInterfaces()
    {
        var dictionary = Create(new Dictionary<string, string> { ["a"] = "one" });
        IReadOnlyDictionary<string, string> readOnly = dictionary;

        ReferenceEquals(dictionary.Values, dictionary.Values).ShouldBeTrue();
        ReferenceEquals(dictionary.Values, readOnly.Values).ShouldBeTrue();
    }

    [Test]
    public void RetainedValues_SeesDirectBackingDictionaryChanges()
    {
        var model = new Dictionary<string, string> { ["a"] = "one" };
        var retained = Create(model).Values;

        model.Add("b", "two");

        retained.Count.ShouldBe(2);
        retained.ShouldContain("one");
        retained.ShouldContain("two");
    }

    [Test]
    public void RetainedValues_SeesAddSetRemoveAndClear()
    {
        var dictionary = Create(new Dictionary<string, string> { ["a"] = "one" });
        var retained = dictionary.Values;

        dictionary.Add("b", "two");
        retained.Count.ShouldBe(2);

        dictionary["a"] = "updated";
        retained.ShouldContain("updated");

        dictionary.Remove("b").ShouldBeTrue();
        retained.Count.ShouldBe(1);

        dictionary.Clear();
        retained.Count.ShouldBe(0);
        retained.ShouldBeEmpty();
    }

    [Test]
    public void Values_RejectsMutations()
    {
        var values = Create(new Dictionary<string, string> { ["a"] = "one" }).Values;

        values.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => values.Add("x"));
        Should.Throw<NotSupportedException>(() => values.Clear());
        Should.Throw<NotSupportedException>(() => values.Remove("one"));
    }

    [Test]
    public void Values_EnumeratorsAreIndependent()
    {
        var values = Create(new Dictionary<string, string> { ["a"] = "one", ["b"] = "two" }).Values;

        using var first = values.GetEnumerator();
        using var second = values.GetEnumerator();

        first.MoveNext().ShouldBeTrue();
        var secondSeen = new List<string>();
        while (second.MoveNext())
        {
            secondSeen.Add(second.Current);
        }

        secondSeen.Count.ShouldBe(2);

        // The first enumerator keeps its own position: exactly one item left,
        // drawn from the same contents the second enumerator observed.
        var firstRest = new List<string>();
        while (first.MoveNext())
        {
            firstRest.Add(first.Current);
        }

        firstRest.Count.ShouldBe(1);
        secondSeen.ShouldContain(firstRest[0]);
    }

    [Test]
    public void RetainedValues_KeepsLiveSemanticsAfterDisposal()
    {
        var dictionary = Create(new Dictionary<string, string> { ["a"] = "one" });
        var retained = dictionary.Values;

        dictionary.Dispose();

        ReferenceEquals(retained, dictionary.Values).ShouldBeTrue();
        retained.Count.ShouldBe(1);
        retained.ShouldContain("one");
    }
}
