using System.ComponentModel;
using SparseTypes.CompilerServices;

namespace SparseTypes;

/// <summary>Retains UI identities for collection rows across refreshes and key reconciliation.</summary>
/// <typeparam name="TElement">The collection element model type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseCollectionRowIdentityStore<TElement>
    where TElement : class
{
    private readonly Dictionary<TElement, List<Guid>> _identitiesByItem = new(SparseCoreRuntime.ReferenceComparer);
    private readonly Dictionary<TElement, int> _occurrencesInRefresh = new(SparseCoreRuntime.ReferenceComparer);
    private readonly Dictionary<SparsePathSegment, Guid> _identityByPath = [];
    private readonly HashSet<SparsePathSegment> _pathsInRefresh = [];
    private readonly Dictionary<Guid, Guid> _identityByTemporaryKey = [];
    private readonly HashSet<Guid> _temporaryKeysInRefresh = [];

    /// <summary>Starts a refresh generation before requesting row identities.</summary>
    public void BeginRefresh()
    {
        _occurrencesInRefresh.Clear();
        _pathsInRefresh.Clear();
        _temporaryKeysInRefresh.Clear();
    }

    /// <summary>Gets or creates the identity for one row in the active refresh generation.</summary>
    /// <param name="item">The current element model.</param>
    /// <param name="identity">The current positional, permanent-key, or temporary-key path segment.</param>
    /// <param name="temporaryKey">The row's temporary identity, when present.</param>
    /// <param name="isKeyed">Whether the collection declares stable keyed identity.</param>
    /// <returns>The stable UI identity.</returns>
    public Guid GetOrCreate(TElement item, SparsePathSegment identity, Guid? temporaryKey, bool isKeyed)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!isKeyed)
        {
            // An unkeyed list may contain one reference several times; each occurrence needs its own identity.
            _occurrencesInRefresh.TryGetValue(item, out var occurrence);
            _occurrencesInRefresh[item] = occurrence + 1;
            if (!_identitiesByItem.TryGetValue(item, out var identities))
            {
                identities = [];
                _identitiesByItem.Add(item, identities);
            }

            if (occurrence == identities.Count)
            {
                identities.Add(Guid.NewGuid());
            }

            return identities[occurrence];
        }

        _pathsInRefresh.Add(identity);
        if (temporaryKey is { } temporary)
        {
            _temporaryKeysInRefresh.Add(temporary);
        }

        var hasIdentity = _identityByPath.TryGetValue(identity, out var uiKey);
        if (!hasIdentity && temporaryKey is { } temporaryIdentity)
        {
            hasIdentity = _identityByTemporaryKey.TryGetValue(temporaryIdentity, out uiKey);
        }

        if (!hasIdentity)
        {
            uiKey = Guid.NewGuid();
        }

        _identityByPath[identity] = uiKey;
        if (temporaryKey is { } stableTemporaryKey)
        {
            _identityByTemporaryKey[stableTemporaryKey] = uiKey;
        }

        return uiKey;
    }

    /// <summary>Removes identities that were not observed in the current refresh generation.</summary>
    public void EndRefresh()
    {
        foreach (var item in _identitiesByItem.Keys.ToArray())
        {
            if (!_occurrencesInRefresh.TryGetValue(item, out var occurrences))
            {
                _identitiesByItem.Remove(item);
            }
            else
            {
                var identities = _identitiesByItem[item];
                identities.RemoveRange(occurrences, identities.Count - occurrences);
            }
        }

        foreach (var path in _identityByPath.Keys.Where(path => !_pathsInRefresh.Contains(path)).ToArray())
        {
            _identityByPath.Remove(path);
        }

        foreach (var temporaryKey in _identityByTemporaryKey.Keys.Where(key => !_temporaryKeysInRefresh.Contains(key)).ToArray())
        {
            _identityByTemporaryKey.Remove(temporaryKey);
        }
    }

    /// <summary>Forgets all retained row identities.</summary>
    public void Reset()
    {
        _identitiesByItem.Clear();
        _occurrencesInRefresh.Clear();
        _identityByPath.Clear();
        _pathsInRefresh.Clear();
        _identityByTemporaryKey.Clear();
        _temporaryKeysInRefresh.Clear();
    }
}
