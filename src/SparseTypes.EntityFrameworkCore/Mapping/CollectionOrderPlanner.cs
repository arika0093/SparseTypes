namespace SparseTypes.EntityFrameworkCore.Mapping;

internal static class CollectionOrderPlanner
{
    internal static List<TEntityChild> PlanOrder<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        KeyedCollectionView<TDtoChild> view,
        Dictionary<object, TEntityChild> tracked,
        SyncPlan<TDtoChild, TEntityChild> plan,
        IReadOnlyList<TEntityChild> trackedOrder,
        string path
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var removed = new HashSet<object>(plan.Removed.Select(static removal => removal.Key), runtime.KeyComparer);
        var survivors = new Dictionary<object, TEntityChild>(Math.Max(0, tracked.Count - plan.Removed.Count), runtime.KeyComparer);
        foreach (var (key, child) in tracked)
        {
            if (!removed.Contains(key))
            {
                survivors[key] = child;
            }
        }

        var assignedAdds = new Dictionary<object, TEntityChild>(plan.Added.Count, runtime.KeyComparer);
        var temporarySeen = new HashSet<Guid>();
        foreach (var addition in plan.Added.OrderBy(static a => a.SequenceHint))
        {
            if (addition.TemporaryKey is not null && addition.TemporaryKey != Guid.Empty && !temporarySeen.Add(addition.TemporaryKey.Value))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"The collection '{runtime.DtoName}' adds a duplicated temporary identity."
                );
            }

            if (addition.AssignedKey is not null && !assignedAdds.TryAdd(addition.AssignedKey, addition.Entity))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"The collection '{runtime.DtoName}' adds a duplicated child key."
                );
            }
        }

        // Assigned keys follow AfterOrder; unassigned additions fill sentinel
        // slots in AfterIndex order.
        var assignedOrder = new List<TEntityChild>(view.AfterOrder.Count);
        var sentinelSlots = 0;
        foreach (var key in view.AfterOrder)
        {
            if (runtime.IsUnassigned(key))
            {
                sentinelSlots++;
                continue;
            }

            if (survivors.TryGetValue(key, out var child))
            {
                assignedOrder.Add(child);
                continue;
            }

            if (assignedAdds.TryGetValue(key, out var created))
            {
                assignedOrder.Add(created);
                continue;
            }

            // Assigned AfterOrder keys outside membership validation only occur
            // when order persistence was skipped; fail rather than guessing.
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyNotFound,
                path,
                $"The keyed transition order for '{runtime.DtoName}' references a child with no tracked identity in this parent collection."
            );
        }

        // All unassigned additions (temporary-keyed or plain) share the
        // declared sentinel in AfterOrder.
        var orderedUnassigned = plan
            .Added.Where(addition => addition.AssignedKey is null)
            .OrderBy(static addition => addition.SequenceHint)
            .Select(static addition => addition.Entity)
            .ToList();
        if (orderedUnassigned.Count != sentinelSlots)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The keyed transition order for '{runtime.DtoName}' holds {sentinelSlots} unassigned slots but {orderedUnassigned.Count} unassigned additions. Refusing a partial reorder."
            );
        }

        // Rebuild positionally: walk AfterOrder once, taking assigned entries
        // and unassigned additions in slot order.
        var merged = new List<TEntityChild>(view.AfterOrder.Count);
        var assignedIndex = 0;
        var unassignedIndex = 0;
        foreach (var key in view.AfterOrder)
        {
            if (runtime.IsUnassigned(key))
            {
                merged.Add(orderedUnassigned[unassignedIndex++]);
                continue;
            }

            merged.Add(assignedOrder[assignedIndex++]);
        }

        if (runtime.RemoveKind == SparseEntityRemoveKind.SoftDelete)
        {
            // Membership is by tracked instance; value equality must not
            // collapse distinct EF entities with different persistent keys.
            var retained = new HashSet<TEntityChild>(merged, ReferenceEqualityComparer.Instance);
            foreach (var removal in plan.Removed.Where(removal => retained.Add(removal.Entity)))
            {
                merged.Add(removal.Entity);
            }

            // Use the original navigation sequence explicitly, rather than
            // relying on dictionary enumeration to preserve tracked order.
            foreach (
                var survivor in trackedOrder.Where(survivor =>
                    !retained.Contains(survivor) && runtime.IsDeletedPredicate?.Invoke(survivor) == true && retained.Add(survivor)
                )
            )
            {
                merged.Add(survivor);
            }
        }

        return merged;
    }
}
