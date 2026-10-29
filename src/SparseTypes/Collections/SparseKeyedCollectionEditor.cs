using System.Collections.Generic;
using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>
/// Path-addressed typed editing facade for one keyed session collection.
/// </summary>
/// <remarks>Adds stable-identity operations over <see cref="SparseCollectionEditor{TElement}"/>.</remarks>
/// <typeparam name="TElement">The collection element model type.</typeparam>
/// <typeparam name="TKey">The assigned key type declared by <c>[SparseKey]</c>.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseKeyedCollectionEditor<TElement, TKey> : SparseCollectionEditor<TElement>
{
    /// <summary>Creates a keyed editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="path">A collection member path (for example <c>Order.SparsePath.Customer.Key(id).Items</c>).</param>
    /// <param name="batch">The session batch runner (for example <c>session.BatchEdit</c>).</param>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address a collection member or belongs to a different root model.</exception>
    public SparseKeyedCollectionEditor(IDescriptorSet descriptors, SparsePath path, Action<Action> batch)
        : this(descriptors, path, batch, null) { }

    /// <summary>Creates a keyed editing facade owned by an edit session.</summary>
    /// <param name="descriptors">The session descriptor set.</param>
    /// <param name="path">A collection member path.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <param name="ownerSession">The session that owns this facade.</param>
    public SparseKeyedCollectionEditor(IDescriptorSet descriptors, SparsePath path, Action<Action> batch, object? ownerSession)
        : base(descriptors, path, batch, ownerSession) { }

    /// <summary>Creates a keyed editor with generated baseline and item-cloning support.</summary>
    /// <param name="descriptors">The session descriptor set.</param>
    /// <param name="path">A collection member path.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <param name="ownerSession">The session that owns this facade.</param>
    /// <param name="cloneElement">The generated deep-clone operation for collection items.</param>
    public SparseKeyedCollectionEditor(
        IDescriptorSet descriptors,
        SparsePath path,
        Action<Action> batch,
        object? ownerSession,
        Func<TElement, TElement> cloneElement
    )
        : base(descriptors, path, batch, ownerSession, cloneElement) { }

    /// <summary>Finds the current position of an assigned key.</summary>
    /// <param name="key">The assigned key to locate.</param>
    /// <returns>The position, or -1 for unassigned, absent, ambiguous, or unkeyed identities.</returns>
    /// <remarks>Unassigned sentinels never resolve; check <see cref="IsUnassignedKey"/> first when needed.</remarks>
    public int IndexOfKey(TKey? key)
    {
        if (!TryResolveArray(out var array, out _) || !IsUsableKeyedArray(array))
        {
            return -1;
        }

        if (key is null || array.IsUnassignedKey(key))
        {
            return -1;
        }

        return array.IndexOfKey(key);
    }

    /// <summary>Finds the current position of a temporary identity.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    /// <returns>The position, or -1 when empty, absent, ambiguous, or unsupported.</returns>
    public int IndexOfTemporaryKey(Guid temporaryKey)
    {
        if (!TryResolveArray(out var array, out _) || temporaryKey == Guid.Empty)
        {
            return -1;
        }

        return array.IndexOfTemporaryKey(temporaryKey);
    }

    /// <summary>Determines whether a key is the configured unassigned marker.</summary>
    /// <param name="key">The key to test.</param>
    /// <returns>True when the key marks rows without durable identity.</returns>
    public bool IsUnassignedKey(TKey? key)
    {
        if (!TryResolveArray(out var array, out _))
        {
            return false;
        }

        return array.IsUnassignedKey(key);
    }

    /// <summary>Determines whether an assigned key currently has a row.</summary>
    /// <param name="key">The assigned key to test.</param>
    /// <returns>True when exactly the durable identity resolves; unassigned keys never count.</returns>
    public bool ContainsKey(TKey? key) => IndexOfKey(key) >= 0;

    /// <summary>Reads the assigned key of the row at a position, for UI row keys.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="key">The row key, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the key was read.</returns>
    public bool TryGetKeyAt(int index, out TKey? key, out SparseCollectionEditError error)
    {
        key = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        var raw = array.GetItemKey(index);
        if (raw is null)
        {
            // A null row key carries no durable identity; callers interpret it
            // through IsUnassignedKey rather than treating it as a lookup key.
            error = SparseCollectionEditError.None;
            return true;
        }

        if (raw is TKey typed)
        {
            key = typed;
            error = SparseCollectionEditError.None;
            return true;
        }

        error = SparseCollectionEditError.InvalidItem;
        return false;
    }

    /// <summary>Reads a row by its assigned key through the current order.</summary>
    /// <param name="key">The assigned key to locate.</param>
    /// <param name="value">The element model, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was read.</returns>
    public bool TryGetByKey(TKey? key, out TElement? value, out SparseCollectionEditError error)
    {
        value = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        return TryGetAt(index, out value, out error);
    }

    /// <summary>Reads a pending row by its temporary identity.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    /// <param name="value">The element model, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was read.</returns>
    public bool TryGetByTemporaryKey(Guid temporaryKey, out TElement? value, out SparseCollectionEditError error)
    {
        value = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (temporaryKey == Guid.Empty)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        var index = array.IndexOfTemporaryKey(temporaryKey);
        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        return TryGetAt(index, out value, out error);
    }

    /// <summary>Captures removed rows from the current retained baseline.</summary>
    /// <param name="rows">The detached baseline snapshots that can be restored.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the editor can query a generated baseline-aware session.</returns>
    public bool TryGetRemovedRows(out IReadOnlyList<SparseRemovedCollectionRow<TElement, TKey>> rows, out SparseCollectionEditError error)
    {
        rows = Array.Empty<SparseRemovedCollectionRow<TElement, TKey>>();
        if (!TryGetCollectionState(out var state) || !TryGetBaselineRevision(out var revision) || !HasElementCloner)
        {
            error = SparseCollectionEditError.BaselineUnavailable;
            return false;
        }

        var result = new List<SparseRemovedCollectionRow<TElement, TKey>>();
        foreach (var item in state.ChangedItems)
        {
            if (!item.IsRemoved)
            {
                continue;
            }

            if (item.Key is not TKey key || !item.Before.IsPresent || item.Before.Value is not TElement model)
            {
                error = SparseCollectionEditError.InvalidItem;
                return false;
            }

            result.Add(new SparseRemovedCollectionRow<TElement, TKey>(Path, key, model, item.BeforeIndex, revision, CloneElement));
        }

        rows = result;
        error = SparseCollectionEditError.None;
        return true;
    }

    /// <summary>Restores a removed baseline row near its original position without reordering surviving rows.</summary>
    /// <param name="row">A removed-row handle from this collection editor.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the baseline row was inserted.</returns>
    public bool TryRestoreRemoved(SparseRemovedCollectionRow<TElement, TKey> row, out SparseCollectionEditError error)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.CollectionPath.Equals(Path))
        {
            error = SparseCollectionEditError.InvalidItem;
            return false;
        }

        if (!TryGetBaselineRevision(out var revision) || revision != row.BaselineRevision)
        {
            error = SparseCollectionEditError.StaleBaseline;
            return false;
        }

        if (!TryGetCollectionState(out var state))
        {
            error = SparseCollectionEditError.BaselineUnavailable;
            return false;
        }

        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        var itemPath = Path.Key(row.Key);
        if (HasPendingDraftUnder(itemPath))
        {
            error = SparseCollectionEditError.PendingChildDraft;
            return false;
        }

        if (array.IndexOfKey(row.Key) >= 0)
        {
            error = SparseCollectionEditError.DuplicateKey;
            return false;
        }

        if (!array.CanInsert)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var insertionIndex = FindRestoreIndex(array, state, row.BaselineIndex);
        return TryInsert(insertionIndex, row.CreateRestoreModel(), out error);
    }

    /// <summary>Resets a changed row to its latest baseline values without changing its position.</summary>
    /// <param name="key">The assigned key of the row.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was reset or already matched its baseline.</returns>
    public bool TryResetByKey(TKey? key, out SparseCollectionEditError error)
    {
        if (!TryGetCollectionState(out var state) || !TryGetBaselineRevision(out _) || !HasElementCloner)
        {
            error = SparseCollectionEditError.BaselineUnavailable;
            return false;
        }

        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        var itemState = state.GetItemState(key);
        if (itemState.IsEmpty)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        if (itemState.IsReordered && !itemState.IsEdited && !itemState.IsAdded)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        var itemPath = Path.Key(key);
        if (HasPendingDraftUnder(itemPath))
        {
            error = SparseCollectionEditError.PendingChildDraft;
            return false;
        }

        if (itemState.IsAdded)
        {
            return TryRemoveAt(index, out error);
        }

        if (!itemState.TryGetBaselineValue(array.GetItemModel(index), out var baselineValue) || baselineValue is not TElement baseline)
        {
            error = SparseCollectionEditError.BaselineUnavailable;
            return false;
        }

        if (!TryCloneElement(baseline, out var clone) || clone is null)
        {
            error = SparseCollectionEditError.BaselineUnavailable;
            return false;
        }

        return TryReplace(index, clone, out error);
    }

    /// <summary>Begins a new child draft initialized from a deep copy of an existing row.</summary>
    /// <typeparam name="TParent">The model containing the collection.</typeparam>
    /// <typeparam name="TItem">The collection item model type.</typeparam>
    /// <typeparam name="TChildPaths">The generated path builder rooted at the item model.</typeparam>
    /// <typeparam name="TChildSession">The generated edit-session type for the item model.</typeparam>
    /// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
    /// <param name="key">The assigned key of the source row.</param>
    /// <param name="itemPath">A generated child path for the item model.</param>
    /// <param name="initializeCopy">Optional app-owned initialization for IDs and business values.</param>
    /// <param name="parentSession">The session that owns this collection editor.</param>
    /// <returns>An independent draft whose acceptance inserts the duplicate.</returns>
    public SparseCollectionAddDraft<TParent, TItem, TChildPaths, TChildSession, TConflict> BeginDuplicate<
        TParent,
        TItem,
        TChildPaths,
        TChildSession,
        TConflict
    >(
        TKey? key,
        ISparseChildEditPath<TParent, TItem, TChildPaths, TChildSession> itemPath,
        Action<TItem>? initializeCopy,
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

        if (!TryGetByKey(key, out var source, out var error) || source is not TItem)
        {
            throw new InvalidOperationException($"The source row could not be cloned ({error}).");
        }

        var sourceCopy = CloneElement(source);
        if (sourceCopy is not TItem copy)
        {
            throw new InvalidOperationException("The generated collection clone returned an incompatible item.");
        }

        initializeCopy?.Invoke(copy);
        return BeginAdd<TParent, TItem, TChildPaths, TChildSession, TConflict>(itemPath, copy, parentSession);
    }

    private static int FindRestoreIndex(IArrayDescriptor array, SparseCollectionState state, int baselineIndex)
    {
        var before = Math.Min(baselineIndex - 1, state.BaselineOrder.Count - 1);
        for (var index = before; index >= 0; index--)
        {
            if (state.BaselineOrder[index] is TKey key && array.IndexOfKey(key) >= 0)
            {
                return array.IndexOfKey(key) + 1;
            }
        }

        for (var index = Math.Max(0, baselineIndex + 1); index < state.BaselineOrder.Count; index++)
        {
            if (state.BaselineOrder[index] is TKey key)
            {
                var liveIndex = array.IndexOfKey(key);
                if (liveIndex >= 0)
                {
                    return liveIndex;
                }
            }
        }

        return array.Count;
    }

    /// <summary>Removes the row with an assigned key as one batched user action.</summary>
    /// <param name="key">The assigned key to remove.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was removed.</returns>
    public bool TryRemoveByKey(TKey? key, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        if (HasPendingDraftUnder(Path.Key(key)) || HasPendingDraftForItem(array, index))
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

        error = array.CanRemove ? SparseCollectionEditError.UnknownKey : SparseCollectionEditError.NotMutable;
        return false;
    }

    /// <summary>Removes a pending row by its temporary identity as one batched user action.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was removed.</returns>
    public bool TryRemoveByTemporaryKey(Guid temporaryKey, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (temporaryKey == Guid.Empty)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        var index = array.IndexOfTemporaryKey(temporaryKey);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        return TryRemoveAt(index, out error);
    }

    /// <summary>Replaces the row with an assigned key as one batched user action.</summary>
    /// <param name="key">The assigned key to replace.</param>
    /// <param name="item">The replacement element model.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was replaced.</returns>
    /// <remarks>Changing the key is remove plus add.</remarks>
    public bool TryReplaceByKey(TKey? key, TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        return TryReplace(index, item, out error);
    }

    /// <summary>Moves the row with an assigned key as one batched user action.</summary>
    /// <param name="key">The assigned key to move.</param>
    /// <param name="newIndex">The target zero-based position.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was moved.</returns>
    public bool TryMoveByKey(TKey? key, int newIndex, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        // Reuse the positional move so index validation stays in one place;
        // the lookup above already pinned identity to the current order.
        return TryMove(index, newIndex, out error);
    }

    private static bool IsUsableKeyedArray(IArrayDescriptor array) => array.IsKeyed && array.KeyType == typeof(TKey);

    private static void RequireUsableKey(IArrayDescriptor array)
    {
        if (!array.IsKeyed)
        {
            throw new InvalidOperationException("The addressed collection carries no stable keyed identity.");
        }

        if (array.KeyType != typeof(TKey))
        {
            throw new InvalidOperationException(
                "The keyed editor key type '"
                    + typeof(TKey)
                    + "' does not match the collection key type '"
                    + (array.KeyType?.ToString() ?? "<unknown>")
                    + "'."
            );
        }
    }
}
