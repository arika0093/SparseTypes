using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Edits a new collection item before inserting it into its parent list.</summary>
/// <typeparam name="TParent">The model containing the collection.</typeparam>
/// <typeparam name="TElement">The collection element type.</typeparam>
/// <typeparam name="TChildPaths">The generated path builder rooted at the element model.</typeparam>
/// <typeparam name="TChildSession">The generated edit-session type for the element model.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseCollectionAddDraft<TParent, TElement, TChildPaths, TChildSession, TConflict> : IDisposable
    where TParent : class
    where TElement : class
    where TChildPaths : ISparseTypedPath<TElement, TElement>
    where TChildSession : ISparseEditSessionWithPaths<TElement, TChildPaths>
    where TConflict : class
{
    private readonly SparseCollectionEditor<TElement> _editor;
    private readonly ISparseEditSessionWithChildren<TParent, TConflict> _parentSession;
    private bool _isOpen = true;

    internal SparseCollectionAddDraft(
        SparseCollectionEditor<TElement> editor,
        ISparseChildEditPath<TParent, TElement, TChildPaths, TChildSession> itemPath,
        TElement initialModel,
        ISparseEditSessionWithChildren<TParent, TConflict> parentSession
    )
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(itemPath);
        ArgumentNullException.ThrowIfNull(initialModel);
        ArgumentNullException.ThrowIfNull(parentSession);
        if (editor.Path.RootType != typeof(TParent) || itemPath.UntypedPath.RootType != typeof(TParent))
        {
            throw new ArgumentException("The collection and item paths must belong to the parent model.");
        }

        if (!editor.Path.IsAncestorOf(itemPath.UntypedPath))
        {
            throw new ArgumentException("The item path must be below the collection path.", nameof(itemPath));
        }

        _editor = editor;
        _parentSession = parentSession;
        CollectionPath = editor.Path;
        var baseline = itemPath.Clone(initialModel);
        Session = itemPath.CreateEditSession(baseline, itemPath.Clone(initialModel), null);
        parentSession.RegisterCollectionDraft(CollectionPath, this);
    }

    /// <summary>Gets the collection receiving the item when this draft is accepted.</summary>
    public SparsePath CollectionPath { get; }

    /// <summary>Gets the independent edit session for the uninserted item.</summary>
    public TChildSession Session { get; }

    /// <summary>Gets whether this draft is still open.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>Gets whether the parent collection path currently resolves.</summary>
    public bool IsTargetMissing => !_editor.IsAvailable(out _);

    /// <summary>Attempts to accept nested child sessions and insert the item.</summary>
    /// <param name="error">The collection or nested-session failure, or <c>None</c> on success.</param>
    /// <param name="conflicts">Nested-session conflicts when acceptance fails.</param>
    /// <returns>True when the item was inserted; otherwise false.</returns>
    public bool TryAcceptChanges(out SparseCollectionEditError error, out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfClosed();
        conflicts = null;
        if (Session is ISparseEditSessionWithChildren<TElement, TConflict> childSession)
        {
            if (childSession.HasPendingCollectionDrafts)
            {
                error = SparseCollectionEditError.PendingChildDraft;
                return false;
            }

            if (!childSession.TryAcceptChildSessions(out conflicts))
            {
                error = SparseCollectionEditError.ChildSessionConflict;
                return false;
            }
        }

        if (!_editor.TryAdd(Session.Model, out error))
        {
            return false;
        }

        Session.AcceptChanges();
        Close();
        return true;
    }

    /// <summary>Accepts the nested child sessions and inserts the item, throwing on failure.</summary>
    /// <exception cref="InvalidOperationException">A nested session conflicts or the collection rejects the item.</exception>
    public void AcceptChanges()
    {
        if (!TryAcceptChanges(out var error, out _))
        {
            throw new InvalidOperationException($"The collection item draft could not be accepted ({error}).");
        }
    }

    /// <summary>Discards this uninserted item and its nested child drafts.</summary>
    public void DiscardChanges()
    {
        ThrowIfClosed();
        if (Session is ISparseEditSessionWithChildren<TElement, TConflict> childSession)
        {
            childSession.DiscardChildSessions();
        }

        Session.AcceptChanges();
        Close();
    }

    /// <summary>Discards the draft when it is still open.</summary>
    public void Dispose()
    {
        if (_isOpen)
        {
            DiscardChanges();
        }
    }

    private void Close()
    {
        _isOpen = false;
        _parentSession.UnregisterCollectionDraft(this);
    }

    private void ThrowIfClosed()
    {
        if (!_isOpen)
        {
            throw new ObjectDisposedException(nameof(SparseCollectionAddDraft<TParent, TElement, TChildPaths, TChildSession, TConflict>));
        }
    }
}
