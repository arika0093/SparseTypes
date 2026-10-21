using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Tracks a path-addressed child draft within a generated edit session.</summary>
/// <typeparam name="TChild">The model addressed by this draft.</typeparam>
/// <typeparam name="TSession">The generated root edit-session type.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditChildDraft<TChild, TSession, TConflict> : IDisposable
    where TChild : class
    where TSession : class
    where TConflict : class
{
    private readonly ISparseEditChildDraftHost<TSession, TConflict> _parentHost;
    private readonly TSession _fork;
    private readonly SparsePath _path;
    private readonly Action<TChild, TChild> _writeModelValue;
    private bool _isOpen = true;

    private SparseEditChildDraft(
        ISparseEditChildDraftHost<TSession, TConflict> parentHost,
        TSession fork,
        SparsePath path,
        Action<TChild, TChild> writeModelValue,
        TChild model
    )
    {
        _parentHost = parentHost;
        _fork = fork;
        _path = path;
        _writeModelValue = writeModelValue;
        Model = model;
    }

    /// <summary>Creates a child draft rooted at a generated typed path.</summary>
    /// <typeparam name="TParent">The type addressed by the parent session.</typeparam>
    /// <param name="parent">The parent session.</param>
    /// <param name="path">The generated typed path to the child model.</param>
    /// <returns>An independent child draft.</returns>
    /// <exception cref="ArgumentException">The path does not resolve to a writable model value.</exception>
    public static SparseEditChildDraft<TChild, TSession, TConflict> Create<TParent>(TSession parent, ISparseTypedPath<TParent, TChild> path)
        where TParent : class
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(path);
        var parentHost = GetHost(parent);
        if (parentHost.ResolveChildDraftModel(path.UntypedPath) is not TChild)
        {
            throw new ArgumentException("The path does not resolve to a model value of the requested type.", nameof(path));
        }

        if (!path.CanWriteModelValueInPlace)
        {
            throw new ArgumentException("The path does not address a generated model value.", nameof(path));
        }

        var fork = parentHost.ForkForChildDraft();
        var forkHost = GetHost(fork);
        if (forkHost.ResolveChildDraftModel(path.UntypedPath) is not TChild target)
        {
            throw new ArgumentException("The path does not resolve to a model value of the requested type.", nameof(path));
        }

        return new SparseEditChildDraft<TChild, TSession, TConflict>(parentHost, fork, path.UntypedPath, path.WriteModelValueInPlace, target);
    }

    /// <summary>Gets the editable child model.</summary>
    public TChild Model { get; }

    /// <summary>Gets whether the draft left the open state.</summary>
    public bool IsDetached => !_isOpen;

    /// <summary>Gets whether the addressed path currently resolves to no model.</summary>
    /// <remarks>The draft and its pending edits are retained while the target is missing.</remarks>
    public bool IsTargetMissing => _isOpen && _parentHost.ResolveChildDraftModel(_path) is not TChild;

    /// <summary>Attempts to merge the draft into the parent's current unsaved state.</summary>
    /// <param name="conflicts">Structured conflicts when the draft cannot be accepted.</param>
    /// <returns>True when the draft was accepted; otherwise false.</returns>
    /// <exception cref="InvalidOperationException">The draft is disconnected or its parent is invalid.</exception>
    public bool TryAcceptIntoParent(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisconnected();
        if (_parentHost.ResolveChildDraftModel(_path) is not TChild)
        {
            conflicts = null;
            return false;
        }

        return _parentHost.TryAcceptChildDraft(_fork, _path, _writeModelValue, out conflicts);
    }

    /// <summary>Checks whether the draft can be merged without changing its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the draft cannot be merged.</param>
    /// <returns>True when the draft is conflict-free; otherwise false.</returns>
    /// <exception cref="InvalidOperationException">The draft is disconnected or its parent is invalid.</exception>
    public bool TryPreflightIntoParent(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisconnected();
        if (_parentHost.ResolveChildDraftModel(_path) is not TChild)
        {
            conflicts = null;
            return false;
        }

        return _parentHost.TryPreflightChildDraft<TChild>(_fork, _path, out conflicts);
    }

    /// <summary>Acknowledges a committed merge without rewriting the parent.</summary>
    /// <exception cref="InvalidOperationException">The parent is invalid or the child target is missing.</exception>
    public void ConfirmAccept()
    {
        ThrowIfDisconnected(closeOnInvalidParent: false);
        _parentHost.ConfirmChildDraft(_fork, _path, _writeModelValue);
    }

    /// <summary>Refreshes the draft session after an external path write.</summary>
    public void RefreshParentDraft() => GetHost(_fork).RefreshAfterChildDraftMutation();

    /// <summary>Disconnects this draft.</summary>
    /// <inheritdoc />
    public void Dispose() => _isOpen = false;

    private static ISparseEditChildDraftHost<TSession, TConflict> GetHost(TSession session) =>
        session as ISparseEditChildDraftHost<TSession, TConflict>
        ?? throw new InvalidOperationException("The generated edit session does not support child drafts.");

    private void ThrowIfDisconnected(bool closeOnInvalidParent = true)
    {
        if (!_isOpen)
        {
            throw new InvalidOperationException("The child edit session is no longer connected.");
        }

        if (!_parentHost.IsInSameLineage(_fork))
        {
            if (closeOnInvalidParent)
            {
                Dispose();
            }

            throw new InvalidOperationException("The parent edit session is no longer connected or belongs to an unrelated edit lineage.");
        }
    }
}
