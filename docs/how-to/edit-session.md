# Work with an edit session

An edit session tracks live mutations on a C# model against a retained baseline snapshot. This guide shows how to start a session, submit a captured `ChangeSet`, choose the right baseline acknowledgement after a server response, reload external server updates, discard edits, branch speculative drafts with `Fork()`, and edit nested models in child sessions. For conceptual boundaries, see [Edit sessions and state boundaries](../explanation/edit-sessions.md).

## Start a draft and edit it

Call `CreateEditSession()` on the model instance that the UI edits. The session retains an isolated snapshot of the initial values as its baseline while `session.Observable` writes directly through to that model instance. Bind UI inputs to `session.Observable` and read display values from `session.Current`.

`HasChanges` compares the live model values against the retained baseline. Changing a property and restoring its original baseline value leaves `HasChanges == false`. When you need to inspect or send the pending changes, call `CreateChangeSet()`. Capturing a `ChangeSet` does not modify the baseline.

To group several observable property writes into a single change notification, wrap them in `session.BatchEdit(() => { ... })`.

## Record Undo and Redo history

Call `session.EnableHistory()` to track observable edits in an Undo/Redo stack (`session.History`). Each property write through `session.Observable` records a reversible step; call `History.Undo()` and `History.Redo()` to walk backward and forward without losing baseline tracking:

<!-- sample: tutorial-history -->
```csharp
var historyModel = new UserSettings { Theme = "light", RefreshInterval = 60 };
var historySession = historyModel.CreateEditSession();
var history = historySession.EnableHistory();

historySession.Observable.Theme = "dark";
historySession.Observable.RefreshInterval = 120;
// history.CanUndo == true

history.Undo();
// historyModel.RefreshInterval == 60 (RefreshInterval edit undone; Theme remains "dark")

history.Redo();
// historyModel.RefreshInterval == 120 (RefreshInterval edit restored)
```
<!-- /sample -->

To collapse multiple observable writes into a single Undo step, wrap them in `session.History.Group(() => { ... })` (see [Edit session state and observers](../reference/edit-session-state.md#edit-history-contract)).

## Submit a snapshot and acknowledge the response

Capture the `ChangeSet` before starting an asynchronous save request, and send `submitted.ToPayload()` over your application's HTTP or message transport. Because `ChangeSet` is immutable, edits made while the request is in flight do not alter `submitted`.

If the transport call fails, leave the session untouched so the user's draft remains dirty and ready to retry. When the server succeeds, choose the acknowledgement method that matches what the server returned and whether the user kept typing during the request:

| Server response and editing policy | Session operation | Live model | Baseline |
| --- | --- | --- | --- |
| Editing is disabled during save, and the server returned the persisted model (with IDs, timestamps, or normalized values) | Start a fresh session: `persisted.CreateEditSession()` | Replaced with `persisted` | `persisted` |
| The server persisted the submitted transition unchanged, and the user kept editing during the request | `session.AcceptChanges(submitted)` | Untouched; later edits stay pending | Advanced by `submitted` |
| The server assigned database keys to new `[SparseTemporaryKey]` rows, and the user kept editing | `session.TryReconcile(submitted, persisted, out error)` | Assigned keys written in place; local property values kept | `persisted` |
| The server normalized property values (or assigned keys), and the user kept editing | `session.TryReconcileAndApply(submitted, persisted, out conflicts)` | Server values adopted; post-submit edits rebased | `persisted` |
| No edits occurred after capture, and the current in-memory model matches what was persisted | `session.AcceptChanges()` | Untouched | Current live model |

### Acknowledge an in-flight save or start a fresh session

When the user continues typing while the request is in flight and the server accepts `submitted` without modifying values, pass `submitted` to `AcceptChanges(submitted)`. It advances the baseline by `submitted` so only the edits made after capture remain dirty. When the server returns an authoritative persisted model and no in-flight edits need to be kept, create a fresh session from the returned model:

<!-- sample: ui-accept-flow -->
```csharp
var orderModel = new UiOrder { Number = "a" };
var orderSession = orderModel.CreateEditSession();

orderSession.Observable.Number = "b";
var submitted = orderSession.CreateChangeSet();

// The server persisted the submitted transition unchanged,
// and the user kept typing while the save was in flight.
orderSession.Observable.Number = "c";
orderSession.AcceptChanges(submitted);
// orderSession.HasChanges == true
// orderSession.CreateChangeSet() carries only Number "b" -> "c"

// When the server returns the authoritative state instead, start over from it.
var persisted = new UiOrder { Number = "b" };
var freshSession = persisted.CreateEditSession();
// freshSession.HasChanges == false
```
<!-- /sample -->

`AcceptChanges()` and `AcceptChanges(submitted)` update only the session's in-memory baseline; neither method performs network or database I/O. If `submitted` does not match the current baseline, `AcceptChanges(submitted)` throws `InvalidOperationException` and leaves the baseline unchanged.

For rows added with `[SparseTemporaryKey]`, use `TryReconcile(submitted, persisted, out error)` to write database-assigned keys into the live model without overwriting other local property values, or `TryReconcileAndApply(submitted, persisted, out conflicts)` to adopt server-normalized property values as well. See [Keyed collections](../explanation/keyed-collections.md#saving-pending-additions-with-reconcile) for the complete temporary-key reconciliation workflow.

## Reload with fresh server state

When a fresh server snapshot arrives outside a save acknowledgement (for example via a refresh button or push notification) while the user has unsaved local edits, call `session.Reload(serverState)`. `Reload` rebases the pending local edits onto `serverState`. When there are no conflicts, it writes the merged values into the live model in place and adopts `serverState` as the new baseline:

<!-- sample: ui-reload -->
```csharp
var reloadModel = new UiOrder { Number = "base" };
var reloadSession = reloadModel.CreateEditSession();
reloadSession.Observable.Number = "local";
var serverState = new UiOrder
{
    Number = "base",
    Items = [new UiOrderItem { Id = "line-1", Name = "First item" }],
};

var reload = reloadSession.Reload(serverState);
// reload.HasConflicts == false
// reloadSession.Current.Number == "local"
// reloadSession.Current.Items.Count == 1
// reloadSession.HasChanges == true
```
<!-- /sample -->

When `serverState` modifies the same property that the user edited locally, `Reload` returns `HasConflicts == true` and leaves both the live model and the retained baseline untouched:

<!-- sample: ui-reload-conflict -->
```csharp
var conflictModel = new UiOrder { Number = "base" };
var conflictSession = conflictModel.CreateEditSession();
conflictSession.Observable.Number = "local";
var conflictingServer = new UiOrder { Number = "server" };

var conflicted = conflictSession.Reload(conflictingServer);
// conflicted.HasConflicts == true
// conflictSession.Current.Number == "local"
// conflictSession.HasChanges == true
```
<!-- /sample -->

## Discard pending edits

Call `RevertChanges()` (or `TryRevertChanges(out conflicts)`) to restore the retained baseline values into the live model in place:

<!-- sample: ui-revert -->
```csharp
var revertModel = new UiOrder { Number = "a" };
var revertSession = revertModel.CreateEditSession();
revertSession.Observable.Number = "b";

var reverted = revertSession.TryRevertChanges(out var revertConflicts);
// reverted == true
// revertConflicts is null
// revertModel.Number == "a"
// revertSession.HasChanges == false
```
<!-- /sample -->

`TryRevertChanges` returns `false` with conflicts only when a modified member cannot be written in place (such as an `init`-only property); in that case, create a new session from a fresh model instance.

## Branch speculative edits with Fork

To edit inside a modal dialog or preview pane without mutating the parent session's model, call `session.Fork()`. The fork clones the current model state and tracks changes from that point. When the user confirms the dialog, call `session.TryMergeFrom(draft, out mergedSession, out conflicts)` to rebase the fork's edits onto the parent session:

<!-- sample: ui-fork-merge -->
```csharp
var widget = new UiWidget
{
    Title = "a",
    Child = new UiWidgetChild { Name = "m" },
};
var widgetSession = widget.CreateEditSession();
var draft = widgetSession.Fork();

draft.Observable.Title = "b";
widgetSession.Observable.Child!.Name = "n";

var didMerge = widgetSession.TryMergeFrom(
    draft,
    out var mergedSession,
    out var mergeConflicts
);
// didMerge == true
// mergeConflicts is null
// mergedSession!.Current.Title == "b"
// mergedSession!.Current.Child!.Name == "n"
if (didMerge)
{
    widgetSession = mergedSession!;
}
// widgetSession.CreateChangeSet() carries Title "a" -> "b" and Child.Name "m" -> "n"
```
<!-- /sample -->

`TryMergeFrom` does not mutate either input session; adopt `mergedSession` when it returns `true`. If both the parent session and the fork modified the same property to different values, `TryMergeFrom` returns `false` with structured conflicts and leaves both sessions unchanged:

<!-- sample: ui-fork-conflict -->
```csharp
var clashing = new UiWidget { Title = "a" };
var clashingSession = clashing.CreateEditSession();
var clashingDraft = clashingSession.Fork();

clashingSession.Observable.Title = "local";
clashingDraft.Observable.Title = "remote";

var didClashMerge = clashingSession.TryMergeFrom(
    clashingDraft,
    out var clashingMerged,
    out var clashConflicts
);
// didClashMerge == false
// clashingMerged is null
// clashConflicts!.Single().PathText == "Title"
// clashingSession.Current.Title == "local"
// clashingDraft.Current.Title == "remote"
```
<!-- /sample -->

## Edit a nested section in a child session

When a nested object or collection item needs its own scoped `Paths`, Undo/Redo history, and accept/discard lifecycle, open a child session with `session.OpenChildSession(path)`:

<!-- sample: ui-child-session -->
```csharp
var child = new UiWidgetChild { Name = "before" };
var model = new UiWidget { Title = "before", Child = child };
var session = model.CreateEditSession();
using var childSession = session.OpenChildSession(UiWidget.SparsePath.Child);

childSession.Session.Observable!.Name = "draft";
session.Observable.Title = "parent edit";

var accepted = childSession.TryAcceptChanges(out var conflicts);
// accepted == true
// conflicts is null
// model.Title == "parent edit"
// model.Child.Name == "draft"
// ReferenceEquals(model.Child, child) == true
```
<!-- /sample -->

`childSession.TryAcceptChanges(out conflicts)` rebases the child draft onto the parent's current state and writes the merged values into the existing child object in place. While a child session has unaccepted edits, `session.HasChanges` and `session.HasPendingChildSessions` are `true`, `session.CreateChangeSet()` includes the child-aware preview, and `session.PreviewChangeSet()` reports only edits made directly on the parent session. Call `session.TryAcceptChildSessions(out conflicts)` to merge all open child drafts into the parent at once.

## Where to go next

* **Blazor forms**: See [Blazor edit form](blazor-edit-form.md) and [Blazor headless components](blazor-headless-components.md) for `EditContext` integration and field components.
* **Multi-model coordination**: See [Coordinate multi-session submissions](edit-session-groups.md) when a single save spans multiple root models.
* **State queries and observers**: See [Edit session state and observers](../reference/edit-session-state.md) for `FieldState`, `CollectionState`, and `ObserveField`.

