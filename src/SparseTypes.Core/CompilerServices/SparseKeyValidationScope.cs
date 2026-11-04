using System.ComponentModel;
using System.Threading;

namespace SparseTypes.CompilerServices;

/// <summary>Owns an empty key set for one generated uniqueness check.</summary>
/// <typeparam name="TKey">The collection key type.</typeparam>
/// <remarks>The set must not be retained or accessed after disposal.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparseKeyValidationScope<TKey> : IDisposable
    where TKey : notnull
{
    private HashSet<TKey>? _keys;
    private readonly int _capacity;
    private readonly Func<int, HashSet<TKey>> _create;

    internal SparseKeyValidationScope(HashSet<TKey> keys, int capacity, Func<int, HashSet<TKey>> create)
    {
        _keys = keys;
        _capacity = capacity;
        _create = create;
    }

    /// <summary>Gets the set exclusively owned by this scope.</summary>
    /// <exception cref="ObjectDisposedException">The scope has been disposed.</exception>
    public HashSet<TKey> Keys => _keys ?? throw new ObjectDisposedException(nameof(SparseKeyValidationScope<TKey>));

    /// <summary>Returns bounded storage for reuse on the current thread.</summary>
    public void Dispose()
    {
        var keys = Interlocked.Exchange(ref _keys, null);
        if (keys is not null)
        {
            SparseKeyValidationPool<TKey>.Return(keys, _capacity, _create);
        }
    }
}

internal static class SparseKeyValidationPool<TKey>
    where TKey : notnull
{
    private const int MaximumRetainedEntries = 8192;

    // The set's comparer comes from the factory, so a cached set is only valid for an equal delegate
    // (same method and target). Mutating state captured by a factory is not tracked.
    [ThreadStatic]
    private static (HashSet<TKey>? Keys, int Capacity, Func<int, HashSet<TKey>>? Create) _cached;

    internal static SparseKeyValidationScope<TKey> Rent(int capacity, Func<int, HashSet<TKey>> create)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (create is null)
        {
            throw new ArgumentNullException(nameof(create));
        }

        var keys = _cached.Keys;
        if (keys is null || _cached.Capacity < capacity || !create.Equals(_cached.Create))
        {
            keys = create(capacity);
        }
        else
        {
            capacity = _cached.Capacity;
        }

        _cached = default;
        return new SparseKeyValidationScope<TKey>(keys, capacity, create);
    }

    internal static void Return(HashSet<TKey> keys, int capacity, Func<int, HashSet<TKey>> create)
    {
        capacity = Math.Max(capacity, keys.Count);
        if (capacity > MaximumRetainedEntries)
        {
            return;
        }

        keys.Clear();
        if (_cached.Keys is null || capacity > _cached.Capacity || !create.Equals(_cached.Create))
        {
            _cached = (keys, capacity, create);
        }
    }
}
