# EF Core Mapping

This guide shows how to map and apply incoming DTO `ChangeSet` modifications directly to tracked Entity Framework Core entities.

Only modified properties are written to the database. Untouched EF properties remain unmodified.

## Define a fragment mapper

Create a partial class annotated with `[SparseFragmentMapper<TEntity, TDto>]`. Matching same-name scalar properties map automatically. Configure overrides, renames, and child collections in `Configure()`:

<!-- illustrative: basic mapper declaration -->
```csharp
using SparseTypes.EntityFrameworkCore;

[SparseFragmentMapper<OrderEntity, OrderDto>]
public sealed partial class OrderMapper
{
    private void Configure()
    {
        // Renamed properties
        Map.Member(dto => dto.CustomerName, entity => entity.Name);

        // Value conversions
        Map.Member(
            dto => dto.Code,
            entity => entity.Code,
            code => code?.ToUpperInvariant()
        );

        // Suppress unpersisted fields
        Map.Ignore(dto => dto.CalculatedTotal);

        // Keyed collection mapping
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child => child.Ignore(line => line.Subtotal));
                lines.Delete();
                lines.Order(entity => entity.SortOrder);
            }
        );
    }
}
```

If your entity and DTO share identical member names and types, you can omit `Configure()` entirely:

<!-- illustrative: empty defaults-only mapper -->
```csharp
[SparseFragmentMapper<OrderEntity, OrderDto>]
public sealed partial class DefaultsOnlyMapper
{
}
```

## Load navigations and apply changes

Use `IncludeFor` to load the navigations needed to apply the incoming change set. Some edit-only collection changes produce a filtered Include containing only the matching rows. That tracked navigation is suitable for applying the change set, but it is not a complete DTO projection.

When rebasing or returning the saved state, query the full `Projection` separately. Do not use `ToDto` on an entity whose collection was loaded through a filtered Include; omitted rows would be absent from that DTO.

<!-- illustrative: controller or endpoint apply flow -->
```csharp
// 1. Create the mapper instance from your DbContext model
var mapper = OrderMapper.Create(db.Model);

// 2. Read the complete current DTO for conflict detection
var current = await db.Orders
    .AsNoTracking()
    .Where(order => order.Id == orderId)
    .Select(mapper.Projection)
    .SingleAsync();
var rebase = incomingChanges.RebaseOnto(current);
if (rebase.HasConflicts)
{
    return Results.Conflict(rebase.Conflicts);
}

// 3. Load only the navigations required to apply the accepted changes
var trackedOrder = await db.Orders
    .IncludeFor(mapper, rebase.Rebased)
    .SingleAsync(order => order.Id == orderId);

// 4. Apply changes directly to the tracked entity
mapper.Apply(db, trackedOrder, rebase.Rebased);

// 5. Persist to database
await db.SaveChangesAsync();

// 6. Read the complete authoritative DTO for the response
var authoritative = await db.Orders.AsNoTracking()
    .Where(order => order.Id == orderId)
    .Select(mapper.Projection)
    .SingleAsync();
```

The same full projection is needed after saving a collection edit. This query is the canonical response read:

<!-- sample: efcore-authoritative-reload -->
```csharp
var authoritative = await db
    .Orders.AsNoTracking()
    .Where(order => order.Id == orderId)
    .Select(mapper.Projection)
    .SingleAsync();
```
<!-- /sample -->

## Reconcile the client session after saving

Choose the reconciliation method based on which values the client must adopt. `TryReconcile` acknowledges the submitted change set, advances the session baseline to the authoritative model, and retargets temporary identities in the live model. It does not replace the live model with authoritative property values, so use it when the client already has the submitted values and only needs identity correlation.

`TryReconcileAndApply` adopts the authoritative model and rebases edits made after the submitted snapshot onto it. Use it when persistence can normalize values or when editing remains enabled while a save is in flight. A planning, correlation, or rebase conflict returns `false` without changing the live model or retained baseline. These conflict guarantees do not make arbitrary setters, property notifications, or callbacks transactional if they throw during the final in-place write.

If editing is disabled during the request, replace the session with one created
from the returned authoritative DTO instead. If the server persisted the
submitted values unchanged and the live model should remain untouched,
`AcceptChanges(submitted)` advances the baseline while preserving edits made
after capture. The [edit-session save guide](edit-session.md#submit-a-snapshot-and-acknowledge-the-response)
compares these acknowledgement choices.

## Handle client-assigned temporary keys

When clients create new child collection items before database primary keys exist, use `ApplyWithResult` and `AssignTemporaryKeys` to correlate client IDs with newly generated database keys:

<!-- illustrative: temporary key assignment workflow -->
```csharp
var result = mapper.ApplyWithResult(db, trackedOrder, rebase.Rebased);
await db.SaveChangesAsync();

mapper.AssignTemporaryKeys(db, result, current, authoritative);
```

`AssignTemporaryKeys` checks all parent, child, and sibling collection scopes before it invokes any temporary-key setter. A deterministic mapping failure, such as a created row missing from the authoritative DTO, leaves every DTO key unchanged. If a setter throws after validation, setters that already ran may have changed the DTO; the mapper cannot roll back arbitrary setter effects.

## Coordinate an optional save pipeline

When saving sparse modifications, applications must coordinate multiple steps: checking revisions, loading navigations with `IncludeFor`, applying changes with mappers, persisting to the database, reading authoritative state, correlating generated keys, and reconciling client edit sessions. If an exception or cancellation occurs partway through (such as during readback or while waiting for persistence), a standard `try/catch` cannot easily tell whether the database write succeeded, whether temporary keys were assigned, or whether the change set can be safely retried.

`SparseEntitySavePipeline.ExecuteAsync` is an orchestration helper that solves this by enforcing a strict four-phase execution order and classifying outcomes into unambiguous stages:

- **Strict phase progression**: Runs `prepare` → `save` → `readBack` → `synchronize` in order, executing each step at most once.
- **Pre-persistence safety**: If `prepare` fails or is canceled, `save` is guaranteed never to run, ensuring the submission can be safely retried without database side effects.
- **Accurate persistence status tracking**: When an exception occurs, the pipeline captures it and reports which stage was reached (`SparseEntitySaveStage`). Callers know whether persistence was not attempted (`PreparationFailed`), failed with a conflict (`ConcurrencyConflict`), completed (`ReadBackFailed`, `ClientSynchronizationFailed`), or has an unknown outcome (`SaveOutcomeUnknown`).
- **Lossless cancellation reporting**: If a callback raises `OperationCanceledException`, the pipeline preserves the persistence status and places the exception in `result.Error` instead of throwing, avoiding blind resubmissions of already-persisted writes.

The pipeline performs no queries, transactions, rollbacks, or retries on its own. It acts purely as an orchestrator across your callbacks.

### Concrete execution example

`ExecuteAsync<TPreparation, TReadBack>` has no generic type constraints on `TPreparation` or `TReadBack`. `TPreparation` is not a generated type; you define any container (such as a record, class, or tuple) to pass intermediate state from `prepare` to later callbacks:

<!-- illustrative: pipeline execution workflow -->
```csharp
// Caller-defined container to carry state from prepare to later callbacks (no generic constraints)
public sealed record SaveContext(
    OrderEntity TrackedOrder,
    SparseEntityApplyResult ApplyResult,
    OrderDto Baseline
);

// Coordinate the save pipeline
var result = await SparseEntitySavePipeline.ExecuteAsync<SaveContext, OrderDto>(
    // 1. prepare: Prepares entities before persistence (reads state, rebases, applies changes).
    // If this fails or cancels, returns PreparationFailed (or ConcurrencyConflict); save never runs.
    prepare: async ct =>
    {
        var current = await db.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(mapper.Projection)
            .SingleAsync(ct);

        var rebase = incomingChanges.RebaseOnto(current);
        if (rebase.HasConflicts)
        {
            throw new InvalidOperationException("Rebase conflict detected.");
        }

        var trackedOrder = await db.Orders
            .IncludeFor(mapper, rebase.Rebased)
            .SingleAsync(order => order.Id == orderId, ct);

        var applyResult = mapper.ApplyWithResult(db, trackedOrder, rebase.Rebased);
        return new SaveContext(trackedOrder, applyResult, current);
    },

    // 2. save: Persists changes exactly once. Receives prepared state.
    // Unclassified errors or cancellation return SaveOutcomeUnknown; never blindly retry.
    save: async (ctx, ct) =>
    {
        await db.SaveChangesAsync(ct);
    },

    // 3. readBack: Reads authoritative DTO and assigns temporary keys after successful save.
    // Failure or cancellation returns ReadBackFailed. Persistence succeeded; do not resubmit changes.
    readBack: async (ctx, ct) =>
    {
        var authoritative = await db.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(mapper.Projection)
            .SingleAsync(ct);

        mapper.AssignTemporaryKeys(db, ctx.ApplyResult, ctx.Baseline, authoritative);
        return authoritative;
    },

    // 4. synchronize: Reconciles live client session with authoritative readback.
    // Failure or cancellation returns ClientSynchronizationFailed. Database write already completed.
    synchronize: (ctx, authoritative, ct) =>
    {
        if (!session.TryReconcile(incomingChanges, authoritative, out var error))
        {
            throw new InvalidOperationException($"Reconcile failed: {error?.Kind}");
        }
        return Task.CompletedTask;
    },

    // cancellationToken: Forwarded to callbacks; reported in result.Error without throwing.
    cancellationToken: cancellationToken
);
```

### Handle stage outcomes and recovery

Evaluate `result.Stage` to determine the follow-up action:

| Stage | Persistence Status | Recommended Action |
|---|---|---|
| `Saved` | Completed | Success. Return or use `result.ReadBack` as authoritative state. |
| `PreparationFailed` | Not attempted | Persistence was not attempted. Safe to fix validation issues and retry with a fresh token. |
| `ConcurrencyConflict` | Not committed | A `DbUpdateConcurrencyException` occurred during prepare or save. Re-read current database state, rebase pending changes, and retry. |
| `SaveOutcomeUnknown` | Unknown | The write may have committed before an error or cancellation occurred. Query the database to inspect current state; do not blindly resubmit. |
| `ReadBackFailed` | Completed | Persistence succeeded. Re-read authoritative state from the database without re-saving. |
| `ClientSynchronizationFailed` | Completed | Persistence and readback completed. Resynchronize client session or reload state from `result.ReadBack` without re-saving. |

<!-- illustrative: stage outcome handling -->
```csharp
switch (result.Stage)
{
    case SparseEntitySaveStage.Saved:
        return Results.Ok(result.ReadBack);

    case SparseEntitySaveStage.ConcurrencyConflict:
        return Results.Conflict(result.Error?.Message);

    case SparseEntitySaveStage.PreparationFailed:
        return Results.BadRequest(result.Error?.Message);

    case SparseEntitySaveStage.SaveOutcomeUnknown:
    case SparseEntitySaveStage.ReadBackFailed:
    case SparseEntitySaveStage.ClientSynchronizationFailed:
        // Recovery required: database write may have succeeded or already completed
        return Results.Problem("Persistence outcome requires verification.", statusCode: 500);
}
```

### Recover from cancellation without losing the persistence status

`ExecuteAsync` reports cancellation instead of throwing it. When a callback honors the token and raises `OperationCanceledException`, the result keeps that exception in `Error` and the stage keeps what is known about persistence:

- Preparation cancellation returns `PreparationFailed`. The save callback never ran, so nothing was persisted and the same change set can be retried with a fresh token.
- Save cancellation returns `SaveOutcomeUnknown`. The write may have committed before cancellation surfaced, so query persisted state first instead of resubmitting. This also covers a callback that saves successfully and observes cancellation afterwards.
- Readback cancellation returns `ReadBackFailed`. Persistence completed, so re-read the authoritative state and continue without resubmitting.
- Synchronization cancellation returns `ClientSynchronizationFailed`. Persistence and readback completed, so finish client synchronization (for example `TryReconcileAndApply`) without resubmitting.

The pipeline performs no cancellation check between stages and never retries a stage. A callback that ignores the token runs to completion, and an already-canceled token surfaces at the first callback that honors it, normally as `PreparationFailed`. Detect cancellation by testing `Error is OperationCanceledException`:

<!-- illustrative: cancellation handling excerpt; pipeline and DTO names are defined elsewhere -->
```csharp
var outcome = await SparseEntitySavePipeline.ExecuteAsync(
    prepare, save, readBack, synchronize, cancellationToken);
if (outcome.Error is OperationCanceledException)
{
    // The stage tells whether persistence was not attempted
    // (PreparationFailed), is unknown (SaveOutcomeUnknown), or completed
    // (ReadBackFailed, ClientSynchronizationFailed).
}
```

## Guard the SQLite write race with an application-managed token

SQLite cannot store-generate a SQL Server style rowversion, so declare an
application-managed token (for example a `Guid`) and advance it on every
write that belongs to the aggregate, including owned-navigation and
child-only line changes:

1. Mark the token with `IsConcurrencyToken()` and keep it read-only for
   the mapper: `Map.Ignore` for writes, `Map.Read` for the projection.
2. Assign a new value in `SaveChanges`/`SaveChangesAsync` for every added
   or modified aggregate root, and for the parent of every changed owned
   navigation or added, modified, or deleted child. EF can leave the parent
   unchanged when only an owned value or child changes, so bump the parent
   explicitly. This also covers adding, replacing, or deleting an owned
   reference.
3. Carry the token outside the change set: return it on reads, accept it
   as `If-Match` on writes, and set it as the entry's `OriginalValue`
   before saving.
4. Catch `DbUpdateConcurrencyException` and answer 412 with refresh
   guidance. Keep semantic `RebaseOnto` conflicts as 409. Never resubmit
   the same change set blindly; re-read, rebase pending edits onto the new
   current state, and submit with the fresh token. Let cancellation
   propagate instead of mapping it to 409 or 412.

<!-- illustrative: token plumbing excerpt; context and DTO names are defined elsewhere -->
```csharp
db.Entry(order).Property(static item => item.Version).OriginalValue = clientVersion;
try
{
    await db.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateConcurrencyException)
{
    return Results.Problem("The order changed since it was read.", statusCode: 412);
}
```

For full mapper configuration options, custom write handlers, and metadata constraints, see [EF Core mapping reference](../reference/efcore-mapping.md).
