namespace SparseTypes.Blazor;

internal static class SparseNestedCollectionStateQuery
{
    internal static bool TryGetState<TModel>(
        ISparseChangeSet changes,
        Generated.IDescriptorSet descriptors,
        SparsePath path,
        out SparseCollectionState? state
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(descriptors);
        state = null;
        if (path.RootType != typeof(TModel) || path.IsRoot || path.Segments.Count == 0)
        {
            return false;
        }

        var currentChanges = changes;
        var currentDescriptors = descriptors;
        var index = 0;
        while (index < path.Segments.Count)
        {
            var segment = path.Segments[index];
            if (segment.Kind != SparsePathSegmentKind.Member || !currentDescriptors.TryGet(segment.Name, out var member))
            {
                return false;
            }

            if (index == path.Segments.Count - 1)
            {
                return TryGetMemberCollectionState(currentChanges, member, path, out state);
            }

            var next = path.Segments[index + 1];
            if (next.Kind == SparsePathSegmentKind.Member)
            {
                if (!currentChanges.TryGetNestedChange(segment.Name, out var nestedChanges) || member.Child is not { } childDescriptors)
                {
                    return false;
                }

                currentChanges = nestedChanges;
                currentDescriptors = childDescriptors;
                index++;
                continue;
            }

            if (member.Array is { IsKeyed: true } array)
            {
                if (
                    !TryGetKeyedChange(currentChanges, segment.Name, array, out var keyedChange, out var keyedAccess)
                    || !TryGetKeyedItemChange(keyedChange!, array, keyedAccess!, next, out var itemChanges, out var itemDescriptors)
                )
                {
                    return false;
                }

                currentChanges = itemChanges!;
                currentDescriptors = itemDescriptors!;
                index += 2;
                continue;
            }

            if (member.Dictionary is { } dictionary && next.Kind == SparsePathSegmentKind.Key)
            {
                if (
                    !TryGetDictionaryChange(currentChanges, segment.Name, dictionary, out var dictionaryChange, out var dictionaryAccess)
                    || !TryGetDictionaryItemChange(
                        dictionaryChange!,
                        dictionary,
                        dictionaryAccess!,
                        next,
                        out var itemChanges,
                        out var itemDescriptors
                    )
                )
                {
                    return false;
                }

                currentChanges = itemChanges!;
                currentDescriptors = itemDescriptors!;
                index += 2;
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool TryGetMemberCollectionState(
        ISparseChangeSet changes,
        Generated.IDescriptor member,
        SparsePath path,
        out SparseCollectionState? state
    )
    {
        if (member.Array is { IsKeyed: true } array)
        {
            if (!TryGetKeyedChange(changes, member.Name, array, out var keyedChange, out var keyedAccess))
            {
                state = null;
                return false;
            }

            state = keyedAccess!.GetState(keyedChange!, path);
            return true;
        }

        if (member.Dictionary is { } dictionary)
        {
            if (!TryGetDictionaryChange(changes, member.Name, dictionary, out var dictionaryChange, out var dictionaryAccess))
            {
                state = null;
                return false;
            }

            state = dictionaryAccess!.GetState(dictionaryChange!, path);
            return true;
        }

        state = null;
        return false;
    }

    private static bool TryGetKeyedChange(
        ISparseChangeSet changes,
        string memberName,
        Generated.IArrayDescriptor array,
        out object? change,
        out SparseCollectionChangeAccess? access
    )
    {
        access = array.KeyType is { } keyType ? SparseCollectionChangeAccess.ForKeyed(keyType, array.ItemType) : null;
        change = access?.GetChange(changes, memberName);
        return change is not null;
    }

    private static bool TryGetKeyedItemChange(
        object change,
        Generated.IArrayDescriptor array,
        SparseCollectionChangeAccess access,
        SparsePathSegment segment,
        out ISparseChangeSet? itemChanges,
        out Generated.IDescriptorSet? itemDescriptors
    )
    {
        itemChanges = null;
        itemDescriptors = null;
        if (segment.Kind is not (SparsePathSegmentKind.Key or SparsePathSegmentKind.TemporaryKey))
        {
            return false;
        }

        itemChanges = access.GetItemChange(change, segment);
        var itemIndex =
            segment.Kind == SparsePathSegmentKind.TemporaryKey
                ? array.IndexOfTemporaryKey(segment.TemporaryKey!.Value)
                : array.IndexOfKey(segment.Key);
        itemDescriptors = itemIndex < 0 ? null : array.GetItemDescriptors(itemIndex);
        return itemChanges is not null && itemDescriptors is not null;
    }

    private static bool TryGetDictionaryChange(
        ISparseChangeSet changes,
        string memberName,
        Generated.IDictDescriptor dictionary,
        out object? change,
        out SparseCollectionChangeAccess? access
    )
    {
        // HasChild describes the member itself, not a dictionary's value shape.
        access = SparseCollectionChangeAccess.ForDictionary(dictionary.KeyType, dictionary.ValueType, hasChild: true);
        change = access?.GetChange(changes, memberName);
        if (change is not null)
        {
            return true;
        }

        access = SparseCollectionChangeAccess.ForDictionary(dictionary.KeyType, dictionary.ValueType, hasChild: false);
        change = access?.GetChange(changes, memberName);
        return change is not null;
    }

    private static bool TryGetDictionaryItemChange(
        object change,
        Generated.IDictDescriptor dictionary,
        SparseCollectionChangeAccess access,
        SparsePathSegment segment,
        out ISparseChangeSet? itemChanges,
        out Generated.IDescriptorSet? itemDescriptors
    )
    {
        itemChanges = access.GetItemChange(change, segment);
        itemDescriptors = dictionary.GetValueDescriptors(segment.Key);
        return itemChanges is not null && itemDescriptors is not null;
    }
}
