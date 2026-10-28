# Resolve rebase conflicts

This guide shows how to handle concurrent edit collisions across server save-or-conflict branches, client session reloads, speculative fork merges, and per-path resolution. For the three-way comparison rules behind these operations, see [ChangeSet rebase](../explanation/rebase.md).

## Reproduce the save-or-conflict branch

A `ChangeSet` carries its own before-state. When a request arrives at the server, load only the current state and rebase the incoming transition against it. For transport and envelope validation details, see [Accept partial updates in an API](partial-updates.md).

If the endpoint needs an updated model to persist or a conflict list to return, call `TryApplyTo`:

<!-- illustrative: server endpoint save-or-conflict branch -->
```csharp
var incoming = request.Changes.ToChangeSet();
var current = await LoadCurrentOrderAsync(request.OrderId);

if (incoming.TryApplyTo(current, out var updated, out var conflicts))
{
    await SaveOrderAsync(updated, loadedVersion);
    return Results.Ok(updated);
}

return Results.Conflict(conflicts);
```

If `TryApplyTo` returns `true`, persist `updated`. If it returns `false`, surface `conflicts` to the caller and write nothing to the datastore.

If you need to inspect or transform the rebased `ChangeSet` before applying it, call `RebaseOnto(current)` instead. It returns a `RebaseResult<Order.ChangeSet>` with `Rebased`, `Conflicts`, and `HasConflicts`. Conflicting members are excluded from `Rebased`. Do not commit `Rebased` when `HasConflicts` is `true` unless your endpoint policy explicitly drops conflicting member edits.

When a save inserts collection rows marked with `[SparseTemporaryKey]`, database-assigned keys are returned in the persisted model after the save. Reconcile those keys on the client with `session.TryReconcile(submitted, persisted, out var error)` instead of rebasing.

## Reload a session onto fresh state

When a fresh server snapshot arrives while a user has unsaved edits in an edit session, call `session.Reload(serverState)` to rebase the pending local changes onto the new state. For the full session lifecycle, see [Work with an edit session](edit-session.md).

<!-- illustrative: client session reload against fresh server snapshot -->
```csharp
var reload = session.Reload(serverState);
if (!reload.HasConflicts)
{
    // Non-overlapping server values are now in session.Current,
    // and serverState is the new baseline.
    return;
}

var priceConflict = reload.Conflicts.Find(Order.SparsePath.Total);
```

If `reload.HasConflicts` is `false`, `Reload` writes the merged values into the live model in place and adopts `serverState` as the session baseline.

If `reload.HasConflicts` is `true`, `Reload` leaves both the live model and the retained baseline untouched. Inspect `reload.Conflicts` to show the collision in the UI, or resolve the conflict paths against `serverState` before applying the result.

## Merge a fork

When a modal dialog or preview pane edits a branched session created with `session.Fork()`, merge those changes back into the parent session with `TryMergeFrom`:

<!-- illustrative: merging a forked draft session into the parent session -->
```csharp
if (parentSession.TryMergeFrom(forkSession, out var merged, out var conflicts))
{
    parentSession = merged!;
}
else
{
    var conflict = conflicts!.Find(Order.SparsePath.ShippingAddress);
}
```

`TryMergeFrom` does not mutate `parentSession` or `forkSession`. If it returns `true`, replace the parent session reference with `merged`. If it returns `false`, both sessions keep their existing state and `conflicts` lists the colliding paths.

## Decide per-path winners

When `RebaseOnto` or `session.Reload` reports conflicts and you want to choose winners per field instead of rejecting the whole change, open a resolution builder with `ChangeSet.BeginResolution`:

<!-- illustrative: per-path conflict resolution using BeginResolution -->
```csharp
var rebase = incoming.RebaseOnto(current);
var resolution = Order.ChangeSet.BeginResolution(rebase, current);

var totalPath = Order.SparsePath.Total;
if (resolution.HasConflict(totalPath))
{
    resolution.UseIncoming(totalPath);
}

resolution.UseCurrent(Order.SparsePath.ShippingAddress);
resolution.SetValue(Order.SparsePath.Notes, "Merged note");
```

Each `SparseConflict` in `rebase.Conflicts` exposes `Path`, `PathText`, `Kind` (`Scalar`, `Nested`, `WholeContribution`, `CollectionAppend`, `CollectionSetUnion`, `CustomStrategy`, `RedactedBefore`, or `InPlaceWriteUnavailable`), and the three endpoints `BaseValue`, `LocalValue`, and `CurrentValue`. Use `conflicts.Find(path)` or `resolution.FindConflict(path)` to inspect a specific member, and `HasConflictsUnder(path)` or `HasConflictsAffecting(path)` to check nested scopes.

Record a decision for every conflict:

* Call `resolution.UseIncoming(path)` to overwrite the current value with the local edit.
* Call `resolution.UseCurrent(path)` to keep the current value and drop the local edit.
* Call `resolution.SetValue(path, value)` to supply a custom typed value.

A `UseIncoming` or `UseCurrent` call on an ancestor path covers all conflicts below that path unless overridden at a more specific path. If a conflict has `Kind == SparseConflictKind.RedactedBefore`, the before-state is withheld, so `resolution` accepts only `UseCurrent(path)` for that path.

After recording decisions, build the resolved `ChangeSet`:

<!-- illustrative: building and applying the resolved ChangeSet -->
```csharp
if (resolution.TryBuild(out var resolved, out var failure))
{
    var updated = resolved!.ToPatch().ApplyTo(current);
}
else
{
    var remaining = failure!.UnresolvedConflicts;
}
```

If `TryBuild` returns `true`, `resolved` is a `ChangeSet` based on `current` that combines the non-conflicting edits from `rebase.Rebased` with your per-path choices. See [Rebase results and conflict resolution](../reference/rebase-api.md) for the complete resolution member list.

## Retry a raced write

Semantic rebase reconciles member edits in memory, while your datastore concurrency token (such as a row version or ETag) guards the commit between loading `current` and writing the updated row. Keep the concurrency token in your request contract outside the `ChangeSet`.

If another writer updates the row after you load `current` but before your conditional write commits, reload the newer row and run `TryApplyTo` again using the original `incoming` `ChangeSet`:

<!-- illustrative: bounded retry loop for a raced conditional write -->
```csharp
for (var attempt = 0; attempt < 3; attempt++)
{
    var (current, version) = await store.LoadCurrentAsync(orderId);
    if (!incoming.TryApplyTo(current, out var merged, out var conflicts))
    {
        return Results.Conflict(conflicts);
    }

    if (await store.TrySaveAsync(orderId, version, merged))
    {
        return Results.Ok(merged);
    }
}

return Results.StatusCode(StatusCodes.Status409Conflict);
```

Always rebase the original `incoming` transition onto the newly loaded state on each attempt. If a concurrent write touched a disjoint member, the retry merges cleanly and commits under the new token. If the concurrent write touched the same member, `TryApplyTo` returns `false` on the retry and exits the loop with structured conflicts.
