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

## Save and synchronize a change set

`PipelineExecuteAsync` reads the current DTO, rebases the submitted change set, loads the required navigations, applies the accepted changes, saves once, reads the authoritative DTO, assigns generated temporary keys, and reconciles the optional session. Its type arguments are inferred from the mapper, context, change set, and session.

<!-- sample: efcore-mapper-pipeline-save -->
```csharp
var result = await mapper.PipelineExecuteAsync(
    db,
    incomingChanges,
    filter: order => order.Id == orderId,
    session: session,
    beforeSave: (save, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    },
    onConflict: (rebase, _) =>
    {
        Console.WriteLine($"Rebase conflicts: {rebase.Conflicts.Count}");
        return Task.CompletedTask;
    },
    onReconcileFailed: (failure, _) =>
    {
        Console.WriteLine($"Session conflicts: {failure.Conflicts?.Count ?? 0}");
        return Task.CompletedTask;
    }
);

DocsCheck.Require(result.Stage == SparseEntityPipelineStage.Saved, "pipeline persists and reconciles");
DocsCheck.Require(result.ReadBack?.Notes == "Updated", "result contains the authoritative DTO");
```
<!-- /sample -->

The mapper must provide a complete `Projection` for the current and authoritative DTO reads. Write-only mappings must add read registrations or use the lower-level `Apply` flow. The filter is an entity predicate and must select exactly one row. `beforeSave` can validate the tracked entity or set an EF original concurrency token. The method calls `SaveChangesAsync` on the whole context, so use a short-lived context without unrelated tracked changes.

`RebaseConflict` means the incoming change set conflicted with the current DTO; the method returns without applying or saving. `onConflict` receives the `RebaseResult` for notification. `ReconcileFailed` means the database save and readback completed, but the optional session could not adopt the persisted DTO; `onReconcileFailed` receives the authoritative DTO and conflicts. Both callbacks are notifications; resolve the issue by fetching current state and making another request.

Database errors, cancellation, and exceptions thrown by callbacks propagate. In particular, `DbUpdateConcurrencyException` is not sent to `onConflict`; catch it around `PipelineExecuteAsync` and handle it as an EF concurrency failure. If an exception occurs during save, readback, or session reconciliation, inspect persisted state before retrying. The method does not retry or roll back changes tracked by the context.

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
4. Catch `DbUpdateConcurrencyException` around `PipelineExecuteAsync` and
   answer 412 with refresh guidance. Keep semantic rebase conflicts as 409. Never resubmit
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
