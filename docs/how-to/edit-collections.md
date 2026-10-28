# Edit keyed collections

This guide shows how to add, rename, remove, reorder, bind, and save rows in a keyed collection during an edit session. Use `session.EditCollection<TElement, TKey>(...)` when interactive UI code needs batched change notifications and structured error codes for duplicate or missing keys. Mutate a plain `List<T>` directly and diff with `before.CreateChangeSet(after)` when running batch transformations outside an edit session.

## Choose an editing style

Models declare element identity with `[SparseKey]` on a scalar property such as `string Id`. Scalar lists such as `List<int>` are atomic whole values; only structural lists with `[SparseKey]` track per-element additions, removals, edits, and order changes. For composite keys and key equality rules, see [Keyed collections](../explanation/keyed-collections.md).

If you do not need live session notifications or `Try*` validation, mutate the `List<T>` directly (`Add`, `RemoveAt`, `Insert`, or index swaps) and call `before.CreateChangeSet(after)`. Note that duplicate keys in `before` or `after` throw `InvalidOperationException` when diffing.

If you are working inside an edit session, call `session.EditCollection<TElement, TKey>(Order.SparsePath.Items)` to obtain a keyed editor. `session.Observable.Items` also exposes an `IList` view (`Add`, `RemoveAt`, `Insert`, `Count`, `IndexOf`), but prefer the keyed editor for keyed collections because its `Try*` methods return `false` with a `SparseCollectionEditError` (`DuplicateKey`, `UnknownKey`) instead of letting invalid state reach the diff step. For the complete editor API, see [Collection editors](../reference/collection-editors.md).

<!-- illustrative: shows obtaining a keyed collection editor from an edit session -->
```csharp
var session = order.CreateEditSession();
var items = session.EditCollection<OrderItem, string>(Order.SparsePath.Items);

if (items.ContainsKey("line-1"))
{
    var index = items.IndexOfKey("line-1");
}
```

## Add rows

To append or insert a row with a known key, call `TryAdd` or `TryInsert` on the keyed editor. If the collection already contains an element with the same key, the editor rejects the operation, leaves the collection unchanged, and outputs `SparseCollectionEditError.DuplicateKey`.

<!-- illustrative: shows adding and inserting rows with duplicate-key handling -->
```csharp
var newItem = new OrderItem { Id = "line-2", Title = "Cable" };

if (!items.TryAdd(newItem, out var error))
{
    // Handle SparseCollectionEditError.DuplicateKey or NotMutable
}

items.TryInsert(0, new OrderItem { Id = "line-0", Title = "Adapter" }, out error);
```

To inspect added rows on a captured `ChangeSet`, read `changes.Items.Added` or check `changes.Items.GetChange(key).IsAdded`.

## Rename a row key safely

Because `[SparseKey]` defines element identity, changing an element's key property from `"line-1"` to `"line-9"` is recorded as removing `"line-1"` and adding `"line-9"`.

If users can edit a business code in the UI, keep `[SparseKey]` on an immutable surrogate identifier (such as a database ID or GUID) and put the editable code on an ordinary property. If you must replace a row's key in place, call `TryReplaceByKey` or `TryReplace` so the editor validates that the new key does not collide with an existing row:

<!-- illustrative: shows replacing a row with a new key while checking for collisions -->
```csharp
if (items.TryGetByKey("line-1", out var current, out _))
{
    var renamed = new OrderItem { Id = "line-9", Title = current!.Title };
    if (!items.TryReplaceByKey("line-1", renamed, out var error))
    {
        // error is SparseCollectionEditError.DuplicateKey if "line-9" already exists
    }
}
```

## Remove rows

If you have the element's key, call `TryRemoveByKey(key, out error)`. If you have a list position, call `TryRemoveAt(index, out error)`. If the key is not present, `TryRemoveByKey` returns `false` with `SparseCollectionEditError.UnknownKey`.

<!-- illustrative: shows removing a row by key and inspecting the resulting ChangeSet -->
```csharp
if (items.TryRemoveByKey("line-1", out var error))
{
    var changes = session.CreateChangeSet();
    var removedChange = changes.Items.GetChange("line-1");
    // removedChange.IsRemoved == true
    // changes.Items.Removed contains the baseline fragment for "line-1"
}
```

## Reorder rows

To move an item to a new zero-based index, call `TryMoveByKey(key, targetIndex, out error)` or `TryMove(oldIndex, newIndex, out error)`. Reordering preserves element identity and field edits while recording the new key sequence.

<!-- illustrative: shows moving a row by key and checking order changes -->
```csharp
if (items.TryMoveByKey("line-2", 0, out var error))
{
    var changes = session.CreateChangeSet();
    // changes.Items.OrderChanged == true
    // changes.Items.AfterOrder reflects the updated key sequence
    // changes.Items.GetChange("line-2").IsReordered == true
}
```

If you sort a collection only for display, sort a query projection (such as `.OrderBy(...)`) rather than mutating the session list, so view sorting does not set `OrderChanged` to `true`.

## Bind a row field in UI

Address an individual row field with a keyed path such as `Order.SparsePath.Items.Key(id).Title`. Keyed paths follow the element by key even if earlier rows are inserted, removed, or reordered while the user is editing.

<!-- illustrative: shows addressing and observing a row property by key path -->
```csharp
var titlePath = Order.SparsePath.Items.Key("line-2").Title;
using var watcher = session.ObserveField(titlePath, () => RefreshRow("line-2"));

if (items.TryGetByKey("line-2", out var row, out _))
{
    row!.Title = "Braided cable";
}

var changes = session.CreateChangeSet();
// changes.Items.GetChange("line-2").IsEdited == true
// changes.Items.Edited["line-2"].Title.After.Value == "Braided cable"
```

When you mutate row properties through `session.Observable.Items` (or inside `session.BatchEdit`), the session notifies field watchers registered for that key path. For general session observation and lifecycle methods, see [Work with an edit session](edit-session.md).

## Save pending additions

When the database assigns primary keys on insert, declare `[SparseKey(Unassigned = 0)]` (or a nullable key) alongside a `[SparseTemporaryKey] public Guid? TemporaryId { get; set; }` property on the item model. Assign `TemporaryId = Guid.NewGuid()` when creating a pending row on the client.

While the permanent key is unassigned, look up the row with `items.TryGetByTemporaryKey(tempId, out var row, out error)` and bind fields with `Order.SparsePath.Items.TemporaryKey(tempId).Title`.

<!-- illustrative: shows adding a pending row with a temporary key and reconciling after save -->
```csharp
var tempId = Guid.NewGuid();
items.TryAdd(new OrderItem { Id = 0, TemporaryId = tempId, Title = "Draft" }, out _);

var submitted = session.CreateChangeSet();
var persisted = await SaveOrderAsync(submitted.ToPayload());

if (!session.TryReconcile(submitted, persisted, out var reconcileError))
{
    throw new InvalidOperationException(reconcileError);
}
```

When the server responds with `persisted` (echoing `TemporaryId` on each newly inserted row alongside its assigned `Id`), call `session.TryReconcile(submitted, persisted, out error)` to write the assigned keys into the live model while keeping in-flight local edits. If the server also normalizes property values during save, call `session.TryReconcileAndApply(submitted, persisted, out conflicts)` instead. When a single save spans multiple sessions, see [Coordinate multi-session submissions](edit-session-groups.md).
