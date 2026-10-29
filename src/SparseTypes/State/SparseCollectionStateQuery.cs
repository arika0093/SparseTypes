using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>Resolves collection state through generated typed change-set bridges.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseCollectionStateQuery
{
    /// <summary>Tries to query keyed or dictionary collection state at a root-preserving path.</summary>
    /// <typeparam name="TModel">The model type at the root of <paramref name="path"/>.</typeparam>
    /// <param name="changes">The captured change set.</param>
    /// <param name="descriptors">Descriptors for the current model graph.</param>
    /// <param name="path">The collection path to query.</param>
    /// <param name="state">The state snapshot when the path resolves.</param>
    /// <returns>True when the path resolves to a keyed or dictionary collection; otherwise false.</returns>
    public static bool TryGetState<TModel>(ISparseChangeSet changes, IDescriptorSet descriptors, SparsePath path, out SparseCollectionState? state)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(descriptors);
        state = null;
        if (path.RootType != typeof(TModel) || path.IsRoot || path.Segments.Count == 0)
        {
            return false;
        }

        var target = descriptors.Find(path);
        if (target?.Array is null && target?.Dictionary is null)
        {
            return false;
        }

        var currentChanges = changes;
        var currentDescriptors = descriptors;
        var index = 0;
        while (index < path.Segments.Count)
        {
            if (currentChanges is not ISparseCollectionChangeSetQueries collectionQueries)
            {
                return false;
            }

            var segment = path.Segments[index];
            if (segment.Kind != SparsePathSegmentKind.Member || !currentDescriptors.TryGet(segment.Name, out var member))
            {
                return false;
            }

            if (index == path.Segments.Count - 1)
            {
                if (!collectionQueries.TryGetCollectionState(segment.Name, path, out var collectionState))
                {
                    return false;
                }

                state = collectionState;
                return true;
            }

            var next = path.Segments[index + 1];
            if (next.Kind == SparsePathSegmentKind.Member)
            {
                if (!currentChanges.TryGetNestedChange(segment.Name, out var nestedChanges))
                {
                    state = SparseCollectionState.Empty(path);
                    return true;
                }

                if (member.Child is not { } childDescriptors)
                {
                    return false;
                }

                currentChanges = nestedChanges;
                currentDescriptors = childDescriptors;
                index++;
                continue;
            }

            if (member.Array is { IsKeyed: true } array && next.Kind is SparsePathSegmentKind.Key or SparsePathSegmentKind.TemporaryKey)
            {
                var itemIndex =
                    next.Kind == SparsePathSegmentKind.TemporaryKey
                        ? array.IndexOfTemporaryKey(next.TemporaryKey!.Value)
                        : array.IndexOfKey(next.Key);
                if (itemIndex < 0 || array.GetItemDescriptors(itemIndex) is not { } itemDescriptors)
                {
                    return false;
                }

                if (!collectionQueries.TryGetCollectionItemChange(segment.Name, next, out var itemChanges))
                {
                    state = SparseCollectionState.Empty(path);
                    return true;
                }

                currentChanges = itemChanges;
                currentDescriptors = itemDescriptors;
                index += 2;
                continue;
            }

            if (member.Dictionary is { } dictionary && next.Kind == SparsePathSegmentKind.Key)
            {
                if (dictionary.GetValueDescriptors(next.Key) is not { } valueDescriptors)
                {
                    return false;
                }

                if (!collectionQueries.TryGetCollectionItemChange(segment.Name, next, out var itemChanges))
                {
                    state = SparseCollectionState.Empty(path);
                    return true;
                }

                currentChanges = itemChanges;
                currentDescriptors = valueDescriptors;
                index += 2;
                continue;
            }

            return false;
        }

        return false;
    }
}
