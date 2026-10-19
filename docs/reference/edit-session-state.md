# Edit session state and observers

Reference for point-in-time state queries (`FieldState`, `CollectionState`) and live subscriptions (`ObserveField`, `WatchField`) on generated edit sessions.

Package and namespace: `SparseTypes`. See [generated EditSession](generated-edit-session.md) for the full session API and [paths](paths.md) for path construction.

## Overview

| Capability | Scope | Lifetime & Cost |
| --- | --- | --- |
| `session.FieldState(path)` | Exact, ancestor, and descendant status for one path | Point-in-time snapshot. Live query diffs against the baseline on every call. |
| `changeSet.FieldState(path)` | Same path inspection on an immutable change set | Point-in-time snapshot. O(R) scan over flattened change records without re-diffing. |
| `session.CollectionState(path)` | Item-level state for keyed collections or dictionaries | Point-in-time snapshot. Diffs the live model once on creation. |
| `session.ObserveField(path)` | Live change watcher bound to session `PropertyChanged` | Opt-in subscription. Refreshes state and notifies when values change. Requires `Dispose`. |

## Field state queries

`FieldState` inspects semantic changes at a typed or model-agnostic `SparsePath`.

<!-- sample: edit-session-field-state -->
```csharp
var widget = new UiWidget
{
    Title = "original",
    Child = new UiWidgetChild { Name = "child-original" },
};
var session = widget.CreateEditSession();

// Mutate child name
session.Observable.Child!.Name = "child-updated";

// Query field state at exact path
var childNameState = session.FieldState(session.Paths.Child.Name);
// childNameState.IsChanged == true
// childNameState.Kind == SparseFieldChangeKind.Changed
// childNameState.HasAncestorChange == false
// childNameState.HasDescendantChange == false
// childNameState.HasChanges == true
// childNameState.Before.Value == "child-original"
// childNameState.After.Value == "child-updated"

// Query field state at ancestor path (Child)
var childState = session.FieldState(session.Paths.Child);
// childState.IsChanged == false (Child reference itself was not replaced)
// childState.HasDescendantChange == true
// childState.HasChanges == true

// For repeated queries, snapshot a single ChangeSet to avoid re-diffing:
var changeSet = session.CreateChangeSet();
var fromSnapshot = changeSet.FieldState(session.Paths.Child.Name);
// fromSnapshot.IsChanged == true
```
<!-- /sample -->

### Field state properties

`SparseFieldState<T>` provides:

| Property | Type | Description |
| --- | --- | --- |
| `Path` | `SparsePath` | Queried path. |
| `IsChanged` | `bool` | True only when an exact transition occurred at `Path`. |
| `Kind` | `SparseFieldChangeKind` | Transition classification: `Unchanged`, `Changed`, `Added`, `Removed`, or `Reordered`. |
| `HasAncestorChange` | `bool` | True when a strict ancestor changed (e.g. parent object replaced). |
| `HasDescendantChange` | `bool` | True when any child path below this path changed. |
| `HasChanges` | `bool` | Aggregate flag: `IsChanged \|\| HasAncestorChange \|\| HasDescendantChange`. |
| `Before` | `Optional<T>` | Presence-aware value before transition. `Missing` if unchanged. |
| `After` | `Optional<T>` | Presence-aware value after transition. `Missing` if unchanged. |

`Before` and `After` reflect exact transitions only. When `IsChanged` is false, both endpoints are `Optional<T>.Missing`, which indicates the absence of an exact transition rather than a missing member.

## Collection state queries

`CollectionState` inspects member-level keyed collections and dictionaries. Deeper paths below items throw `ArgumentException`.

<!-- sample: edit-session-collection-state -->
```csharp
var widget = new UiWidget
{
    Items =
    [
        new UiWidgetItem { Id = "a", Name = "item-a" },
        new UiWidgetItem { Id = "b", Name = "item-b" },
    ],
};
var session = widget.CreateEditSession();

// Reorder and edit
session.Observable.Items.Move(1, 0);
session.Observable.Items[0].Name = "item-b-edited";

// Query collection state for Items
var collectionState = session.CollectionState(session.Paths.Items);
var itemBState = collectionState.GetItemState("b");
// itemBState.IsEdited == true
// itemBState.IsReordered == true
// itemBState.IsAdded == false
// itemBState.IsRemoved == false

// Unknown key yields empty state
var unknownState = collectionState.GetItemState("nonexistent");
// unknownState.IsEmpty == true
```
<!-- /sample -->

### Collection item lookup

* **Stable keys**: `collectionState.GetItemState(key)` returns item state for matching keyed rows or dictionary keys. Unknown keys return `SparseCollectionItemState.Empty` (`IsEmpty == true`).
* **Temporary identities**: `collectionState.GetTemporaryState(guid)` returns item state for unassigned rows tracked with `[SparseTemporaryKey]`.
* **Reordering**: `IsReordered` reports whether an item's relative position among surviving keys changed. Dictionary items never report reorders.

## Live field observers

`ObserveField` (and its alias `WatchField`) attaches an active watcher to a specific path.

<!-- sample: edit-session-observe-field -->
```csharp
var widget = new UiWidget { Title = "start" };
var session = widget.CreateEditSession();

var changeCount = 0;
using var watcher = session.ObserveField(session.Paths.Title);
watcher.PropertyChanged += (_, _) => changeCount++;

// Observable change notifies watcher synchronously
session.Observable.Title = "next";
// changeCount > 0
// watcher.After.Value == "next"
// watcher.IsChanged == true

var lastCount = changeCount;
// Setting same value raises nothing (effective change suppression)
session.Observable.Title = "next";
// changeCount == lastCount

// Disposing detaches notifications
watcher.Dispose();
session.Observable.Title = "after-dispose";
// changeCount == lastCount
```
<!-- /sample -->

### Observer characteristics

`SparseObservedField<T>` exposes Path, Current (`SparseFieldState<T>`), IsChanged, Kind, HasAncestorChange, HasDescendantChange, HasChanges, Before, After, and the PropertyChanged event. `Refresh()` explicitly re-queries the state; `Dispose()` detaches notifications. Current is the retained state snapshot, not an Optional current-model value.

* **Notification filtering**: The watcher queries the session on invalidation. It raises `PropertyChanged` only when the refreshed state differs from the retained state. Redundant assignments raise no notifications.
* **Synchronous dispatch**: Events fire synchronously on the mutating thread. No UI dispatching is performed.
* **Raw model writes**: Direct assignments to `session.Model` bypass notification pipelines and will not trigger watcher events. Use `session.Observable` for mutations.
* **Disposal**: Observers subscribe to session events. Always call `Dispose()` to release references when the watcher is no longer needed.

## Reusing a state snapshot

Generated sessions that expose `CaptureStateSnapshot()` create a
`SparseChangeSetQueryView` over one captured change set. Reuse that view when
many fields need state from the same render or inspection pass; it flattens
and indexes the transition lazily, then answers path queries from that fixed
snapshot. The view does not refresh after later model edits. Capture a new
snapshot after session notifications or other changes to the draft.

Use the session's `FieldState(path)` for a one-off query that must reflect the
current live model, including mutations that did not raise session
notifications. For repeated queries over a known generation, the captured
view avoids deriving and scanning a new change set for every field. The
Blazor `SparseEditContext` shares such a snapshot across field components in
one refresh generation when the session supports that capability.

### Query view and collection members

`new SparseChangeSetQueryView(ISparseChangeSet changes)` and `SparseChangeSetQueryView.Capture(changes)` create a fixed query view. Members are Changes, Records, ProjectionCount, EnsureIndexed(), FieldState(SparsePath), and typed FieldState<TModel,TValue>(path). ProjectionCount reports flattened projection work; EnsureIndexed eagerly initializes the otherwise lazy index.

`SparseStateQueries` extensions expose CaptureQueryView() and typed/untyped FieldState on ISparseChangeSet. KeyedState(change, path) and DictionaryState(change, path), also available as static SparseChangeSetQueryView methods, project interface-level collection transitions.

`SparseCollectionState` exposes Path, IsChanged, IsEmpty, OrderChanged, ChangedItems, GetItemState(object? key), and GetTemporaryState(Guid temporaryKey). `SparseCollectionItemState` exposes Key, TemporaryKey, BeforeIndex, AfterIndex, Before, After, IsAdded, IsRemoved, IsEdited, IsReordered, IsChanged, and IsEmpty.

For display tables, `SparseChangeRowProjection.Project(changes, options = null)` accepts ISparseChangeSet or IReadOnlyList<SparseChangeRecord>. SparseChangeRow exposes init properties Path, PathText, Kind, Before, After, Operation, BeforeText, and AfterText. SparseChangeRowOptions supplies MissingText, optional FormatValue, FormatOperation, and FormatError delegates, plus GetValueText, GetOperation, and GetErrorText helpers. This is display formatting, not transition comparison.

## Edit history contract

`session.EnableHistory(options)` enables session-owned Undo and Redo and sets `session.History`. Before `EnableHistory` or after `session.DisableHistory()`, `session.History` is `null`.

| Member | Description |
| --- | --- |
| `CanUndo` / `CanRedo` | `true` when at least one entry is available on the Undo or Redo stack |
| `UndoCount` / `RedoCount` | Number of entries currently on the Undo or Redo stack |
| `Undo()` / `Redo()` | Replays the top transition in place; throws `InvalidOperationException` if the stack is empty or if replay conflicts |
| `TryUndo(out conflicts)` / `TryRedo(out conflicts)` | Attempts replay in place; returns `false` with structured `SparseConflict` entries on conflict |
| `BeginGroup()` / `Group(Action)` | Combines multiple observable edits into a single Undo/Redo entry |
| `Clear()` | Empties both Undo and Redo stacks |

`EditHistoryOptions.MaxEntries` defaults to `50` (minimum `1`). When the Undo stack exceeds `MaxEntries`, the oldest entry is dropped. Successful baseline updates (`AcceptChanges`, `RevertChanges`, `Reload`, `TryReconcile`, and `TryReconcileAndApply`) clear both stacks automatically.

## Notification and cache contract

* **`INotifyPropertyChanged`**: Raised on the session (`PropertyName == nameof(HasChanges)`) after observable edits and baseline acknowledgements.
* **`TransitionObserved`**: Raised after each observable edit (or at the outer boundary of `session.BatchEdit`) with the committed `ChangeSet` transition.
* **`onChanged` callback**: Optional delegate passed to `CreateEditSession(onChanged: ...)` invoked synchronously after observable edits.
* **Thread affinity**: All session and collection-view notifications (`INotifyCollectionChanged` and `INotifyPropertyChanged`) fire synchronously on the mutating thread without UI-thread marshaling.
* **Change cache**: `HasChanges` is cached while mutations go exclusively through `session.Observable`. Accessing the raw model via `ISparseEditSession<TModel>` or reading unproxied mutable references through descriptors permanently disables the cache so direct mutations are still detected on subsequent `HasChanges` reads.

## Generated type placement

Companion types are emitted as nested types on the `[SparseModel]` class, while internal operation kernels and builders are emitted into a per-model container under `SparseTypes.Generated`:

| Nested in `Model` | Emitted in `SparseTypes.Generated.<Container>` |
| --- | --- |
| `Model.Fragment` (and `Model.SparsePath`) | `<Container>.FragmentBuilder`, `<Container>.SparsePaths<TRoot>` |
| `Model.Patch`, `Model.ChangeSet`, `Model.ChangePayload` | Wire DTOs (`__Internal_<hash>`) and operation kernels (`FragmentOperations`, `PatchOperations`, `ChangeSetOperations`) |
| `Model.EditSession` (`Model.SparseEditSession` if `EditSession` collides) | `<Container>.DescriptorFactory` |
| `Model.Observable` (`Model.SparseObservable` if `Observable` collides), `Model.ReadOnlyView` | `<Container>FragmentJsonConverter` |

If a model declares a member named `Observable` or `EditSession`, the generator renames the nested companion type to `SparseObservable` or `SparseEditSession`. Member names `Fragment`, `Patch`, `ChangeSet`, `ChangePayload`, or `SparsePath` report diagnostic [SPF009](analyzer.md#spf009-member-conflicts-with-generated-api).
