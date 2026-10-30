# UI Framework Integration

SparseTypes derives semantic changes by comparing a retained baseline with the current model. UI dirty flags and change notifications drive data binding, validation, and control state, while the session's `ChangeSet` compares the baseline snapshot against the live model values.

This page explains how the framework-neutral `EditSession` integrates with UI frameworks: first the neutral observable and history model, then the `SparseTypes.Blazor` adapter, and finally desktop and mobile MVVM frameworks (WPF, WinForms, .NET MAUI, WinUI, and Avalonia). For procedural recipes on saving, acknowledging, reloading, forking, and opening child sessions, see [Work with an edit session](../how-to/edit-session.md). For generated type placement, notification contracts, and state query APIs, see [Edit session state and observers](../reference/edit-session-state.md).

<!-- sample: ui-session-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class UiOrder
{
    public string Number { get; set; } = string.Empty;

    public List<UiOrderItem> Items { get; set; } = new();
}

[SparseModel]
public partial class UiOrderItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
```
<!-- /sample -->

## Framework-neutral edit sessions

Reference-type models create an edit session through `CreateEditSession()` without depending on any UI package. The session retains an isolated baseline snapshot (`Fragment.From`), holds a reference to the caller's live model instance, and computes `ChangeSet` transitions on demand.

Bind editable controls to `session.Observable` and read display state from `session.Current`. Assigning a property on `session.Observable` mutates the underlying model in place and raises `INotifyPropertyChanged` notifications. Reading `session.Current` exposes a read-only view over that same live model instance without setters, so display logic cannot mutate state accidentally.

<!-- illustrative: excerpt; continues the UiOrder flow in prose and does not compile as written -->
```csharp
var line = new UiOrder { Number = "ORD-1" };
var editSession = line.CreateEditSession();

editSession.Observable.Number = "ORD-2";
string shown = editSession.Current.Number;
// shown == "ORD-2"
// line.Number == "ORD-2"

editSession.BatchEdit(() =>
{
    editSession.Observable.Number = "ORD-3";
});

editSession.RevertChanges();
// line.Number == "ORD-1"
// editSession.HasChanges == false
```

Because `RevertChanges()`, `Reload()`, and in-place apply write directly into the existing model instance rather than replacing the root object reference, UI bindings that hold a reference to the model or `session.Observable` stay connected.

### Change cache and notification boundaries

`HasChanges`, `CreateChangeSet()`, and `CreatePatch()` compare the retained baseline against the live model's current state without advancing the baseline until you acknowledge a save with `AcceptChanges` or `TryReconcile`. Editing a field and restoring its original value leaves `HasChanges == false`, even if a UI control marks the input as touched. While all mutations go through `session.Observable`, the session caches `HasChanges` and recomputes only after an observable write. Exposing raw mutable references (such as reading the raw model via `ISparseEditSession<TModel>` or reading unproxied mutable objects through descriptors) disables that cache so subsequent in-place writes are still detected.

Use `session.BatchEdit(() => { ... })` when a single user action modifies several properties. The outermost batch coalesces `PropertyChanged`, `TransitionObserved`, and the optional `onChanged` callback into a single notification at the end of the batch. `BatchEdit` is a notification boundary only; it does not roll back writes if the callback throws.

### Session-owned Undo and Redo history

Undo and Redo history is part of the framework-neutral session and works in any UI framework or console app. The session retains no history entries until you call `EnableHistory()`, and accessing `session.History` before `EnableHistory()` or after `DisableHistory()` throws `InvalidOperationException`:

<!-- sample: ui-history -->
```csharp
var historyOrder = new UiOrder { Number = "ORD-1" };
var historySession = historyOrder.CreateEditSession();

var history = historySession.EnableHistory(new EditHistoryOptions { MaxEntries = 20 });

historySession.Observable.Number = "ORD-2";
// history.CanUndo == true

history.Undo();
// historyOrder.Number == "ORD-1"

history.Redo();
// historyOrder.Number == "ORD-2"

historySession.DisableHistory();
// historySession.History throws InvalidOperationException when disabled
```
<!-- /sample -->

History boundaries are separate from notification boundaries. Use `history.Group(() => { ... })` or `history.BeginGroup()` to bundle multiple observable edits (such as dragging a slider or filling an address block) into a single Undo step, whereas `BatchEdit` only coalesces change notifications. Operations that advance or reset the session baseline (`AcceptChanges()`, `RevertChanges()`, `Reload()`, and `TryReconcile()`) automatically clear the Undo and Redo stacks when they succeed. See [Edit session state and observers](../reference/edit-session-state.md#edit-history-contract) for the full `History` member table.

### Navigating typed paths and editing collections

Every generated edit session exposes `session.Paths`, which provides the same compile-time path builder as `Model.SparsePath`:

<!-- sample: ui-session-paths -->
```csharp
var orderModel = new UiOrder { Number = "ORD-1" };
var orderSession = orderModel.CreateEditSession();

SparsePath<UiOrder, string> numberPath = orderSession.Paths.Number;
// numberPath.ToString() == "Number"

var widgetModel = new UiWidget
{
    Title = "w",
    Items = [new UiWidgetItem { Id = "a", Name = "first" }],
};
var widgetSession = widgetModel.CreateEditSession();
SparsePath<UiWidget, string> itemNamePath = widgetSession.Paths.Items.Key("a").Name;
// itemNamePath.ToString() == "Items[\"a\"].Name"

var itemState = widgetSession.FieldState(widgetSession.Paths.Items.Key("a").Name);
// itemState.HasChanges == false
```
<!-- /sample -->

To add, move, replace, or remove items in a list member with automatic batching and key lookup, open a collection facade with `session.EditCollection<TElement, TKey>(path)`:

<!-- sample: ui-collection-consumer -->
```csharp
var shelfModel = new UiOrder
{
    Items =
    [
        new UiOrderItem { Id = "a", Name = "Alpha" },
        new UiOrderItem { Id = "b", Name = "Beta" },
    ],
};
var shelfSession = shelfModel.CreateEditSession();
var shelf = shelfSession.EditCollection<UiOrderItem, string>(UiOrder.SparsePath.Items);
var shelfTransitions = 0;
shelfSession.TransitionObserved += _ => shelfTransitions++;

shelf.TryAdd(new UiOrderItem { Id = "c", Name = "Gamma" }, out _);
// shelfTransitions == 1
shelf.TryMoveByKey("a", 2, out _);
// shelfModel.Items ids == ["b", "c", "a"]
shelf.TryRemoveByKey("b", out _);
// shelfSession.CreateChangeSet().Items.AfterOrder == ["c", "a"]
```
<!-- /sample -->

Each facade operation resolves the live collection through the session descriptors, runs inside a single `BatchEdit`, and returns a `SparseCollectionEditError` on failure without mutating the collection or raising notifications.

### Inspection and cross-framework validation

For generic UI builders, logs, and diagnostics, the session provides two reflection-free inspection seams:

* `session.Descriptors` exposes per-member metadata, live get/set accessors, and source property attributes (see [Descriptors reference](../reference/descriptors.md)).
* `ChangeSet.EnumerateChanges()` flattens a transition into `SparseChangeRecord` rows (`SparsePath`, before/after values, and `SparseChangeKind`), and `session.EnumerateChangedPaths()` lists modified paths.

For cross-framework validation outside Blazor, `SparseValidationStore<TModel>` implements `INotifyDataErrorInfo` and records errors by typed `SparsePath` and origin, allowing server errors, rebase conflicts (`AddConflicts`), and local validation rules to coexist independently.

## Blazor integration

The `SparseTypes.Blazor` package targets `net10.0` and connects the framework-neutral edit session to Blazor's `EditContext`, `EditForm`, and `ValidationMessageStore`:

<!-- sample: ui-session -->
```csharp
var uiOrder = new UiOrder { Number = "ORD-1" };
var uiSession = uiOrder.CreateEditSession();

uiSession.Observable.Number = "ORD-2";
// uiSession.HasChanges == true

var uiChanges = uiSession.CreateChangeSet();
// uiChanges.IsEmpty == false

uiSession.AcceptChanges();
// uiSession.HasChanges == false
```
<!-- /sample -->

Call `uiSession.CreateEditContext()` to create an `EditContext` whose `Model` is the original `UiOrder` instance:

<!-- illustrative: excerpt; continues the ui-session flow in prose and does not compile as written -->
```csharp
var editContext = uiSession.CreateEditContext();
// After persisting the current model:
uiSession.AcceptChanges(editContext);
```

```razor
<EditForm EditContext="@editContext">...</EditForm>
```

`CreateEditContext()` binds the underlying model `T` rather than `session.Observable`. This keeps `DataAnnotations` validators and `FieldIdentifier` metadata working against `T`, while input writes go through `session.Observable` (or `session.Bind(...)`) so the session tracks changes and raises notifications.

### Blazor extension methods

| Member | Purpose |
| --- | --- |
| `session.CreateEditContext()` | Creates a Blazor `EditContext` bound to the session's live model |
| `session.AcceptChanges(editContext)` | Advances the baseline to the current model and clears `editContext` modified flags |
| `session.AcceptChanges(editContext, changes)` | Advances the baseline by `changes` and clears `editContext` modified flags when no later edits remain |
| `session.CreateValidationStore(editContext)` | Creates a `ValidationMessageStore` bound to `editContext` |
| `session.Field(path)` | Resolves a `FieldIdentifier` from a `SparsePath` or generated typed path |
| `session.AddValidationError(store, fieldOrPath, message)` | Adds a validation message to `store` for a field or `SparsePath` |
| `session.AddConflicts(conflictStore, editContext, conflicts, options)` | Surfaces structured `SparseConflict` entries as validation errors |
| `session.ClearConflicts(conflictStore, editContext)` | Clears the conflict message store without disturbing other validation stores |
| `freshSession.ReplaceEditContext(staleContext)` | Creates a new `EditContext` bound to a fresh session's model after detaching old handlers |

`session.Field(path)` resolves scalar properties, nested properties, dictionary entries (`Contacts["billing"].Name`), positional list items (`Lines[1].Quantity`), and keyed list items (`Lines["b"].Quantity` or `OrderDto.SparsePath.Lines.Key("b").Price`). Keyed paths continue to resolve to the matching item even after the list is reordered.

### Session scope and headless components

`SparseSessionScope` is a headless Blazor component that cascades the active session to child components and calls `StateHasChanged` automatically when the session raises `INotifyPropertyChanged`. Inside a scope:

* `SparseField<TModel, TValue>` binds a typed path (`session.Paths.Number` or `session.Paths.Items.Key("a").Name`) and exposes the current value, a setter callback, an expression (for member-only paths), and point-in-time `FieldState`.
* `SparseCollection<TModel, TElement>` renders keyed collection rows with stable UI keys, per-item state (`IsAdded`, `IsEdited`, `IsReordered`), and row move/remove helpers backed by `EditCollection`.
* `SparseChildEdit` and `SparseChildEditDefault` manage independent child sessions in `Draft` mode (held until explicitly accepted) or `PassThrough` mode (accepted into the parent on each edit).

See [Blazor edit form](../how-to/blazor-edit-form.md) for the complete form save and conflict-handling workflow, and [Blazor headless components](../how-to/blazor-headless-components.md) for custom input wiring.

## WPF, WinForms, .NET MAUI, WinUI, and Avalonia

Desktop and mobile MVVM frameworks bind directly to `session.Observable`. The generated `Observable` proxy writes through to the underlying model and raises `INotifyPropertyChanged` on the mutating thread.

* **Nested models**: Promoted nested models are exposed as child `Observable` proxies whose property changes bubble up to the parent proxy and the session's `onChanged` callback.
* **Collections**: Mutable list (`List<T>`, `IList<T>`, `Collection<T>`, `ObservableCollection<T>`) and dictionary (`Dictionary<TKey, TValue>`, `IDictionary<TKey, TValue>`) properties surface as notifying collection views implementing `INotifyCollectionChanged` and `INotifyPropertyChanged` (`Count` and `Item[]`).
* **Adding and replacing items**: Add model instances through `AddModel` / `InsertModel` on the collection view, and replace an entire collection instance with the generated `Replace<PropertyName>(newCollection)` method on the observable proxy.

Create a neutral edit session and assign `session.Observable` to your view's `DataContext` or binding source:

<!-- sample: ui-wpf-session -->
```csharp
var stockModel = new UiOrder { Number = "ORD-1" };
var saveEnabled = false;
var stockSession = stockModel.CreateEditSession(onChanged: () =>
{
    saveEnabled = true;
});
var stockView = stockSession.Observable;

stockView.Number = "ORD-2";
stockView.Items.AddModel(new UiOrderItem { Id = "line-1", Name = "First item" });
// saveEnabled == true
// stockModel.Number == "ORD-2"
// stockSession.Current.Number == "ORD-2"

var stockChanges = stockSession.CreateChangeSet();
// stockChanges.Number.After.Value == "ORD-2"
```
<!-- /sample -->

### WPF binding example

```xml
<TextBox Text="{Binding Number, UpdateSourceTrigger=PropertyChanged}" />
<ItemsControl ItemsSource="{Binding Items}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <TextBlock Text="{Binding Name}" />
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

<!-- illustrative: excerpt; 'model' comes from the host application and the fence does not compile as written -->
```csharp
var session = model.CreateEditSession(
    onChanged: () => SaveCommand.NotifyCanExecuteChanged());
DataContext = session.Observable;
```
