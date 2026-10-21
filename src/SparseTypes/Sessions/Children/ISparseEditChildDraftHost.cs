using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Adapts a generated edit session to the shared child-draft coordinator.</summary>
/// <typeparam name="TSession">The generated root edit-session type.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditChildDraftHost<TSession, TConflict>
    where TSession : class
    where TConflict : class
{
    /// <summary>Creates an independent session fork for a child draft.</summary>
    TSession ForkForChildDraft();

    /// <summary>Checks whether another session belongs to the same edit lineage.</summary>
    /// <param name="other">The session to compare with this session.</param>
    /// <returns>True when both sessions descend from the same root.</returns>
    bool IsInSameLineage(TSession other);

    /// <summary>Resolves a model value from this session without changing its tracking state.</summary>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The model value, or null when the path has no model target.</returns>
    object? ResolveChildDraftModel(SparsePath path);

    /// <summary>Refreshes change tracking after a child draft writes through a path.</summary>
    void RefreshAfterChildDraftMutation();

    /// <summary>Attempts to rebase and accept a child draft into this session.</summary>
    /// <typeparam name="TChild">The model addressed by the child path.</typeparam>
    /// <param name="fork">The independent session holding the draft.</param>
    /// <param name="path">The path to the child model.</param>
    /// <param name="writeModelValue">Writes the updated value into the existing model instance.</param>
    /// <param name="conflicts">Structured conflicts when the rebase cannot be accepted.</param>
    /// <returns>True when the draft was accepted; otherwise false.</returns>
    bool TryAcceptChildDraft<TChild>(TSession fork, SparsePath path, Action<TChild, TChild> writeModelValue, out IReadOnlyList<TConflict>? conflicts)
        where TChild : class;

    /// <summary>Checks whether a child draft can be accepted without mutating either session.</summary>
    /// <typeparam name="TChild">The model addressed by the child path.</typeparam>
    /// <param name="fork">The independent session holding the draft.</param>
    /// <param name="path">The path to the child model.</param>
    /// <param name="conflicts">Structured conflicts when the rebase cannot be accepted.</param>
    /// <returns>True when the draft is conflict-free; otherwise false.</returns>
    bool TryPreflightChildDraft<TChild>(TSession fork, SparsePath path, out IReadOnlyList<TConflict>? conflicts)
        where TChild : class;

    /// <summary>Confirms a parent commit by advancing the fork baseline to the accepted value.</summary>
    /// <typeparam name="TChild">The model addressed by the child path.</typeparam>
    /// <param name="fork">The independent session holding the draft.</param>
    /// <param name="path">The path to the child model.</param>
    /// <param name="writeModelValue">Writes the current value into the fork's existing model instance.</param>
    void ConfirmChildDraft<TChild>(TSession fork, SparsePath path, Action<TChild, TChild> writeModelValue)
        where TChild : class;
}
