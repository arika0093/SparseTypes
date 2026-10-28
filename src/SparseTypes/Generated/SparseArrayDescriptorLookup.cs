namespace SparseTypes.Generated;

internal sealed class SparseArrayDescriptorLookup
{
    private readonly SparseArrayDescriptorAccess _access;

    public SparseArrayDescriptorLookup(SparseArrayDescriptorAccess access) => _access = access;

    public int IndexOfKey(object? key)
    {
        if (key is null || _access.IsUnassignedKey?.Invoke(key) == true)
        {
            return -1;
        }

        if (_access.IndexOfKey is not null)
        {
            return _access.IndexOfKey(key);
        }

        if (_access.Count is null || _access.GetItemKey is null || _access.KeyEquals is null)
        {
            return -1;
        }

        // Raw model edits can change keys without notifications or a count change.
        // A live scan also rejects ambiguity without rebuilding a dictionary on misses.
        var found = -1;
        var count = _access.Count();
        for (var index = 0; index < count; index++)
        {
            var candidate = _access.GetItemKey(index);
            if (candidate is null || !_access.KeyEquals(candidate, key))
            {
                continue;
            }

            if (found >= 0)
            {
                return -1;
            }

            found = index;
        }

        return found;
    }

    public int IndexOfTemporaryKey(Guid temporaryKey)
    {
        if (temporaryKey == Guid.Empty)
        {
            return -1;
        }

        if (_access.IndexOfTemporaryKey is not null)
        {
            return _access.IndexOfTemporaryKey(temporaryKey);
        }

        if (_access.Count is null || _access.GetItemTemporaryKey is null)
        {
            return -1;
        }

        var found = -1;
        var count = _access.Count();
        for (var index = 0; index < count; index++)
        {
            if (_access.GetItemTemporaryKey(index) != temporaryKey)
            {
                continue;
            }

            if (found >= 0)
            {
                return -1;
            }

            found = index;
        }

        return found;
    }
}
