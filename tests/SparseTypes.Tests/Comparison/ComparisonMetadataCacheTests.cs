using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using SparseTypes.CompilerServices;

namespace SparseTypes.Tests;

public sealed class ComparisonMetadataCacheTests
{
    private static readonly object DiscoveryGate = new();

    private static readonly Type PrimitivesType =
        typeof(SparseFragmentRuntime).Assembly.GetType("SparseTypes.FragmentComparisonPrimitives")
        ?? throw new InvalidOperationException("Missing FragmentComparisonPrimitives.");

    private static readonly PropertyInfo DiscoveryObserverProperty =
        PrimitivesType.GetProperty(
            "DiscoveryObserver",
            BindingFlags.Static | BindingFlags.NonPublic
        ) ?? throw new InvalidOperationException("Missing DiscoveryObserver probe.");

    private static int CountDiscoveries(Type type, string name, Action probe)
    {
        var count = 0;
        Action<Type, string> observer = (discovered, property) =>
        {
            if (discovered == type && property == name)
            {
                Interlocked.Increment(ref count);
            }
        };
        lock (DiscoveryGate)
        {
            var previous = DiscoveryObserverProperty.GetValue(null);
            DiscoveryObserverProperty.SetValue(null, observer);
            try
            {
                probe();
            }
            finally
            {
                DiscoveryObserverProperty.SetValue(null, previous);
            }
        }

        return count;
    }

    private static bool Equal(object left, object right) =>
        SparseFragmentRuntime.AreEqual(left, right);

    private static Dictionary<string, int> Entries(params (string Key, int Value)[] pairs)
    {
        var inner = new Dictionary<string, int>();
        foreach (var (key, value) in pairs)
        {
            inner[key] = value;
        }

        return inner;
    }

    [Test]
    public void MissingComparer_IsDiscoveredOnceAcrossDistinctInstances()
    {
        var discoveries = CountDiscoveries(
            typeof(ComparerlessDictionary),
            "Comparer",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new ComparerlessDictionary(Entries(("a", 1), ("b", 2)));
                    var same = new ComparerlessDictionary(Entries(("b", 2), ("a", 1)));
                    var changed = new ComparerlessDictionary(Entries(("a", 1), ("b", 3)));

                    Equal(left, same).ShouldBeTrue();
                    Equal(same, left).ShouldBeTrue();
                    Equal(left, changed).ShouldBeFalse();
                    Equal(changed, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void WriteOnlyComparer_IsCachedAsMiss()
    {
        var discoveries = CountDiscoveries(
            typeof(WriteOnlyComparerDictionary),
            "Comparer",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new WriteOnlyComparerDictionary(Entries(("a", 1)));
                    var same = new WriteOnlyComparerDictionary(Entries(("a", 1)));
                    var changed = new WriteOnlyComparerDictionary(Entries(("a", 2)));

                    Equal(left, same).ShouldBeTrue();
                    Equal(left, changed).ShouldBeFalse();
                    Equal(changed, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void IndexerNamedComparer_IsCachedAsMiss()
    {
        var discoveries = CountDiscoveries(
            typeof(RenamedIndexerSet),
            "Comparer",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new RenamedIndexerSet(["a", "b"]);
                    var same = new RenamedIndexerSet(["b", "a"]);
                    var changed = new RenamedIndexerSet(["a", "c"]);

                    Equal(left, same).ShouldBeTrue();
                    Equal(same, left).ShouldBeTrue();
                    Equal(left, changed).ShouldBeFalse();
                    Equal(changed, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void WrongTypeCount_IsCachedAsMiss()
    {
        var discoveries = CountDiscoveries(
            typeof(TextCountDictionary),
            "Count",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new TextCountDictionary(Entries(("a", 1), ("b", 2)));
                    var same = new TextCountDictionary(Entries(("b", 2), ("a", 1)));
                    var changed = new TextCountDictionary(Entries(("a", 1)));

                    Equal(left, same).ShouldBeTrue();
                    Equal(same, left).ShouldBeTrue();
                    Equal(left, changed).ShouldBeFalse();
                    Equal(changed, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void ExplicitOnlyCount_IsCachedAsMiss()
    {
        var discoveries = CountDiscoveries(
            typeof(ExplicitCountDictionary),
            "Count",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new ExplicitCountDictionary(Entries(("a", 1)));
                    var same = new ExplicitCountDictionary(Entries(("a", 1)));
                    var changed = new ExplicitCountDictionary(Entries(("a", 2)));

                    Equal(left, same).ShouldBeTrue();
                    Equal(left, changed).ShouldBeFalse();
                    Equal(changed, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void AmbiguousComparer_IsCachedWithoutEscaping()
    {
        // Guard: the fixture really is ambiguous through the same lookup the
        // cache performs.
        Should.Throw<AmbiguousMatchException>(() =>
            typeof(AmbiguousComparerDictionary).GetProperty(
                "Comparer",
                BindingFlags.Public | BindingFlags.Instance
            )
        );

        var discoveries = CountDiscoveries(
            typeof(AmbiguousComparerDictionary),
            "Comparer",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new AmbiguousComparerDictionary(Entries(("a", 1)));
                    var same = new AmbiguousComparerDictionary(Entries(("a", 1)));
                    var changed = new AmbiguousComparerDictionary(Entries(("a", 2)));

                    Equal(left, same).ShouldBeTrue();
                    Equal(left, changed).ShouldBeFalse();
                    Equal(changed, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void AmbiguousCount_IsCachedWithoutEscaping()
    {
        Should.Throw<AmbiguousMatchException>(() =>
            typeof(AmbiguousCountDictionary).GetProperty(
                "Count",
                BindingFlags.Public | BindingFlags.Instance
            )
        );

        var discoveries = CountDiscoveries(
            typeof(AmbiguousCountDictionary),
            "Count",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new AmbiguousCountDictionary(Entries(("a", 1), ("b", 2)));
                    var same = new AmbiguousCountDictionary(Entries(("b", 2), ("a", 1)));
                    var changed = new AmbiguousCountDictionary(Entries(("a", 1)));

                    Equal(left, same).ShouldBeTrue();
                    Equal(left, changed).ShouldBeFalse();
                    Equal(changed, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void PositiveComparer_IsDiscoveredOnceAndStillHonored()
    {
        var discoveries = CountDiscoveries(
            typeof(PositiveComparerDictionary),
            "Comparer",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var left = new PositiveComparerDictionary(
                        Entries(("first", 1), ("second", 2)),
                        StringComparer.OrdinalIgnoreCase
                    );
                    var reordered = new PositiveComparerDictionary(
                        Entries(("SECOND", 2), ("FIRST", 1)),
                        StringComparer.OrdinalIgnoreCase
                    );
                    var ordinal = new PositiveComparerDictionary(
                        Entries(("first", 1), ("second", 2)),
                        StringComparer.Ordinal
                    );

                    Equal(left, reordered).ShouldBeTrue();
                    Equal(reordered, left).ShouldBeTrue();
                    Equal(left, ordinal).ShouldBeFalse();
                    Equal(ordinal, left).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
    }

    [Test]
    public void ThrowingComparerGetter_ValueIsNotCachedWhileDiscoveryIs()
    {
        ThrowingComparerDictionary? throwing = null;
        ThrowingComparerDictionary? healthy = null;
        var discoveries = CountDiscoveries(
            typeof(ThrowingComparerDictionary),
            "Comparer",
            () =>
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    throwing = new ThrowingComparerDictionary(
                        Entries(("a", 1)),
                        throwOnAccess: true
                    );
                    healthy = new ThrowingComparerDictionary(
                        Entries(("a", 1)),
                        throwOnAccess: false
                    );
                    var changed = new ThrowingComparerDictionary(
                        Entries(("a", 2)),
                        throwOnAccess: false
                    );

                    Equal(throwing, healthy).ShouldBeTrue();
                    Equal(healthy, throwing).ShouldBeTrue();
                    Equal(throwing, changed).ShouldBeFalse();
                }
            }
        );

        discoveries.ShouldBe(1);
        throwing.ShouldNotBeNull();
        healthy.ShouldNotBeNull();
        // The getter runs on every comparison; only its metadata is cached.
        (throwing!.ComparerCalls + healthy!.ComparerCalls).ShouldBeGreaterThan(3);

        throwing.ThrowOnAccess = false;
        Equal(throwing, healthy).ShouldBeTrue();
    }

    [Test]
    public void OpaqueEntries_KeyAndValueMissesAreCached()
    {
        var keyCount = 0;
        var valueCount = 0;
        Action<Type, string> observer = (discovered, property) =>
        {
            if (discovered == typeof(OpaqueEntry))
            {
                if (property == "Key")
                {
                    Interlocked.Increment(ref keyCount);
                }
                else if (property == "Value")
                {
                    Interlocked.Increment(ref valueCount);
                }
            }
        };
        lock (DiscoveryGate)
        {
            var previous = DiscoveryObserverProperty.GetValue(null);
            DiscoveryObserverProperty.SetValue(null, observer);
            try
            {
                CompareOpaqueEntries();
            }
            finally
            {
                DiscoveryObserverProperty.SetValue(null, previous);
            }
        }

        keyCount.ShouldBe(1);
        valueCount.ShouldBe(1);
    }

    private static void CompareOpaqueEntries()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var left = new KeylessEntryDictionary(Entries(("a", 1)));
            var same = new KeylessEntryDictionary(Entries(("b", 2)));
            var shorter = new KeylessEntryDictionary(new Dictionary<string, int>());

            // Entries expose no keys or values, so equal counts compare
            // vacuously while differing counts stay unequal.
            Equal(left, same).ShouldBeTrue();
            Equal(left, shorter).ShouldBeFalse();
        }
    }

    [Test]
    public void ConcurrentComparisons_StayCorrectWithoutRequiringSingleDiscovery()
    {
        const int width = 64;
        var results = new bool[width];
        Parallel.For(
            0,
            width,
            index =>
            {
                var left = new ConcurrentProbeDictionary(Entries(("a", index)));
                var same = new ConcurrentProbeDictionary(Entries(("a", index)));
                var changed = new ConcurrentProbeDictionary(Entries(("a", index + 1)));

                results[index] =
                    Equal(left, same) && !Equal(left, changed) && !Equal(changed, left);
            }
        );

        foreach (var result in results)
        {
            result.ShouldBeTrue();
        }
    }

    private sealed class ComparerlessDictionary(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class WriteOnlyComparerDictionary(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        public object? Comparer
        {
            set { }
        }

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RenamedIndexerSet(IEnumerable<string> items) : IReadOnlySet<string>
    {
        private readonly HashSet<string> _inner = new(items, StringComparer.OrdinalIgnoreCase);

        [IndexerName("Comparer")]
        public int this[int index] => index;

        public int Count => _inner.Count;

        public bool Contains(string item) => _inner.Contains(item);

        public bool IsProperSubsetOf(IEnumerable<string> other) => _inner.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<string> other) =>
            _inner.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<string> other) => _inner.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<string> other) => _inner.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<string> other) => _inner.Overlaps(other);

        public bool SetEquals(IEnumerable<string> other) => _inner.SetEquals(other);

        public IEnumerator<string> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TextCountDictionary(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        int IReadOnlyCollection<KeyValuePair<string, int>>.Count => _inner.Count;

        public string Count => "many";

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ExplicitCountDictionary(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        int IReadOnlyCollection<KeyValuePair<string, int>>.Count => _inner.Count;

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private class AmbiguousComparerBase(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        public object Comparer => EqualityComparer<string>.Default;

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class AmbiguousComparerDictionary(Dictionary<string, int> inner)
        : AmbiguousComparerBase(inner)
    {
        public new string Comparer => "ambiguous";
    }

    private class AmbiguousCountBase(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class AmbiguousCountDictionary(Dictionary<string, int> inner)
        : AmbiguousCountBase(inner)
    {
        public new string Count => "many";
    }

    private sealed class PositiveComparerDictionary(
        Dictionary<string, int> inner,
        IEqualityComparer<string> comparer
    ) : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        public IEqualityComparer<string> Comparer { get; } = comparer;

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ThrowingComparerDictionary(
        Dictionary<string, int> inner,
        bool throwOnAccess
    ) : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;
        private readonly IEqualityComparer<string> _comparer = StringComparer.OrdinalIgnoreCase;

        public bool ThrowOnAccess = throwOnAccess;

        public int ComparerCalls;

        public IEqualityComparer<string> Comparer
        {
            get
            {
                ComparerCalls++;
                return ThrowOnAccess
                    ? throw new InvalidOperationException("Probe getter failure.")
                    : _comparer;
            }
        }

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class OpaqueEntry
    {
        public string Label => "opaque";
    }

    private sealed class KeylessEntryDictionary(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            foreach (var _ in _inner)
            {
                yield return new OpaqueEntry();
            }
        }
    }

    private sealed class ConcurrentProbeDictionary(Dictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        private readonly Dictionary<string, int> _inner = inner;

        public int this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<int> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out int value) => _inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
