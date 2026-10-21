using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore;

/// <summary>Applies change sets through a mapper plan cached for each EF model.</summary>
/// <typeparam name="TEntity">The entity type handled by the mapper.</typeparam>
/// <typeparam name="TDto">The DTO type carried by the change set.</typeparam>
public interface ISparseEntityMapper<TEntity, TDto>
    where TEntity : class
    where TDto : class
{
    /// <summary>Applies a change set to an entity tracked by the supplied context.</summary>
    /// <param name="context">The context whose model selects the mapping plan.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="changes">The accepted change set to apply.</param>
    void Apply(DbContext context, TEntity entity, ISparseChangeSet<TDto> changes);

    /// <summary>Applies a change set and returns details about the writes performed.</summary>
    /// <param name="context">The context whose model selects the mapping plan.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="changes">The accepted change set to apply.</param>
    /// <returns>The result describing created entities and applied paths.</returns>
    SparseEntityApplyResult ApplyWithResult(DbContext context, TEntity entity, ISparseChangeSet<TDto> changes);

    /// <summary>Projects an entity into its mapped DTO.</summary>
    /// <param name="context">The context whose model selects the mapping plan.</param>
    /// <param name="entity">The entity to project.</param>
    /// <returns>The DTO projected from the entity.</returns>
    TDto ToDto(DbContext context, TEntity entity);

    /// <summary>
    /// Preserves baseline temporary identities and assigns identities to
    /// entities created by an apply operation.
    /// </summary>
    /// <param name="context">The context that applied the change set.</param>
    /// <param name="result">The result returned by <see cref="ApplyWithResult"/>.</param>
    /// <param name="baseline">The prior DTO containing temporary identities to preserve.</param>
    /// <param name="authoritative">The current DTO to update with resolved identities.</param>
    void AssignTemporaryKeys(DbContext context, SparseEntityApplyResult result, TDto baseline, TDto authoritative);

    /// <summary>Rebases, applies, persists, reloads, and optionally reconciles one change set.</summary>
    /// <typeparam name="TContext">The concrete context type.</typeparam>
    /// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
    /// <param name="context">The context used to query and persist the entity.</param>
    /// <param name="changes">The original submitted change set.</param>
    /// <param name="filter">The expression selecting the single entity to update.</param>
    /// <param name="session">The edit session to reconcile after persistence, when present.</param>
    /// <param name="beforeSave">An optional callback after applying changes and before saving.</param>
    /// <param name="onConflict">An optional callback when rebasing finds semantic conflicts.</param>
    /// <param name="onReconcileFailed">An optional callback when session reconciliation fails.</param>
    /// <param name="cancellationToken">Cancels database operations and callbacks that honor it.</param>
    /// <returns>The rebase, persistence, and optional reconciliation outcome.</returns>
    Task<SparseEntityPipelineResult<TDto, TChangeSet>> PipelineExecuteAsync<TContext, TChangeSet>(
        TContext context,
        TChangeSet changes,
        Expression<Func<TEntity, bool>> filter,
        ISparseEditSessionPipeline<TDto, TChangeSet>? session = null,
        Func<SparseEntityBeforeSaveContext<TContext, TEntity, TDto, TChangeSet>, CancellationToken, Task>? beforeSave = null,
        Func<RebaseResult<TChangeSet>, CancellationToken, Task>? onConflict = null,
        Func<SparseEntityReconcileFailure<TDto, TChangeSet>, CancellationToken, Task>? onReconcileFailed = null,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext
        where TChangeSet : class, ISparseChangeSet<TDto>, ISparseChangeSetPipeline<TDto, TChangeSet>;
}
