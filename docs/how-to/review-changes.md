# Review changes before save

This guide shows how to gate form submission on dirty state, list pending transitions, project changes into a preview table, highlight edited fields and rows, and separate local, server, and conflict validation errors. For session lifecycles and baseline acknowledgement, see [Work with an edit session](edit-session.md).

## Gate submit on dirty state

Read `session.HasChanges` to enable or disable your save button. `HasChanges` compares the live model against the retained baseline, so editing a field and restoring its original value clears the dirty state.

When the user previews or submits the draft, call `session.CreateChangeSet()`. Capturing a `ChangeSet` does not modify the baseline, so you can call it repeatedly to refresh a review dialog before sending the request.

<!-- illustrative: gating save and inspecting a captured ChangeSet -->
```csharp
if (!session.HasChanges)
{
    return;
}

var changes = session.CreateChangeSet();
if (changes.Title.IsChanged)
{
    var previous = changes.Title.Before.Value;
    var current = changes.Title.After.Value;
}
```

Use `changes.IsEmpty` or `changes.IsChanged` to check the captured transition as a whole, and read `changes.Field.IsChanged`, `changes.Field.Before`, and `changes.Field.After` for individual members.

## List what changed

When your confirmation prompt or audit log needs a flat list of modifications instead of member-by-member checks, call `changes.EnumerateChanges()`. Each `SparseChangeRecord` exposes `Path`, `PathText`, `Before`, `After`, and `Kind` (`Changed`, `Added`, `Removed`, or `Order`).

If you only need the modified paths, call `changes.EnumerateChangedPaths()`. To look up a single path in a captured transition, call `changes.Find(path)`:

<!-- illustrative: enumerating flattened change records and looking up a path -->
```csharp
var changes = session.CreateChangeSet();
foreach (var record in changes.EnumerateChanges())
{
    Console.WriteLine($"{record.Kind}: {record.PathText}");
}

var titleRecord = changes.Find(OrderModel.SparsePath.Title);
```

When code operates on the generic `ISparseChangeSet` contract across model types, call `EnumerateChangeRecords()` to flatten the transition, or query specific members by CLR name with `TryGetValueChange`, `TryGetNestedChange`, `TryGetKeyedChange`, and `TryGetDictionaryChange`.

## Show a preview table

To bind a confirmation table such as the playground's `ChangePreviewTable`, project the `ChangeSet` with `SparseChangeRowProjection.Project`. Pass `SparseChangeRowOptions` when you want custom labels for operations or values:

<!-- illustrative: projecting a ChangeSet into display rows for a preview table -->
```csharp
var changes = session.CreateChangeSet();
var rows = SparseChangeRowProjection.Project(
    changes,
    new SparseChangeRowOptions
    {
        FormatOperation = kind => kind.ToString().ToUpperInvariant(),
        FormatValue = value => value?.ToString() ?? "(none)",
    }
);
```

Each projected row exposes `Path`, `PathText`, `Kind`, `Operation`, `Before`, `After`, `BeforeText`, and `AfterText`. Map `Operation`, `PathText`, `BeforeText`, and `AfterText` to your table's Operation, Target, Before, and After columns.

## Highlight edited fields

Bind form controls and row badges to paths on `Model.SparsePath` (for example, `OrderModel.SparsePath.Items.Key(id).Title`) and query the session for dirty and collection-item state. For scalar or nested fields, read `IsChanged` from `session.FieldState(path)` or a live `session.ObserveField(path)` watcher. For keyed collections, query `session.CollectionState(path)` and inspect `IsAdded`, `IsReordered`, and `IsRemoved` on each row to toggle badges. See [Edit keyed collections](edit-collections.md) for collection mutation patterns.

<!-- illustrative: reading field and row state for UI badges -->
```csharp
var titlePath = OrderModel.SparsePath.Items.Key(itemId).Title;
var isTitleDirty = session.FieldState(titlePath).IsChanged;

var itemsState = session.CollectionState(OrderModel.SparsePath.Items);
var rowState = itemsState.GetItemState(itemId);
var showAddedBadge = rowState.IsAdded;
var showOrderBadge = rowState.IsReordered;
```

If the user is editing a nested section in an open child session, `session.CreateChangeSet()` includes those pending child edits in the preview, while `session.PreviewChangeSet()` reports only edits made directly on the parent session.

## Keep local and server errors apart

Store field and path errors in `SparseValidationStore<TModel>`, which implements `INotifyDataErrorInfo` for XAML and UI bindings. Tag every error with an origin from `SparseValidationOrigins` (`LocalValidation`, `ServerValidation`, or `RebaseConflict`) so each step in the save lifecycle clears only its own messages.

When a user edits a field, clear and re-evaluate `SparseValidationOrigins.LocalValidation` at that path without wiping server messages on other fields. Before sending a save request or refreshing from the server, call `store.ClearOrigin(SparseValidationOrigins.ServerValidation)` so stale server errors disappear while local validation errors remain in place.

<!-- illustrative: separating local validation, server validation, and rebase conflicts by origin -->
```csharp
var titlePath = OrderModel.SparsePath.Title;
store.ClearErrors(titlePath, SparseValidationOrigins.LocalValidation);
if (string.IsNullOrWhiteSpace(session.Current.Title))
{
    store.SetErrors(titlePath, SparseValidationOrigins.LocalValidation, "Title is required.");
}

store.ClearOrigin(SparseValidationOrigins.ServerValidation);
store.ClearOrigin(SparseValidationOrigins.RebaseConflict);
store.AddConflicts(conflicts);
```

When a save or reload returns rebase conflicts, pass them to `store.AddConflicts(conflicts, options)`, which records them under `SparseValidationOrigins.RebaseConflict` by default. Query `store.HasPathErrors(path)` or `store.GetPathErrors(path)` to highlight a field and its descendants, or `store.GetAllErrors()` to render a summary banner. Use `store.Clear()` only when resetting the entire form.

For handling conflicts after they are surfaced, see [Resolve rebase conflicts](resolve-conflicts.md) and [ChangeSet rebase](../explanation/rebase.md). For the full validation store contract, see [Validation stores](../reference/validation.md).
