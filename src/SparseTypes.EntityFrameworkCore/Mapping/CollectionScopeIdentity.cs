using System.Collections.Generic;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>Kinds of parent scope that disambiguate nested collection correlations.</summary>
/// <remarks>Index-addressed parents stay <see cref="Unscoped" />.</remarks>
internal enum CollectionScopeKind
{
    /// <summary>No typed parent scope; legacy path-string correlation applies.</summary>
    Unscoped,

    /// <summary>The parent element carries a stable assigned key.</summary>
    Assigned,

    /// <summary>The parent element carries a temporary identity.</summary>
    Temporary,
}

/// <summary>Identifies one parent collection element for nested correlation.</summary>
/// <remarks>A value of this type is a lookup token, never a dictionary key.</remarks>
internal readonly record struct CollectionScopeIdentity(CollectionScopeKind Kind, object? Key, Guid Temp, IEqualityComparer<object>? KeyComparer)
{
    /// <summary>Gets the identity for a parent without a stable scope.</summary>
    public static CollectionScopeIdentity Unscoped => default;

    /// <summary>Creates the identity for a parent with a stable assigned key.</summary>
    /// <param name="key">The parent DTO key value.</param>
    /// <param name="comparer">The parent collection's key comparer.</param>
    public static CollectionScopeIdentity Assigned(object key, IEqualityComparer<object>? comparer) =>
        new(CollectionScopeKind.Assigned, key, Guid.Empty, comparer);

    /// <summary>Creates the identity for a parent with a temporary identity.</summary>
    /// <param name="temporary">The parent temporary identity.</param>
    public static CollectionScopeIdentity Temporary(Guid temporary) => new(CollectionScopeKind.Temporary, null, temporary, null);

    /// <summary>Derives the scope for a collection element from its identities.</summary>
    /// <param name="key">The element DTO key, when known.</param>
    /// <param name="assigned">Whether the key is an assigned (non-sentinel) identity.</param>
    /// <param name="temporary">The element temporary identity, when present.</param>
    /// <param name="comparer">The owning collection's key comparer.</param>
    public static CollectionScopeIdentity ForElement(object? key, bool assigned, Guid? temporary, IEqualityComparer<object>? comparer)
    {
        if (key is not null && assigned)
        {
            return Assigned(key, comparer);
        }

        if (temporary is not null && temporary != Guid.Empty)
        {
            return Temporary(temporary.Value);
        }

        return Unscoped;
    }
}

/// <summary>Nested temporary-identity records for one mapped collection.</summary>
/// <remarks>Temporary identities are recorded per typed ancestor path in addition to legacy path-string buckets.</remarks>
internal sealed class NestedScopeTable
{
    /// <summary>Records of one parent scope: temporary identities plus member entities.</summary>
    /// <remarks>
    /// Members include temp-less created entities so scope-membership probes
    /// stay precise even when a row carries no temporary identity.
    /// </remarks>
    private sealed class ScopeSlot
    {
        public readonly Dictionary<Guid, object> Temps = new();
        public readonly HashSet<object> Members = new(ReferenceEqualityComparer.Instance);
    }

    private readonly List<(CollectionScopePath Path, ScopeSlot Slot)> _scopes = [];

    // Reference-identity set of every member recorded in this table. Foreign
    // membership probes consult this instead of materializing the large
    // complement of one scope's members.
    private readonly HashSet<object> _allMembers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Records a created child under its parent scope.</summary>
    /// <param name="path">The nested collection path, for diagnostics.</param>
    /// <param name="scope">The typed parent ancestry.</param>
    /// <param name="temporaryKey">The created child's temporary identity.</param>
    /// <param name="entity">The created child entity.</param>
    /// <exception cref="SparseEntityMappingException">
    /// The same parent scope already records another entity under the identity.
    /// </exception>
    public void Record(string path, CollectionScopePath scope, Guid temporaryKey, object entity)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entity);
        var slot = ResolveSlot(scope, create: true)!;
        if (slot.Temps.TryGetValue(temporaryKey, out var existing))
        {
            if (!ReferenceEquals(existing, entity))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"Two created children under '{path}' share one temporary identity. Temporary identities must be unique within one collection scope."
                );
            }

            return;
        }

        slot.Temps[temporaryKey] = entity;
        slot.Members.Add(entity);
        _allMembers.Add(entity);
    }

    /// <summary>Records a created entity as a scope member without a temporary identity.</summary>
    /// <param name="scope">The typed parent ancestry.</param>
    /// <param name="entity">The created child entity.</param>
    public void RecordEntity(CollectionScopePath scope, object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ResolveSlot(scope, create: true)!.Members.Add(entity);
        _allMembers.Add(entity);
    }

    /// <summary>Looks up created children for a parent scope.</summary>
    /// <param name="scopes">Candidate typed parent ancestries.</param>
    /// <returns>The temporary-to-entity map, or null when nothing was recorded.</returns>
    public IReadOnlyDictionary<Guid, object>? TryGet(IReadOnlyList<CollectionScopePath> scopes)
    {
        foreach (var scope in scopes)
        {
            var slot = ResolveSlot(scope, create: false);
            if (slot is not null && slot.Temps.Count > 0)
            {
                return slot.Temps;
            }
        }

        return null;
    }

    /// <summary>Builds a membership filter for members recorded under other scopes.</summary>
    /// <param name="mine">Candidate typed ancestries of the current item.</param>
    /// <returns>A filter answering sibling-scope membership without materializing the complement.</returns>
    /// <remarks>
    /// Members with no scoped record anywhere stay unattributed and are never
    /// reported as foreign, so the legacy correlation path still sees them.
    /// A member recorded under both a candidate scope and another scope is
    /// known to this item, not foreign.
    /// </remarks>
    public ForeignMemberFilter ForeignMembers(IReadOnlyList<CollectionScopePath> mine)
    {
        HashSet<object>? known = null;
        foreach (var scope in mine)
        {
            var slot = ResolveSlot(scope, create: false);
            if (slot is not null && slot.Members.Count > 0)
            {
                // Only candidate-scope members here: the current-scope union
                // is small by construction, unlike the full complement the
                // previous materialization built per call.
                (known ??= new HashSet<object>(ReferenceEqualityComparer.Instance)).UnionWith(slot.Members);
            }
        }

        return new ForeignMemberFilter(_allMembers, known);
    }

    /// <summary>Reference-identity membership filter for sibling-scope entities.</summary>
    /// <remarks>
    /// A recorded entity is foreign exactly when the table attributes it
    /// (under any scope) and no candidate scope lists it as a member. The
    /// default filter reports nothing as foreign, which keeps unattributed
    /// entities on the legacy correlation path. The filter is retained for
    /// the duration of one reconciliation call only.
    /// </remarks>
    public readonly struct ForeignMemberFilter
    {
        private readonly HashSet<object> _allMembers;
        private readonly HashSet<object>? _known;

        internal ForeignMemberFilter(HashSet<object> allMembers, HashSet<object>? known)
        {
            _allMembers = allMembers;
            _known = known;
        }

        /// <summary>Checks whether a member belongs to a scope outside the candidates.</summary>
        /// <param name="member">The recorded candidate entity.</param>
        /// <returns>Whether the table attributes the member but no candidate scope does.</returns>
        public bool Contains(object member)
        {
            // Adds are frozen during reconciliation, so the live set read is
            // stable across repeated probes of the same filter instance.
            var all = _allMembers;
            if (all is null || all.Count == 0)
            {
                return false;
            }

            var known = _known;
            if (known is not null && known.Contains(member))
            {
                return false;
            }

            return all.Contains(member);
        }
    }

    private ScopeSlot? ResolveSlot(CollectionScopePath scope, bool create)
    {
        foreach (var (candidate, slot) in _scopes)
        {
            if (candidate.Matches(scope))
            {
                return slot;
            }
        }

        if (!create)
        {
            return null;
        }

        var fresh = new ScopeSlot();
        _scopes.Add((scope, fresh));
        return fresh;
    }
}
