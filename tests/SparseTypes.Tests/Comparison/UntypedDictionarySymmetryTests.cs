using SparseTypes.CompilerServices;

namespace SparseTypes.Tests.Comparison;

/// <summary>Untyped dictionary equality must not depend on operand order when key comparers partition keys differently.</summary>
public sealed class UntypedDictionarySymmetryTests
{
    private sealed class UpperCaseClassComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => IsUpper(x) == IsUpper(y);

        public int GetHashCode(string value) => IsUpper(value) ? 1 : 0;

        private static bool IsUpper(string? value) => value is { Length: > 0 } && char.IsUpper(value[0]);
    }

    private static Dictionary<string, int> CrossedLeft(int a = 1, int b = 2) =>
        new(StringComparer.OrdinalIgnoreCase) { ["a"] = a, ["B"] = b };

    private static Dictionary<string, int> CrossedRight(int lower = 1, int upper = 2) =>
        new(new UpperCaseClassComparer()) { ["b"] = lower, ["A"] = upper };

    [Test]
    public void CrossedKeyPartitionsAreSymmetricThroughCoreAndFacadeRuntimes()
    {
        var left = CrossedLeft();
        var right = CrossedRight();

        var forward = SparseCoreRuntime.AreEqual((object)left, (object)right);
        SparseCoreRuntime.AreEqual((object)right, (object)left).ShouldBe(forward);
        SparseFragmentRuntime.AreEqual((object)left, (object)right).ShouldBe(forward);
        SparseFragmentRuntime.AreEqual((object)right, (object)left).ShouldBe(forward);
        forward.ShouldBeFalse();
    }

    [Test]
    public void NestedValuesUnderCrossedKeyPartitionsAreSymmetric()
    {
        var left = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new List<int> { 1 },
            ["B"] = new List<int> { 2 },
        };
        var right = new Dictionary<string, object>(new UpperCaseClassComparer())
        {
            ["b"] = new List<int> { 1 },
            ["A"] = new List<int> { 2 },
        };

        SparseCoreRuntime.AreEqual((object)right, (object)left).ShouldBe(SparseCoreRuntime.AreEqual((object)left, (object)right));
    }

    [Test]
    public void MatchingAndDifferingDictionariesKeepOrdinaryEquality()
    {
        SparseCoreRuntime.AreEqual((object)CrossedLeft(), (object)CrossedLeft()).ShouldBeTrue();
        SparseCoreRuntime.AreEqual((object)CrossedRight(), (object)CrossedRight()).ShouldBeTrue();
        SparseCoreRuntime.AreEqual((object)CrossedLeft(), (object)CrossedLeft(b: 3)).ShouldBeFalse();
        SparseCoreRuntime.AreEqual((object)CrossedRight(), (object)CrossedRight(upper: 3)).ShouldBeFalse();
    }
}
