using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Projects a session's pending changes onto a separate model candidate.</summary>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithPendingState<TModel, TConflict>
    where TModel : class
    where TConflict : class
{
    /// <summary>Applies this session's pending changes to a separate model candidate.</summary>
    /// <param name="current">The model state to rebase pending changes onto.</param>
    /// <param name="updated">The candidate model on success; otherwise null.</param>
    /// <param name="conflicts">Structured conflicts when projection fails.</param>
    /// <returns>True when the candidate includes all pending changes; otherwise false.</returns>
    bool TryApplyPendingState(
        TModel current,
        out TModel? updated,
        out IReadOnlyList<TConflict>? conflicts
    );
}
