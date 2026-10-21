using System.ComponentModel;
using System.Linq;

namespace SparseTypes.CompilerServices;

/// <summary>Shared generated-code kernels for comparison, merge, cloning, and key validation.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SparseCoreRuntime
{
    /// <summary>Gets the reference-identity comparer used by generated helpers.</summary>
    public static IEqualityComparer<object> ReferenceComparer =>
        SparseReferenceEqualityComparer.Instance;

    /// <summary>Compares two values using sparse value semantics.</summary>
    public static bool AreEqual(object? left, object? right) =>
        SparseValueComparer.AreEqual(left, right);

    /// <summary>Compares two typed values using sparse value semantics.</summary>
    public static bool AreEqual<T>(T? left, T? right) => SparseValueComparer.AreEqual(left, right);

    /// <summary>Compares sequences in order.</summary>
    public static bool AreSequenceEqual<T>(IEnumerable<T>? left, IEnumerable<T>? right) =>
        SparseValueComparer.AreSequenceEqual(left, right);

    /// <summary>Compares sequences in order with a caller-provided item comparer.</summary>
    public static bool AreSequenceEqual<T>(
        IEnumerable<T>? left,
        IEnumerable<T>? right,
        Func<T, T, bool> itemComparer
    ) => SparseValueComparer.AreSequenceEqual(left, right, itemComparer);

    /// <summary>Compares arrays in order.</summary>
    public static bool AreSequenceEqual<T>(T[]? left, T[]? right) =>
        SparseConcreteComparisons.AreSequenceEqual(left, right);

    /// <summary>Compares lists in order.</summary>
    public static bool AreSequenceEqual<T>(List<T>? left, List<T>? right) =>
        SparseConcreteComparisons.AreSequenceEqual(left, right);

    /// <summary>Compares arrays in order with a caller-provided item comparer.</summary>
    public static bool AreSequenceEqual<T>(T[]? left, T[]? right, Func<T, T, bool> itemComparer) =>
        SparseConcreteComparisons.AreSequenceEqual(left, right, itemComparer);

    /// <summary>Compares lists in order with a caller-provided item comparer.</summary>
    public static bool AreSequenceEqual<T>(
        List<T>? left,
        List<T>? right,
        Func<T, T, bool> itemComparer
    ) => SparseConcreteComparisons.AreSequenceEqual(left, right, itemComparer);

    /// <summary>Compares sets without depending on enumeration order.</summary>
    public static bool AreSetEqual<T>(IEnumerable<T>? left, IEnumerable<T>? right) =>
        SparseValueComparer.AreSetEqual(left, right);

    /// <summary>Compares hash sets without depending on enumeration order.</summary>
    public static bool AreSetEqual<T>(HashSet<T>? left, HashSet<T>? right) =>
        SparseConcreteComparisons.AreSetEqual(left, right);

    /// <summary>Compares sorted sets without depending on enumeration order.</summary>
    public static bool AreSetEqual<T>(SortedSet<T>? left, SortedSet<T>? right) =>
        SparseConcreteComparisons.AreSetEqual(left, right);

    /// <summary>Compares dictionaries by key and value.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>>? left,
        IEnumerable<KeyValuePair<TKey, TValue>>? right
    ) => SparseValueComparer.AreDictionaryEqual(left, right);

    /// <summary>Compares dictionaries by key and caller-provided value equality.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>>? left,
        IEnumerable<KeyValuePair<TKey, TValue>>? right,
        Func<TValue, TValue, bool> valueComparer
    ) => SparseValueComparer.AreDictionaryEqual(left, right, valueComparer);

    /// <summary>Compares dictionaries by key and value.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        Dictionary<TKey, TValue>? left,
        Dictionary<TKey, TValue>? right
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right);

    /// <summary>Compares sorted dictionaries by key and value.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedDictionary<TKey, TValue>? left,
        SortedDictionary<TKey, TValue>? right
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right);

    /// <summary>Compares sorted lists by key and value.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedList<TKey, TValue>? left,
        SortedList<TKey, TValue>? right
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right);

    /// <summary>Appends ordered contributions, preserving duplicates.</summary>
    public static List<T> MergeAppendList<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeAppendList(lower, higher);

    /// <summary>Merges ordered contributions, retaining the first occurrence of each value.</summary>
    public static T[] MergeDistinctArray<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeDistinctArray(lower, higher);

    /// <summary>Merges ordered contributions, retaining the first occurrence of each value.</summary>
    public static List<T> MergeDistinctList<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeDistinctList(lower, higher);

    /// <summary>Merges set-shaped contributions while preserving a concrete hash-set comparer.</summary>
    public static HashSet<T> MergeSet<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeSet(lower, higher);

    /// <summary>Creates a reference-identity dictionary for clone operations.</summary>
    public static Dictionary<object, object> CreateCloneContext() =>
        new(SparseReferenceEqualityComparer.Instance);

    /// <summary>Rents a reference-identity context for one generated clone operation.</summary>
    public static SparseCloneContextScope RentCloneContext() => SparseCloneContextPool.Rent();

    /// <summary>Rents a set for validating one generated keyed collection operation.</summary>
    public static SparseKeyValidationScope<TKey> RentKeyValidation<TKey>(
        int capacity,
        Func<int, HashSet<TKey>> create
    )
        where TKey : notnull => SparseKeyValidationPool<TKey>.Rent(capacity, create);

    /// <summary>Creates a reference-identity set for model conversion cycle detection.</summary>
    public static HashSet<object> CreateFromCycleContext() =>
        new(SparseReferenceEqualityComparer.Instance);

    /// <summary>Creates a pair-identity set for model diff cycle detection.</summary>
    public static HashSet<KeyValuePair<object, object>> CreateDiffCycleContext() =>
        new(SparseDiffPairEqualityComparer.Instance);

    /// <summary>Ensures that a sequence contains no duplicate keys.</summary>
    public static void EnsureUniqueKeys<TKey>(
        IEnumerable<TKey> keys,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        var seen = new HashSet<TKey>(comparer);
        if (keys.Any(key => !seen.Add(key)))
        {
            throw new ArgumentException("The collection contains duplicate keys.", nameof(keys));
        }
    }

    /// <summary>Compares key sequences in order.</summary>
    public static bool KeyOrderEquals<TKey>(
        IReadOnlyList<TKey> left,
        IReadOnlyList<TKey> right,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
        {
            return false;
        }

        comparer ??= EqualityComparer<TKey>.Default;
        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
