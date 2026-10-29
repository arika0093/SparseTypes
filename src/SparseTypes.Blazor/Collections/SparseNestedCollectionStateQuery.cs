using SparseTypes.Generated;

namespace SparseTypes.Blazor;

internal static class SparseNestedCollectionStateQuery
{
    internal static bool TryGetState<TModel>(ISparseChangeSet changes, IDescriptorSet descriptors, SparsePath path, out SparseCollectionState? state)
        where TModel : class => SparseCollectionStateQuery.TryGetState<TModel>(changes, descriptors, path, out state);
}
