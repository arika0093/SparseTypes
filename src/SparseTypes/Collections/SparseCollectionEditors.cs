using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>Entry points for the path-addressed collection editing facade.</summary>
/// <remarks>Uses generated descriptors to edit collection members.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseCollectionEditors
{
    /// <summary>Opens a positional editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="collectionPath">A path ending at a collection member.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <typeparam name="TElement">The collection element model type.</typeparam>
    /// <returns>A facade bound to the addressed member.</returns>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address a collection member.</exception>
    public static SparseCollectionEditor<TElement> EditCollection<TElement>(
        this IDescriptorSet descriptors,
        SparsePath collectionPath,
        Action<Action> batch
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        return new SparseCollectionEditor<TElement>(descriptors, collectionPath, batch);
    }

    /// <summary>Opens a positional editing facade owned by an edit session.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="collectionPath">A path ending at a collection member.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <param name="ownerSession">The session that owns this facade.</param>
    /// <typeparam name="TElement">The collection element model type.</typeparam>
    /// <returns>A facade bound to the addressed member.</returns>
    public static SparseCollectionEditor<TElement> EditCollection<TElement>(
        this IDescriptorSet descriptors,
        SparsePath collectionPath,
        Action<Action> batch,
        object ownerSession
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(ownerSession);
        return new SparseCollectionEditor<TElement>(descriptors, collectionPath, batch, ownerSession);
    }

    /// <summary>Opens a keyed editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="collectionPath">A path ending at a collection member.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <typeparam name="TElement">The collection element model type.</typeparam>
    /// <typeparam name="TKey">The assigned key type declared by <c>[SparseKey]</c>.</typeparam>
    /// <returns>A facade bound to the addressed member.</returns>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address a collection member.</exception>
    public static SparseKeyedCollectionEditor<TElement, TKey> EditKeyedCollection<TElement, TKey>(
        this IDescriptorSet descriptors,
        SparsePath collectionPath,
        Action<Action> batch
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        return new SparseKeyedCollectionEditor<TElement, TKey>(descriptors, collectionPath, batch);
    }

    /// <summary>Opens a keyed editing facade owned by an edit session.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="collectionPath">A path ending at a collection member.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <param name="ownerSession">The session that owns this facade.</param>
    /// <typeparam name="TElement">The collection element model type.</typeparam>
    /// <typeparam name="TKey">The assigned key type declared by <c>[SparseKey]</c>.</typeparam>
    /// <returns>A facade bound to the addressed member.</returns>
    public static SparseKeyedCollectionEditor<TElement, TKey> EditKeyedCollection<TElement, TKey>(
        this IDescriptorSet descriptors,
        SparsePath collectionPath,
        Action<Action> batch,
        object ownerSession
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(ownerSession);
        return new SparseKeyedCollectionEditor<TElement, TKey>(descriptors, collectionPath, batch, ownerSession);
    }

    /// <summary>Opens a keyed editor with generated baseline and item-cloning support.</summary>
    /// <param name="descriptors">The session descriptor set.</param>
    /// <param name="collectionPath">A path ending at a collection member.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <param name="ownerSession">The session that owns this facade.</param>
    /// <param name="cloneElement">The generated deep-clone operation for collection items.</param>
    /// <typeparam name="TElement">The collection element model type.</typeparam>
    /// <typeparam name="TKey">The assigned key type.</typeparam>
    /// <returns>A keyed editor supporting baseline-relative row operations.</returns>
    public static SparseKeyedCollectionEditor<TElement, TKey> EditKeyedCollection<TElement, TKey>(
        this IDescriptorSet descriptors,
        SparsePath collectionPath,
        Action<Action> batch,
        object ownerSession,
        Func<TElement, TElement> cloneElement
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(ownerSession);
        ArgumentNullException.ThrowIfNull(cloneElement);
        return new SparseKeyedCollectionEditor<TElement, TKey>(descriptors, collectionPath, batch, ownerSession, cloneElement);
    }
}
