using SparseTypes.CompilerServices;

namespace SparseTypes.Tests.Comparison;

/// <summary>Key/value sequences without a dictionary view must still honor the supplied value comparer.</summary>
public sealed class PairSequenceValueComparerTests
{
    private static bool SameLength(string left, string right) => left.Length == right.Length;

    private static bool Never(string left, string right) => false;

    private static IEnumerable<KeyValuePair<string, string>> Iterate(params KeyValuePair<string, string>[] pairs)
    {
        foreach (var pair in pairs)
        {
            yield return pair;
        }
    }

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    [Test]
    public void CustomComparerAcceptsValuesThatDifferByDefault()
    {
        KeyValuePair<string, string>[] left = [Pair("a", "xx"), Pair("b", "yyy")];
        List<KeyValuePair<string, string>> right = [Pair("b", "zzz"), Pair("a", "qq")];

        SparseCoreRuntime.AreDictionaryEqual(left, right).ShouldBeFalse();
        SparseCoreRuntime.AreDictionaryEqual(left, right, SameLength).ShouldBeTrue();
        SparseCoreRuntime.AreDictionaryEqual(Iterate([.. left]), Iterate([.. right]), SameLength).ShouldBeTrue();
    }

    [Test]
    public void CustomComparerRejectsValuesThatAreDefaultEqual()
    {
        KeyValuePair<string, string>[] left = [Pair("a", "x")];
        KeyValuePair<string, string>[] right = [Pair("a", "x")];

        SparseCoreRuntime.AreDictionaryEqual(left, right).ShouldBeTrue();
        SparseCoreRuntime.AreDictionaryEqual(left, right, Never).ShouldBeFalse();
    }

    [Test]
    public void DuplicateKeysAreMatchedAsAMultiset()
    {
        KeyValuePair<string, string>[] left = [Pair("a", "x"), Pair("a", "yy")];
        KeyValuePair<string, string>[] reordered = [Pair("a", "ww"), Pair("a", "z")];
        KeyValuePair<string, string>[] unmatched = [Pair("a", "z"), Pair("a", "q")];

        SparseCoreRuntime.AreDictionaryEqual(left, reordered, SameLength).ShouldBeTrue();
        SparseCoreRuntime.AreDictionaryEqual(left, unmatched, SameLength).ShouldBeFalse();
    }
}
