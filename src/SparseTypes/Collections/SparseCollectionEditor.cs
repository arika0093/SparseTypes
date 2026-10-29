using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>
/// Path-addressed typed editing facade for one session collection.
/// </summary>
/// <remarks>A thin typed front over the member's <see cref="IArrayDescriptor"/>.</remarks>
/// <typeparam name="TElement">The collection element model type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public class SparseCollectionEditor<TElement>
{
    private readonly IDescriptorSet _descriptors;
    private readonly Action<Action> _batch;
    private readonly object? _ownerSession;
    private readonly Func<TElement, TElement>? _cloneElement;

    /// <summary>Creates an editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="path">A collection member path (for example <c>Order.SparsePath.Customer.Key(id).Items</c>).</param>
    /// <param name="batch">The session batch runner (for example <c>session.BatchEdit</c>).</param>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address a collection member or belongs to a different root model.</exception>
    public SparseCollectionEditor(IDescriptorSet descriptors, SparsePath path, Action<Action> batch)
        : this(descriptors, path, batch, null, null) { }

    /// <summary>Creates an editing facade for a collection owned by an edit session.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="path">A collection member path.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <param name="ownerSession">The session that owns this facade.</param>
    public SparseCollectionEditor(IDescriptorSet descriptors, SparsePath path, Action<Action> batch, object? ownerSession)
        : this(descriptors, path, batch, ownerSession, null) { }

    /// <summary>Creates a session-owned collection editor with generated element cloning.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="path">A collection member path.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <param name="ownerSession">The session that owns this facade.</param>
    /// <param name="cloneElement">The generated deep-clone operation for collection items.</param>
    public SparseCollectionEditor(
        IDescriptorSet descriptors,
        SparsePath path,
        Action<Action> batch,
        object? ownerSession,
        Func<TElement, TElement>? cloneElement
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        if (path.IsRoot || path.Segments[path.Segments.Count - 1].Kind != SparsePathSegmentKind.Member)
        {
            throw new ArgumentException("The collection path must address a member.", nameof(path));
        }

        _ = descriptors.ResolveModel(SparsePath.Root(path.RootType));
        _descriptors = descriptors;
        _ownerSession = ownerSession;
        _cloneElement = cloneElement;
        Path = path;
        _batch = batch;
    }

    /// <summary>Gets the addressed collection path.</summary>
    public SparsePath Path { get; }

    /// <summary>Gets the live item count, or zero when the collection is unavailable.</summary>
    public int Count => TryResolveArray(out var array, out _) ? array.Count : 0;

    /// <summary>Gets whether items can currently be appended.</summary>
    public bool CanAdd => TryResolveArray(out var array, out _) && array.CanAdd;

    /// <summary>Gets whether items can currently be inserted.</summary>
    public bool CanInsert => TryResolveArray(out var array, out _) && array.CanInsert;

    /// <summary>Gets whether existing items can currently be replaced.</summary>
    public bool CanReplace => TryResolveArray(out var array, out _) && array.CanSetItem;

    /// <summary>Gets whether items can currently be removed.</summary>
    public bool CanRemove => TryResolveArray(out var array, out _) && array.CanRemove;

    /// <summary>Gets whether items can currently be moved.</summary>
    public bool CanMove => TryResolveArray(out var array, out _) && array.CanMove;

    /// <summary>Gets whether the collection carries stable keyed identity.</summary>
    public bool IsKeyed => TryResolveArray(out var array, out _) && array.IsKeyed;

    /// <summary>Gets the key type, or null for unkeyed or unavailable collections.</summary>
    public Type? KeyType => TryResolveArray(out var array, out _) ? array.KeyType : null;

    /// <summary>Gets the single key property name, or empty for unkeyed collections.</summary>
    public IReadOnlyList<string> KeyPropertyNames => TryResolveArray(out var array, out _) ? array.KeyPropertyNames : Array.Empty<string>();

    /// <summary>Reports whether the collection is currently readable.</summary>
    /// <param name="error">The resolution failure, or <c>None</c> when readable.</param>
    /// <returns>True when the member resolves to a live sequence.</returns>
    /// <remarks>Immutable collections stay readable; only edits report <c>NotMutable</c>.</remarks>
    public bool IsAvailable(out SparseCollectionEditError error) => TryResolveArray(out _, out error);

    /// <summary>Begins editing a new item without inserting it into the collection.</summary>
    /// <typeparam name="TParent">The model containing the collection.</typeparam>
    /// <typeparam name="TItem">The reference-type item model.</typeparam>
    /// <typeparam name="TChildPaths">The generated path builder rooted at the item model.</typeparam>
    /// <typeparam name="TChildSession">The generated edit-session type for the item model.</typeparam>
    /// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
    /// <param name="itemPath">A generated path to a collection element, usually <c>collectionPath.At(0)</c>.</param>
    /// <param name="initialModel">The initial item state.</param>
    /// <param name="parentSession">The parent session that tracks this uninserted draft.</param>
    /// <returns>An independent draft whose acceptance inserts the item.</returns>
    public SparseCollectionAddDraft<TParent, TItem, TChildPaths, TChildSession, TConflict> BeginAdd<
        TParent,
        TItem,
        TChildPaths,
        TChildSession,
        TConflict
    >(
        ISparseChildEditPath<TParent, TItem, TChildPaths, TChildSession> itemPath,
        TItem initialModel,
        ISparseEditSessionWithChildren<TParent, TConflict> parentSession
    )
        where TParent : class
        where TItem : class
        where TChildPaths : ISparseTypedPath<TItem, TItem>
        where TChildSession : ISparseEditSessionWithPaths<TItem, TChildPaths>
        where TConflict : class
    {
        if (typeof(TItem) != typeof(TElement))
        {
            throw new ArgumentException("The generated item path does not match this collection's element type.", nameof(itemPath));
        }

        if (_ownerSession is null || !ReferenceEquals(_ownerSession, parentSession))
        {
            throw new ArgumentException("The parent session must own this collection editor.", nameof(parentSession));
        }

        return new SparseCollectionAddDraft<TParent, TItem, TChildPaths, TChildSession, TConflict>(
            (SparseCollectionEditor<TItem>)(object)this,
            itemPath,
            initialModel,
            parentSession
        );
    }

    /// <summary>Reads the underlying model at a position.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="value">The element model, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the element was read.</returns>
    public bool TryGetAt(int index, out TElement? value, out SparseCollectionEditError error)
    {
        value = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        // Models, not observable proxies: the descriptor view returns proxies
        // for fragment elements, while the model resolver unwraps them.
        var model = array.GetItemModel(index);
        if (model is null)
        {
            return true;
        }

        if (model is TElement typed)
        {
            value = typed;
            error = SparseCollectionEditError.None;
            return true;
        }

        error = SparseCollectionEditError.InvalidItem;
        return false;
    }

    /// <summary>Appends an item as one batched user action.</summary>
    /// <param name="item">The element model to append.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was appended.</returns>
    public bool TryAdd(TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (!array.CanAdd)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        Batched(() => accepted = array.TryAdd(item));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = DiagnoseMutationFailure(array.CanAdd, array, item);
        return false;
    }

    /// <summary>Inserts an item at a position as one batched user action.</summary>
    /// <param name="index">The insertion position; equal to <see cref="Count"/> appends.</param>
    /// <param name="item">The element model to insert.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was inserted.</returns>
    public bool TryInsert(int index, TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index > array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (!array.CanInsert)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedIndex = index;
        Batched(() => accepted = array.TryInsert(capturedIndex, item));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = DiagnoseMutationFailure(array.CanInsert, array, item);
        return false;
    }

    /// <summary>Replaces the item at a position as one batched user action.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="item">The replacement element model.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was replaced.</returns>
    public bool TryReplace(int index, TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (HasPendingDraftForItem(array, index))
        {
            error = SparseCollectionEditError.PendingChildDraft;
            return false;
        }

        if (!array.CanSetItem)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedIndex = index;
        Batched(() => accepted = array.TrySetItem(capturedIndex, item));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = DiagnoseMutationFailure(array.CanSetItem, array, item);
        return false;
    }

    /// <summary>Removes the item at a position as one batched user action.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was removed.</returns>
    public bool TryRemoveAt(int index, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (HasPendingDraftForItem(array, index))
        {
            error = SparseCollectionEditError.PendingChildDraft;
            return false;
        }

        if (!array.CanRemove)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedIndex = index;
        Batched(() => accepted = array.TryRemoveAt(capturedIndex));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = array.CanRemove ? SparseCollectionEditError.IndexOutOfRange : SparseCollectionEditError.NotMutable;
        return false;
    }

    /// <summary>Moves an item as one batched user action, replacing handwritten remove-then-insert code.</summary>
    /// <param name="oldIndex">The current zero-based position.</param>
    /// <param name="newIndex">The target zero-based position.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was moved.</returns>
    public bool TryMove(int oldIndex, int newIndex, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (oldIndex < 0 || oldIndex >= array.Count || newIndex < 0 || newIndex >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (!array.CanMove)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedOld = oldIndex;
        var capturedNew = newIndex;
        Batched(() => accepted = array.TryMove(capturedOld, capturedNew));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = array.CanMove ? SparseCollectionEditError.IndexOutOfRange : SparseCollectionEditError.NotMutable;
        return false;
    }

    /// <summary>Runs one mutation through the session batch runner.</summary>
    /// <param name="mutation">The observable mutation to group.</param>
    protected void Batched(Action mutation) => _batch(mutation);

    /// <summary>Gets whether this editor was created with generated element cloning.</summary>
    protected bool HasElementCloner => _cloneElement is not null;

    /// <summary>Clones an element using the generated path's deep-copy operation.</summary>
    protected TElement CloneElement(TElement element)
    {
        if (_cloneElement is null)
        {
            throw new InvalidOperationException("The generated collection path does not provide an element clone operation.");
        }

        return _cloneElement(element);
    }

    /// <summary>Creates a detached deep copy of a collection item when a generated clone is available.</summary>
    protected bool TryCloneElement(TElement element, out TElement clone)
    {
        if (_cloneElement is null)
        {
            clone = default!;
            return false;
        }

        clone = _cloneElement(element);
        return clone is not null;
    }

    /// <summary>Gets the owner session when it exposes baseline collection state.</summary>
    protected bool TryGetCollectionState(out SparseCollectionState state)
    {
        state = default;
        if (_ownerSession is not ISparseEditSessionWithCollectionState stateSession)
        {
            return false;
        }

        try
        {
            state = stateSession.CollectionState(Path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Gets the retained baseline revision when the owner session exposes it.</summary>
    protected bool TryGetBaselineRevision(out long revision)
    {
        if (_ownerSession is ISparseEditSessionWithBaselineRevision baselineSession)
        {
            revision = baselineSession.BaselineRevision;
            return true;
        }

        revision = 0;
        return false;
    }

    /// <summary>Reports whether a child or uninserted draft is registered under the supplied path.</summary>
    protected bool HasPendingDraftUnder(SparsePath path) =>
        (_ownerSession is ISparseCollectionDraftRegistry drafts && drafts.HasPendingCollectionDraftUnder(path))
        || (_ownerSession is ISparseChildSessionRegistry children && children.HasPendingChildSessionUnder(path));

    /// <summary>Reports whether removing or replacing an item would invalidate an open nested draft.</summary>
    protected bool HasPendingDraftForItem(IArrayDescriptor array, int index)
    {
        var item = array.GetItemModel(index);
        if (item is not null && _ownerSession is ISparseCollectionDraftRegistry drafts && drafts.HasPendingCollectionDraftFor(item))
        {
            return true;
        }

        if (!array.IsKeyed)
        {
            return false;
        }

        var key = array.GetItemKey(index);
        if (key is not null && !array.IsUnassignedKey(key))
        {
            return HasPendingDraftUnder(Path.Key(key));
        }

        if (array.GetItemTemporaryKey(index) is { } temporaryKey)
        {
            return HasPendingDraftUnder(Path.TemporaryKey(temporaryKey));
        }

        return false;
    }

    /// <summary>Resolves the live sequence descriptor for one operation.</summary>
    /// <param name="array">The live descriptor, or null when unavailable.</param>
    /// <param name="error">The resolution failure, or <c>None</c> on success.</param>
    /// <returns>True when the path resolves to a live sequence.</returns>
    protected bool TryResolveArray(out IArrayDescriptor array, out SparseCollectionEditError error)
    {
        array = null!;
        var member = _descriptors.Find(Path);
        if (member is null)
        {
            error = Path.Segments.Count == 1 ? SparseCollectionEditError.UnknownCollection : SparseCollectionEditError.PathUnavailable;
            return false;
        }

        var sequence = member.Array;
        if (sequence is null)
        {
            // A supported shape without a live accessor means the member is
            // null; an unsupported shape means the member is no sequence.
            error = member.Shape.HasArray ? SparseCollectionEditError.NullCollection : SparseCollectionEditError.NotACollection;
            return false;
        }

        array = sequence;
        error = SparseCollectionEditError.None;
        return true;
    }

    private static SparseCollectionEditError DiagnoseMutationFailure(bool canMutate, IArrayDescriptor array, TElement? item)
    {
        // The descriptor already validated indexes and rejected duplicates
        // atomically without notifying; only the cause is left to report.
        if (!canMutate)
        {
            return SparseCollectionEditError.NotMutable;
        }

        if (item is null)
        {
            return array.IsItemNullable ? SparseCollectionEditError.DuplicateKey : SparseCollectionEditError.InvalidItem;
        }

        if (!array.ItemType.IsInstanceOfType(item) && !array.ItemViewType.IsInstanceOfType(item))
        {
            return SparseCollectionEditError.InvalidItem;
        }

        return SparseCollectionEditError.DuplicateKey;
    }
}
