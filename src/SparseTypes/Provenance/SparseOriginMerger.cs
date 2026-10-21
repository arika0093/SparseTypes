namespace SparseTypes;

#pragma warning disable CS8714 // Preserve nullable element provenance behavior across TFM annotations.

/// <summary>
/// Computes per-element origin attributions for built-in collection merges.
/// </summary>
/// <remarks>Live-merge counterpart to the post-hoc <see cref="SparseCollectionProvenance"/> explanation.</remarks>
internal static class SparseOriginMerger
{
    /// <summary>
    /// Concatenates lower then higher attributions for an append merge.
    /// </summary>
    public static string?[] MergeAppendOrigins<T>(
        IEnumerable<T> lower,
        string?[]? lowerOrigins,
        string? lowerFallback,
        IEnumerable<T> higher,
        string?[]? higherOrigins,
        string? higherFallback
    )
    {
        // Origins depend on element positions only; known counts fill the
        // result without enumerating or retaining element payloads.
        var lowerCount = GetCount(lower);
        var higherCount = GetCount(higher);
        var result = new string?[checked(lowerCount + higherCount)];
        FillOriginRange(result, 0, lowerCount, lowerOrigins, lowerFallback);
        FillOriginRange(result, lowerCount, higherCount, higherOrigins, higherFallback);
        return result;
    }

    /// <summary>
    /// Tracks first-accepted attributions for a sequence set-union merge.
    /// </summary>
    public static string?[] MergeSetUnionOrigins<T>(
        IEnumerable<T> lower,
        string?[]? lowerOrigins,
        string? lowerFallback,
        IEnumerable<T> higher,
        string?[]? higherOrigins,
        string? higherFallback,
        IEqualityComparer<T>? comparer
    )
    {
        comparer ??= EqualityComparer<T>.Default;
        var seen = new HashSet<T>(comparer);
        var result = new List<string?>();
        AddDistinct(lower, lowerOrigins, lowerFallback, seen, result);
        AddDistinct(higher, higherOrigins, higherFallback, seen, result);
        return result.ToArray();
    }

    /// <summary>Tracks first-accepted attributions for a set-union merge in effective enumeration order.</summary>
    /// <remarks>The effective comparer wins, otherwise the first active contribution comparer wins.</remarks>
    public static string?[] MergeSetOrigins<T>(
        IEnumerable<T> lower,
        string?[]? lowerOrigins,
        string? lowerFallback,
        IEnumerable<T> higher,
        string?[]? higherOrigins,
        string? higherFallback,
        IEnumerable<T> effective
    )
    {
        ArgumentNullException.ThrowIfNull(effective);
        var comparer = DiscoverSetComparer(lower, higher, effective);
        if (comparer is null)
        {
            // Count-only probe: no element payload is copied for the output.
            return new string?[GetCount(effective)];
        }

        // First-origin tracking while building the union.
        var firstByValue = new Dictionary<T, string?>(comparer);
        AddFirst(lower, lowerOrigins, lowerFallback, firstByValue);
        AddFirst(higher, higherOrigins, higherFallback, firstByValue);

        if (TryGetCount(effective, out var count))
        {
            var result = new string?[count];
            var position = 0;
            foreach (var value in effective)
            {
                result[position++] = firstByValue.TryGetValue(value, out var origin) ? origin : null;
            }

            return result;
        }

        // Count-unknown effective sources enumerate once; only origin strings
        // are buffered, never the element payloads.
        var buffer = new List<string?>();
        foreach (var value in effective)
        {
            buffer.Add(firstByValue.TryGetValue(value, out var origin) ? origin : null);
        }

        return buffer.ToArray();
    }

    private static IEqualityComparer<T>? DiscoverSetComparer<T>(IEnumerable<T> lower, IEnumerable<T> higher, IEnumerable<T> effective)
    {
        var effectiveComparer = SparseCollectionProvenance.TryGetSetComparer(effective);
        var activeComparer = SparseCollectionProvenance.TryGetSetComparer(lower) ?? SparseCollectionProvenance.TryGetSetComparer(higher);
        if (effectiveComparer is not null && activeComparer is not null && !effectiveComparer.Equals(activeComparer))
        {
            return null;
        }

        return effectiveComparer ?? activeComparer ?? EqualityComparer<T>.Default;
    }

    /// <summary>Obtains the element count without enumerating supported collection shapes.</summary>
    private static int GetCount<T>(IEnumerable<T> values) => TryGetCount(values, out var count) ? count : CountEnumerable(values);

    private static bool TryGetCount<T>(IEnumerable<T> values, out int count)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values is ICollection<T> genericCollection)
        {
            count = genericCollection.Count;
            return true;
        }

        if (values is IReadOnlyCollection<T> readOnlyCollection)
        {
            count = readOnlyCollection.Count;
            return true;
        }

        // Synchronized wrappers may be mutated concurrently; skip their stale
        // Count and fall through to a single forward pass.
        if (values is System.Collections.ICollection nonGenericCollection && !nonGenericCollection.IsSynchronized)
        {
            count = nonGenericCollection.Count;
            return true;
        }

        count = 0;
        return false;
    }

    private static int CountEnumerable<T>(IEnumerable<T> values)
    {
        var count = 0;
        using var enumerator = values.GetEnumerator();
        checked
        {
            while (enumerator.MoveNext())
            {
                count++;
            }
        }

        return count;
    }

    private static void FillOriginRange(string?[] result, int offset, int count, string?[]? origins, string? fallback)
    {
        for (var index = 0; index < count; index++)
        {
            result[offset + index] = OriginAt(origins, index, fallback);
        }
    }

    private static string? OriginAt(string?[]? origins, int index, string? fallback) =>
        origins is null || index >= origins.Length ? fallback : origins[index] ?? fallback;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loops track element positions while building the distinct union; LINQ would obscure provenance positions."
    )]
    private static void AddDistinct<T>(IEnumerable<T> values, string?[]? origins, string? fallback, HashSet<T> seen, List<string?> result)
    {
        ArgumentNullException.ThrowIfNull(values);
        // Forward enumeration keeps per-element attribution aligned with the
        // source positions; duplicates advance the position without emitting.
        var position = 0;
        foreach (var value in values)
        {
            var index = position++;
            if (seen.Add(value))
            {
                result.Add(OriginAt(origins, index, fallback));
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loops track element positions while building the comparer-aware union; LINQ would obscure provenance positions."
    )]
    private static void AddFirst<T>(IEnumerable<T> values, string?[]? origins, string? fallback, Dictionary<T, string?> firstByValue)
    {
        ArgumentNullException.ThrowIfNull(values);
        var position = 0;
        foreach (var value in values)
        {
            var index = position++;
            if (!firstByValue.ContainsKey(value))
            {
                firstByValue[value] = OriginAt(origins, index, fallback);
            }
        }
    }
}
