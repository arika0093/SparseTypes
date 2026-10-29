using System.ComponentModel;

namespace SparseTypes;

/// <summary>Baseline snapshot and revision handle for a removed keyed collection row.</summary>
/// <typeparam name="TElement">The collection element type.</typeparam>
/// <typeparam name="TKey">The assigned key type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseRemovedCollectionRow<TElement, TKey>
{
    private readonly TElement _model;
    private readonly Func<TElement, TElement> _cloneElement;

    internal SparseRemovedCollectionRow(
        SparsePath collectionPath,
        TKey key,
        TElement model,
        int baselineIndex,
        long baselineRevision,
        Func<TElement, TElement> cloneElement
    )
    {
        CollectionPath = collectionPath;
        Key = key;
        _cloneElement = cloneElement;
        _model = cloneElement(model);
        BaselineIndex = baselineIndex;
        BaselineRevision = baselineRevision;
    }

    /// <summary>Gets the collection path that produced this handle.</summary>
    public SparsePath CollectionPath { get; }

    /// <summary>Gets the assigned key of the removed row.</summary>
    public TKey Key { get; }

    /// <summary>Gets a detached copy of the baseline row.</summary>
    public TElement Model => _cloneElement(_model);

    /// <summary>Gets the row's zero-based position in the baseline.</summary>
    public int BaselineIndex { get; }

    /// <summary>Gets the baseline revision captured when this handle was created.</summary>
    public long BaselineRevision { get; }

    internal TElement CreateRestoreModel() => _cloneElement(_model);
}
