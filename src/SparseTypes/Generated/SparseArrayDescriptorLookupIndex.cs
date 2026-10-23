using System;
using System.Collections.Generic;

namespace SparseTypes.Generated;

internal sealed class SparseArrayDescriptorLookupIndex
{
    private readonly SparseArrayDescriptorAccess _access;
    private readonly object _gate = new();
    private Dictionary<object, int>? _keyPositions;
    private Dictionary<Guid, int>? _temporaryKeyPositions;
    private int _keyCount = -1;
    private int _temporaryKeyCount = -1;

    public SparseArrayDescriptorLookupIndex(SparseArrayDescriptorAccess access) => _access = access;

    public int IndexOfKey(object? key)
    {
        var resolver = _access.IndexOfKey;
        if (
            resolver is null
            || key is null
            || _access.Count is null
            || _access.GetItemKey is null
            || _access.KeyEquals is null
            || _access.KeyHash is null
        )
        {
            return resolver?.Invoke(key) ?? -1;
        }

        lock (_gate)
        {
            EnsureKeyPositions();
            if (_keyPositions!.TryGetValue(key, out var position) && KeyMatchesAt(position, key) && !HasKeyBefore(position, key))
            {
                return position;
            }

            var result = resolver(key);
            RebuildKeyPositions();
            return result;
        }
    }

    public int IndexOfTemporaryKey(Guid temporaryKey)
    {
        var resolver = _access.IndexOfTemporaryKey;
        if (resolver is null || _access.Count is null || _access.GetItemTemporaryKey is null)
        {
            return resolver?.Invoke(temporaryKey) ?? -1;
        }

        lock (_gate)
        {
            EnsureTemporaryKeyPositions();
            if (
                _temporaryKeyPositions!.TryGetValue(temporaryKey, out var position)
                && TemporaryKeyMatchesAt(position, temporaryKey)
                && !HasTemporaryKeyBefore(position, temporaryKey)
            )
            {
                return position;
            }

            var result = resolver(temporaryKey);
            RebuildTemporaryKeyPositions();
            return result;
        }
    }

    private void EnsureKeyPositions()
    {
        if (_keyPositions is not null && _keyCount == _access.Count!())
        {
            return;
        }

        RebuildKeyPositions();
    }

    private void RebuildKeyPositions()
    {
        var count = _access.Count!();
        var positions = new Dictionary<object, int>(count, new KeyComparer(_access.KeyEquals!, _access.KeyHash!));
        var getItemKey = _access.GetItemKey!;
        for (var index = 0; index < count; index++)
        {
            if (getItemKey(index) is { } key)
            {
                positions.TryAdd(key, index);
            }
        }

        _keyPositions = positions;
        _keyCount = count;
    }

    private void EnsureTemporaryKeyPositions()
    {
        if (_temporaryKeyPositions is not null && _temporaryKeyCount == _access.Count!())
        {
            return;
        }

        RebuildTemporaryKeyPositions();
    }

    private void RebuildTemporaryKeyPositions()
    {
        var count = _access.Count!();
        var positions = new Dictionary<Guid, int>(count);
        var getTemporaryKey = _access.GetItemTemporaryKey!;
        for (var index = 0; index < count; index++)
        {
            if (getTemporaryKey(index) is { } key)
            {
                positions.TryAdd(key, index);
            }
        }

        _temporaryKeyPositions = positions;
        _temporaryKeyCount = count;
    }

    private bool KeyMatchesAt(int index, object key)
    {
        if ((uint)index >= (uint)_access.Count!())
        {
            return false;
        }

        return _access.KeyEquals!(_access.GetItemKey!(index), key);
    }

    private bool HasKeyBefore(int index, object key)
    {
        var getItemKey = _access.GetItemKey!;
        var keyEquals = _access.KeyEquals!;
        for (var preceding = 0; preceding < index; preceding++)
        {
            if (keyEquals(getItemKey(preceding), key))
            {
                return true;
            }
        }

        return false;
    }

    private bool TemporaryKeyMatchesAt(int index, Guid temporaryKey) =>
        (uint)index < (uint)_access.Count!() && _access.GetItemTemporaryKey!(index) == temporaryKey;

    private bool HasTemporaryKeyBefore(int index, Guid temporaryKey)
    {
        var getTemporaryKey = _access.GetItemTemporaryKey!;
        for (var preceding = 0; preceding < index; preceding++)
        {
            if (getTemporaryKey(preceding) == temporaryKey)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class KeyComparer : IEqualityComparer<object>
    {
        private readonly Func<object?, object?, bool> _equals;
        private readonly Func<object?, int> _hash;

        public KeyComparer(Func<object?, object?, bool> equals, Func<object?, int> hash)
        {
            _equals = equals;
            _hash = hash;
        }

        bool IEqualityComparer<object>.Equals(object? left, object? right) => _equals(left, right);

        int IEqualityComparer<object>.GetHashCode(object value) => _hash(value);
    }
}
