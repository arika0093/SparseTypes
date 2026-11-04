using SparseTypes;

namespace SparseTypes.Tests;

[SparseModel]
public partial class InterfaceSetSettings
{
    public ISet<string> Mutable { get; set; } = new HashSet<string>();
    public IReadOnlySet<string> ReadOnly { get; set; } = new HashSet<string>();
}

public sealed class InterfaceSetCloneTests
{
    [Test]
    public void SortedSetInterfaceClonePreservesOrderingComparerAndAliases()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "b", "A", "c" };
        var model = new InterfaceSetSettings { Mutable = set, ReadOnly = set };
        var clone = model.DeepClone().DeepClone();
        ReferenceEquals(clone.Mutable, clone.ReadOnly).ShouldBeTrue();
        var sorted = clone.Mutable.ShouldBeOfType<SortedSet<string>>();
        ReferenceEquals(sorted.Comparer, set.Comparer).ShouldBeTrue();
        sorted.ShouldBe(new[] { "A", "b", "c" });
        sorted.Contains("B").ShouldBeTrue();
        sorted.Add("a").ShouldBeFalse();

        var readOnlySorted = clone.ReadOnly.ShouldBeOfType<SortedSet<string>>();
        ReferenceEquals(readOnlySorted, sorted).ShouldBeTrue();
        ReferenceEquals(readOnlySorted.Comparer, set.Comparer).ShouldBeTrue();
        set.Count.ShouldBe(3);
    }

    [Test]
    public void SortedSetInterfaceSurvivesFragmentRoundTrip()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "b", "A" };
        var model = new InterfaceSetSettings { Mutable = set, ReadOnly = set };
        var restored = InterfaceSetSettings.Fragment.From(model).ToModel();
        var sorted = restored.Mutable.ShouldBeOfType<SortedSet<string>>();
        ReferenceEquals(sorted.Comparer, set.Comparer).ShouldBeTrue();
        sorted.Contains("B").ShouldBeTrue();
        sorted.ShouldBe(new[] { "A", "b" });

        var readOnlySorted = restored.ReadOnly.ShouldBeOfType<SortedSet<string>>();
        ReferenceEquals(readOnlySorted, sorted).ShouldBeTrue();
        ReferenceEquals(readOnlySorted.Comparer, set.Comparer).ShouldBeTrue();
    }
}
