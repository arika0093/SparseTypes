using System.ComponentModel;

namespace SparseTypes;

/// <summary>Identifies a generated collection path and its element model type.</summary>
/// <typeparam name="TModel">The root model type.</typeparam>
/// <typeparam name="TElement">The collection element type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseCollectionPath<TModel, TElement>
{
    /// <summary>Gets the model-agnostic path to the collection member.</summary>
    SparsePath UntypedPath { get; }

    /// <summary>Gets a typed path to an element at the given position.</summary>
    /// <param name="index">The zero-based position.</param>
    SparsePath<TModel, TElement> At(int index);

    /// <summary>Creates a detached deep copy of a collection element.</summary>
    /// <param name="element">The element to copy.</param>
    /// <returns>A deep copy that shares no mutable model objects with the source.</returns>
    TElement CloneElement(TElement element);
}

/// <summary>Identifies a generated keyed collection path and its assigned key type.</summary>
/// <typeparam name="TModel">The root model type.</typeparam>
/// <typeparam name="TElement">The collection element type.</typeparam>
/// <typeparam name="TKey">The assigned key type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseKeyedCollectionPath<TModel, TElement, TKey> : ISparseCollectionPath<TModel, TElement>
{
    /// <summary>Gets a typed path to the element with the given key.</summary>
    /// <param name="key">The assigned key.</param>
    SparsePath<TModel, TElement> Key(TKey key);
}
