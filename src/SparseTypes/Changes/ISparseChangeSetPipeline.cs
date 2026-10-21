using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Connects a generated change set to its model-specific rebase operation.</summary>
/// <typeparam name="TModel">The model type described by the change set.</typeparam>
/// <typeparam name="TChangeSet">The concrete change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseChangeSetPipeline<TModel, TChangeSet>
    where TChangeSet : class
{
    /// <summary>Rebases this change set onto the supplied current model.</summary>
    /// <param name="current">The current model state.</param>
    /// <returns>The rebased change set and any conflicts.</returns>
    RebaseResult<TChangeSet> RebaseForPipeline(TModel current);
}

/// <summary>Connects a generated edit session to authoritative save reconciliation.</summary>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TChangeSet">The submitted change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionPipeline<TModel, TChangeSet>
    where TModel : class
    where TChangeSet : class
{
    /// <summary>Reconciles the submitted change set with persisted values and applies them to the live model.</summary>
    /// <param name="submitted">The original submitted change set.</param>
    /// <param name="persisted">The authoritative persisted model.</param>
    /// <param name="conflicts">Typed-path conflicts when reconciliation fails.</param>
    /// <returns>True when the session adopted persisted state.</returns>
    bool TryReconcileForPipeline(TChangeSet submitted, TModel persisted, out IReadOnlyList<SparseConflict>? conflicts);
}
