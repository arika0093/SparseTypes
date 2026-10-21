using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore;

/// <summary>Identifies the outcome of one mapper-managed save operation.</summary>
public enum SparseEntityPipelineStage
{
    /// <summary>Persistence and any requested session reconciliation completed.</summary>
    Saved,

    /// <summary>Rebasing detected semantic conflicts and persistence was not attempted.</summary>
    RebaseConflict,

    /// <summary>Persistence completed, but the edit session could not adopt the authoritative state.</summary>
    ReconcileFailed,
}

/// <summary>The result of one mapper-managed save operation.</summary>
/// <typeparam name="TDto">The mapped model type.</typeparam>
/// <typeparam name="TChangeSet">The submitted generated change-set type.</typeparam>
public sealed class SparseEntityPipelineResult<TDto, TChangeSet>
    where TDto : class
    where TChangeSet : class
{
    internal SparseEntityPipelineResult(
        SparseEntityPipelineStage stage,
        TDto current,
        TChangeSet submitted,
        RebaseResult<TChangeSet> rebase,
        SparseEntityApplyResult? applyResult,
        TDto? readBack,
        IReadOnlyList<SparseConflict>? reconcileConflicts
    )
    {
        Stage = stage;
        Current = current;
        Submitted = submitted;
        Rebase = rebase;
        ApplyResult = applyResult;
        ReadBack = readBack;
        ReconcileConflicts = reconcileConflicts;
    }

    /// <summary>Gets the operation outcome.</summary>
    public SparseEntityPipelineStage Stage { get; }

    /// <summary>Gets the current model read before rebasing.</summary>
    public TDto Current { get; }

    /// <summary>Gets the original submitted change set.</summary>
    public TChangeSet Submitted { get; }

    /// <summary>Gets the rebase result used by the operation.</summary>
    public RebaseResult<TChangeSet> Rebase { get; }

    /// <summary>Gets apply details when persistence was attempted.</summary>
    public SparseEntityApplyResult? ApplyResult { get; }

    /// <summary>Gets the authoritative model read after persistence.</summary>
    public TDto? ReadBack { get; }

    /// <summary>Gets typed conflicts when session reconciliation failed.</summary>
    public IReadOnlyList<SparseConflict>? ReconcileConflicts { get; }
}

/// <summary>Provides typed state to a callback that runs immediately before persistence.</summary>
/// <typeparam name="TContext">The concrete DbContext type.</typeparam>
/// <typeparam name="TEntity">The EF entity type.</typeparam>
/// <typeparam name="TDto">The mapped model type.</typeparam>
/// <typeparam name="TChangeSet">The submitted change-set type.</typeparam>
public sealed class SparseEntityBeforeSaveContext<TContext, TEntity, TDto, TChangeSet>
    where TContext : DbContext
    where TEntity : class
    where TDto : class
    where TChangeSet : class
{
    internal SparseEntityBeforeSaveContext(
        TContext db,
        TEntity entity,
        TDto current,
        TChangeSet submitted,
        TChangeSet rebased,
        SparseEntityApplyResult applyResult
    )
    {
        Db = db;
        Entity = entity;
        Current = current;
        Submitted = submitted;
        Rebased = rebased;
        ApplyResult = applyResult;
    }

    /// <summary>Gets the DbContext used for this save operation.</summary>
    public TContext Db { get; }

    /// <summary>Gets the tracked entity receiving the accepted changes.</summary>
    public TEntity Entity { get; }

    /// <summary>Gets the current model read before rebasing.</summary>
    public TDto Current { get; }

    /// <summary>Gets the original submitted change set.</summary>
    public TChangeSet Submitted { get; }

    /// <summary>Gets the rebased change set applied to the entity.</summary>
    public TChangeSet Rebased { get; }

    /// <summary>Gets the result describing the writes performed.</summary>
    public SparseEntityApplyResult ApplyResult { get; }
}

/// <summary>Describes a failed attempt to reconcile a session after persistence.</summary>
/// <typeparam name="TDto">The mapped model type.</typeparam>
/// <typeparam name="TChangeSet">The submitted change-set type.</typeparam>
public sealed class SparseEntityReconcileFailure<TDto, TChangeSet>
    where TDto : class
    where TChangeSet : class
{
    internal SparseEntityReconcileFailure(TChangeSet submitted, TDto persisted, IReadOnlyList<SparseConflict>? conflicts)
    {
        Submitted = submitted;
        Persisted = persisted;
        Conflicts = conflicts;
    }

    /// <summary>Gets the original submitted change set.</summary>
    public TChangeSet Submitted { get; }

    /// <summary>Gets the authoritative model already persisted to the database.</summary>
    public TDto Persisted { get; }

    /// <summary>Gets the typed conflicts returned by session reconciliation.</summary>
    public IReadOnlyList<SparseConflict>? Conflicts { get; }
}
