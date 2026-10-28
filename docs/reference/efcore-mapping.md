# EF Core Mapping Reference

Technical reference for `SparseTypes.EntityFrameworkCore`, covering mapper configuration APIs, plan lifecycle, metadata handling, and execution constraints.

Package and namespace: `SparseTypes.EntityFrameworkCore`. For the application-facing API index, see [API reference](README.md).

## Configuration APIs

The mapper builder (`Map`) is available inside the synchronous `Configure` method of a `[SparseFragmentMapper<TEntity, TDto>]` class.

<!-- illustrative: configuration builder API overview -->
```csharp
[SparseFragmentMapper<OrderEntity, OrderDto>]
public sealed partial class OrderMapper
{
    private void Configure()
    {
        // Property mappings
        Map.Member(dto => dto.Name, entity => entity.CustomerName);
        Map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        Map.Member(dto => dto.Code, entity => entity.Code, code => code?.ToUpperInvariant());

        // Custom read projection for non-invertible writes
        Map.Read(dto => dto.Code, entity => entity.Code);

        // Custom write handler
        Map.Write(dto => dto.AuditNote, (entity, after) => entity.AuditTrail.Add(after.Value!));

        // Member suppression
        Map.Ignore(dto => dto.DisplayTotal);

        // Missing value policies
        Map.OnMissing(dto => dto.Notes, SparseEntityMissingPolicy.SetNull);
        Map.MissingDefault(SparseEntityMissingPolicy.Ignore);

        // Protected member opt-in
        Map.AllowWrite(dto => dto.LegacyId);

        // Keyed collection mapping
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child => child.Ignore(line => line.Subtotal));
                lines.Key(dto => dto.Key, entity => ValueTuple.Create(entity.TenantId, entity.LineNo));
                lines.UnassignedKey(0);
                lines.Delete(); // Options: Detach(), SoftDelete(predicate), Remove(callback)
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );

        // Scoped nested object mapping
        Map.Object(
            dto => dto.Address,
            entity => entity.Address,
            child => child.Ignore(a => a.City),
            SparseEntityMissingPolicy.Throw
        );
    }
}
```

### Member mapping methods

| Method | Description |
|---|---|
| `Map.Member(dto, entity)` | Maps a DTO property to an entity property (renamed or flattened). |
| `Map.Member(dto, entity, converter)` | Applies a value converter during mapping. The converter runs during validation. |
| `Map.Member(dto, entity, onMissing)` | Maps a member with an explicit missing policy. |
| `Map.Member(dto, entity, converter, onMissing)` | Combines conversion and a missing policy. |
| `Map.Read(dto, entity)` | Registers an explicit projection for non-invertible conversions. |
| `Map.Write(dto, handler)` | Executes a custom write callback receiving the presence-aware `Optional<T>` after-state. |
| `Map.Ignore(dto)` | Suppresses mapping for a DTO property. |
| `Map.OnMissing(dto, policy)` | Sets the behavior when a DTO property changes to `Missing` (`Throw`, `Ignore`, `SetDefault`, `SetNull`). |
| `Map.AllowWrite(dto)` | Permits writing to protected EF properties (primary keys, generated values, shadow properties). |
| `Map.MissingDefault(policy)` | Sets the default missing policy. `DefaultMissing` exposes it on the builder. |
| `Map.Collection(dto, entity)` | Declares collection mapping using generated/default element configuration. |
| `Map.Collection(dto, entity, configure)` | Configures a keyed collection through its element builder. |
| `Map.Object(dto, entity, configure, objectMissing = Throw)` | Configures a nested object and its missing policy. |

The builder has a parameterless constructor and fluent methods return the same builder. DTO/entity selectors are expression trees; custom converters and writers are delegates. `DeclareCollection` and the two `DeclareCollectionElement` overloads register generated structural metadata and are intended for generator/adaptor integration rather than ordinary Configure methods.

### Keyed collections

| Method | Description |
|---|---|
| `lines.Key(dtoKey, entityKey)` | Defines composite, computed, or custom key projections between DTO and entity. |
| `lines.Element(builder)` | Configures path-scoped element mappings and overrides. |
| `lines.Create(factory)` | Supplies construction of an entity child from a DTO child. |
| `lines.UnassignedKey(sentinel)` | Declares an explicit unassigned key sentinel (e.g., `0`). |
| `lines.Delete()` | Marks removed entities as deleted (`EntityState.Deleted`). |
| `lines.Detach()` | Unlinks removed entities from parent (optional foreign keys only). |
| `lines.SoftDelete(callback)` | Invokes a callback to mark records deleted without removing the row. |
| `lines.SoftDelete(callback, isDeleted)` | Also supplies a predicate for identifying deleted entities in memory. |
| `lines.SoftDeleteFilter(expression)` | Supplies an expression-based deleted-row filter. |
| `lines.Remove(callback)` | Supplies custom removal with DbContext and entity child. |
| `lines.Order(callback)` | Enables explicit collection order synchronization. |
| `lines.Order(expression)` | Selects an integer entity order property. |

`SparseEntityCollectionBuilder<TEntityChild,TDtoChild>` has a parameterless constructor; methods return the builder. Delete, Detach, SoftDelete, and Remove select different removal policies, not additional save operations.

## Mapper execution API

`[SparseFragmentMapper<TEntity,TDto>]` has a parameterless constructor and read-only EntityType and DtoType. Its generated mapper offers `Create(IModel model)` and generated mapping defaults; Configure is synchronous. The runtime alternative implements `IEntityModelMapping<TEntity,TDto>.Configure(SparseEntityMapBuilder<TEntity,TDto> map)` and calls `SparseEntityMapper<TEntity,TDto>.Create(mapping, model)`.

| Member on SparseEntityMapper<TEntity,TDto> | Contract |
| --- | --- |
| `Projection` | `Expression<Func<TEntity,TDto>>` for query projection. |
| `ToDto(TEntity entity)` | Materializes a DTO from a loaded entity. |
| `Apply(DbContext context, TEntity entity, ISparseChangeSet<TDto> changes)` | Preflights and writes accepted transitions to tracked entities. Does not save or rebase. |
| `ApplyWithResult(context, entity, changes)` | Same application with applied-path and created-entity correlation results. |
| `AssignTemporaryKeys(context, result, baseline, authoritative)` | Copies generated key assignments into corresponding DTOs after persistence. |
| `PipelineExecuteAsync<TContext,TChangeSet>(context, changes, filter, session = null, beforeSave = null, onConflict = null, onReconcileFailed = null, cancellationToken = default)` | Rebases, applies, saves, reads back, assigns temporary keys, and optionally reconciles an edit session. Type arguments are inferred. |

`ISparseEntityMapper<TEntity,TDto>` is the service contract for Apply, ApplyWithResult, AssignTemporaryKeys, `PipelineExecuteAsync`, and `ToDto(DbContext context, TEntity entity)`. Its ToDto signature differs from the concrete mapper's instance method.

`query.IncludeFor(mapper, changes)` has ISparseChangeSet and typed ISparseChangeSet<TDto> overloads and returns IQueryable<TEntity>.

`services.AddSparseEntityMappers(params Type[] mappingTypes)` registers mapping types. `serviceProvider.ValidateSparseEntityMappers<TContext>()` validates registered plans for a DbContext. GeneratedDefaultsRegistry in the Mapping namespace is the generated registration hook, not an application mapping builder.

## Application results and errors

`SparseEntityApplyResult` exposes AppliedPaths, CreatedByPath, and HasCreatedEntities. Lookup methods are `GetCreated(Guid temporaryKey)`, `GetCreated(string collectionPath)`, `GetCreated<TEntityChild>(string collectionPath)`, `GetCreated<TEntityChild,TDto>(collectionSelector)`, and `TryGetCreated<TEntityChild>` with either temporary key alone or collection path plus temporary key. Path-scoped lookup avoids ambiguity across different collections.

Each `SparseEntityAppliedPath` records DtoPath, Operation, EntityType, EntityMember, EntityKey, and optional RemoveKind. Its constructor takes those values, with entity details optional. SparseEntityApplyOperationKind contains Set, SetNull, MissingIgnored, Created, Removed, Reordered, and CustomWrite. SparseEntityRemoveKind contains Delete, Detach, SoftDelete, and Custom.

`SparseEntityMappingException` exposes Failure, DtoPath, Mapper, and Errors. Constructors take `(failure, message)` or `(failure, dtoPath, message, entityTarget = null, suggestedAction = null)`. Error entries expose Failure, DtoPath, Message, EntityTarget, and SuggestedAction, with a matching detailed constructor.

SparseEntityMappingFailure values are Unknown, Duplicate, Unmapped, Ambiguous, Protected, CollectionNotSupported, MissingNotHandled, NullNotAllowed, MetadataMismatch, NestedNotCreatable, CollectionNotLoaded, CollectionKeyNotFound, and CollectionKeyConflict.

## Mapper-managed persistence

`SparseEntityMapper<TEntity,TDto>.PipelineExecuteAsync` and the matching `ISparseEntityMapper<TEntity,TDto>` service method perform the standard persistence sequence:

1. Project the current DTO using the supplied entity filter and rebase the submitted generated change set.
2. If rebase has semantic conflicts, invoke `onConflict` once when provided and return `RebaseConflict` without loading a tracked entity or saving.
3. Load the tracked entity with the navigations selected by `IncludeFor`, apply the rebased change set, and invoke `beforeSave` once when provided.
4. Call `DbContext.SaveChangesAsync(cancellationToken)` once, project the authoritative DTO, assign temporary keys, and reconcile the optional session.
5. If session reconciliation returns false, invoke `onReconcileFailed` once when provided and return `ReconcileFailed` with persisted DTO and conflicts.

The `filter` is an `Expression<Func<TEntity,bool>>`; `SingleAsync` enforces that it selects one entity. The filter is used for both current and authoritative projections, so it must remain stable across the save. Query, apply, persistence, readback, and callback exceptions propagate. `DbUpdateConcurrencyException` is not a rebase conflict and does not invoke `onConflict`. Callbacks are notifications and do not resume or resolve a failed operation.

`SparseEntityPipelineResult<TDto,TChangeSet>` exposes:

| Member | Meaning |
|---|---|
| `Stage` | `Saved`, `RebaseConflict`, or `ReconcileFailed`. |
| `Current` | DTO projected before rebasing. |
| `Submitted` | Original submitted change set. |
| `Rebase` | Typed rebase result; its `Rebased` set is the one applied on success. |
| `ApplyResult` | Apply and temporary-key correlation details after apply; null on rebase conflict. |
| `ReadBack` | Authoritative DTO after persistence; null on rebase conflict. |
| `ReconcileConflicts` | Conflicts returned by failed session reconciliation, when available. |

`SparseEntityBeforeSaveContext<TContext,TEntity,TDto,TChangeSet>` supplies the concrete context type, tracked entity, current DTO, original submitted set, rebased set, and apply result. Use this callback to validate or set original concurrency-token values.

The method saves the entire DbContext. Use a short-lived context without unrelated pending changes. It does not begin a transaction, retry, or roll back tracked state. Cancellation and all exceptions propagate; after a save-stage or later failure, inspect persisted state before retrying. If the application owns an outer transaction, a successful result occurs before that transaction is committed, so session reconciliation does not prove the outer commit succeeded.

## Execution lifecycle

1. **Plan construction**: `OrderMapper.Create(db.Model)` compiles source-generated conventional defaults, executes `Configure()`, and validates the plan against `IModel`.
2. **Navigation loading**: `query.IncludeFor(mapper, changes)` inspects the accepted `ChangeSet` and includes only navigations required by changed paths. Scalar keyed edit filters share a budget of 1,024 key entries across one query, including repeated requests for a navigation. Once a request exceeds the remaining budget, that navigation uses an unfiltered Include. Large edit sets remain supported, but may load the entire parent collection. This bounds generated key comparisons; applications still control request size and the number of children stored under a parent.
3. **Preflight validation**: `Apply` or `ApplyWithResult` validates all operations before modifying any entity state. A failure leaves `ChangeTracker` untouched. Missing nested objects may require constructing detached prospective instances during validation. Constructor invocation failures are collected as `NestedNotCreatable` errors at the affected DTO path, so independent member errors are still reported. Constructors, getters, and configured converters can execute application code during preflight; their external side effects are not rolled back.
4. **Direct mutation**: Only modified properties execute setters. Unchanged EF properties remain untouched.
5. **Temporary key correlation**: `ApplyWithResult` correlates client-generated `[SparseTemporaryKey]` values to new entities. Call `mapper.AssignTemporaryKeys(db, result, baseline, authoritative)` after `SaveChangesAsync()`. The mapper validates correlation across the complete DTO tree before invoking temporary-key setters. A deterministic mapping failure leaves all DTO keys unchanged. If a setter throws during the final write, earlier setter effects may remain; setter execution is not transactional.

Soft-delete ordering places active rows in the submitted order, then newly removed rows in removal-plan order, then previously deleted rows in the original tracked navigation order. Retention uses entity reference identity, so custom entity equality cannot collapse distinct tracked instances. Database read order still requires an explicit ordering configuration.

## Missing versus null semantics

* `Missing`: Never converted to null automatically. The default policy throws `MissingNotHandled`. Configure `OnMissing` or `MissingDefault` to change this to `Ignore`, `SetDefault`, or `SetNull`.
* `Present(null)`: Explicitly writes `null` to the entity property. Throws `NullNotAllowed` if the entity property is non-nullable.

## EF metadata integration

Mappings run against EF property entries:
* Identity, generated, concurrency-token, and shadow properties are protected by default.
* Unmapped changed paths throw `SparseEntityMappingException` with the mapper type, target path, and recommended fix.
* Compile-time diagnostics: `SPF033` flags incompatible default mappings; `SPF034` flags invalid mapper declarations; `SPF035` flags unsupported member shapes.

## Supported / unsupported matrix

| Feature | Support |
|---|---|
| Scalar edits, renamed/flattened properties, conversions | Supported |
| Explicit null vs changed-to-Missing policies | Supported |
| Nested models via child change sets | Supported |
| Owned navigations, shadow properties (with `AllowWrite`) | Supported |
| Keyed add/edit/remove with Delete/Detach/SoftDelete | Supported |
| Composite and computed keys via `Key()` | Supported |
| Path-scoped `[SparseTemporaryKey]` correlation | Supported |
| Entity-to-DTO `Projection` and `ToDto` | Supported |
| Query navigation pruning via `IncludeFor` | Supported |
| Primitive collections (whole value replacement) | Supported |
| Unkeyed collections, skip navigations (many-to-many) | Unsupported (fail explicitly) |
| Filtered includes for collections with additions/removals | Unsupported (unfiltered required) |
| Mapper-managed rebase, `SaveChanges`, temporary-key assignment, optional session reconciliation | Supported by `PipelineExecuteAsync` |
| Custom transaction, authorization, concurrency-token policy, retry | Application-owned |
| Optimistic concurrency tokens | Application-owned (`IsConcurrencyToken` plus a per-write advance covering child writes) |
