using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Tracks independent edit histories owned by one generated session.</summary>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditHistoryCollection<TChangeSet>
    where TChangeSet : class
{
    private readonly List<SparseEditHistory<TChangeSet>> _histories = new List<SparseEditHistory<TChangeSet>>();

    /// <summary>Adds a history and removes it from the collection when disposed.</summary>
    /// <param name="history">The history to track.</param>
    public void Add(SparseEditHistory<TChangeSet> history)
    {
        if (history == null)
        {
            throw new System.ArgumentNullException(nameof(history));
        }

        _histories.Add(history);
        history.Disposed += () => _histories.Remove(history);
    }

    /// <summary>Clears every tracked history without disposing it.</summary>
    public void Clear()
    {
        for (var index = 0; index < _histories.Count; index++)
        {
            _histories[index].Clear();
        }
    }
}

/// <summary>Non-disposable control over a history whose lifetime belongs to its session.</summary>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseOwnedEditHistory<TChangeSet> : ISparseEditHistory
    where TChangeSet : class
{
    private SparseEditHistory<TChangeSet>? _inner;

    /// <summary>Creates the session-owned view over a typed history.</summary>
    /// <param name="history">The history retained by the owning session.</param>
    public SparseOwnedEditHistory(SparseEditHistory<TChangeSet> history)
    {
        _inner = history ?? throw new System.ArgumentNullException(nameof(history));
        _inner.StateChanged += OnStateChanged;
    }

    /// <summary>Raised after undo/redo stack state changes.</summary>
    public event System.Action? StateChanged;

    /// <summary>Gets whether an undo entry is available.</summary>
    public bool CanUndo => Inner.CanUndo;

    /// <summary>Gets whether a redo entry is available.</summary>
    public bool CanRedo => Inner.CanRedo;

    /// <summary>Gets the retained undo entry count.</summary>
    public int UndoCount => Inner.UndoCount;

    /// <summary>Gets the retained redo entry count.</summary>
    public int RedoCount => Inner.RedoCount;

    /// <summary>Gets the maximum retained undo entries.</summary>
    public int MaxEntries => Inner.MaxEntries;

    /// <summary>Undoes the newest entry, throwing on conflict.</summary>
    public void Undo() => Inner.Undo();

    /// <summary>Attempts to undo the newest entry.</summary>
    /// <param name="conflicts">Conflicts when the operation fails.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryUndo(out System.Collections.Generic.IReadOnlyList<SparseConflict>? conflicts) => Inner.TryUndo(out conflicts);

    /// <summary>Redoes the newest undone entry, throwing on conflict.</summary>
    public void Redo() => Inner.Redo();

    /// <summary>Attempts to redo the newest undone entry.</summary>
    /// <param name="conflicts">Conflicts when the operation fails.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryRedo(out System.Collections.Generic.IReadOnlyList<SparseConflict>? conflicts) => Inner.TryRedo(out conflicts);

    /// <summary>Begins an explicit group across multiple events.</summary>
    /// <returns>A scope that commits the group on dispose.</returns>
    public System.IDisposable BeginGroup() => Inner.BeginGroup();

    /// <summary>Runs an action inside one explicit history group.</summary>
    /// <param name="action">The edits to group.</param>
    public void Group(System.Action action) => Inner.Group(action);

    /// <summary>Clears undo entries, redo entries, and any buffered group.</summary>
    public void Clear() => Inner.Clear();

    /// <summary>Disposes the wrapped history as part of owner-controlled shutdown.</summary>
    public void DisposeOwned()
    {
        var history = _inner;
        _inner = null;
        if (history == null)
        {
            return;
        }

        history.StateChanged -= OnStateChanged;
        history.Dispose();
    }

    private SparseEditHistory<TChangeSet> Inner => _inner ?? throw new System.ObjectDisposedException(GetType().FullName);

    private void OnStateChanged()
    {
        StateChanged?.Invoke();
    }
}
