# T.EditSession

A generated EditSession retains a baseline snapshot while its Observable edits a live class model. It does not save to a database or send network requests.

## Creation and properties

Obtain a session with `model.CreateEditSession(onChanged: ...)` or `baseline.CreateEditSession(current, onChanged: ...)`. The callback is optional and synchronous. The generated session's constructors are implementation-owned; use these extension factories.

| Property | Contract |
| --- | --- |
| `T.Observable Observable` | Write-through notifying proxy. |
| `T.ReadOnlyView Current` | Live read-only projection. |
| `bool HasChanges` | Whether the draft differs from baseline; child-session integration contributes where supported. |
| `long BaselineRevision` | Revision used to invalidate stale group submissions. |
| `Paths` | Generated typed navigation rooted at T. |
| `IDescriptorSet Descriptors` | Dynamic metadata/access entry point. |
| `ISparseEditHistory History` | Session-owned history; throws `InvalidOperationException` until enabled. |

`Model` is available through `ISparseEditSession<T>`, not a normal concrete session property. Prefer Current for reads and Observable for edits. `GetModelForFrameworkAccess()` returns the live model for adapters that control raw access.

## Capture and baseline operations

| Member | Effect |
| --- | --- |
| `T.ChangeSet CreateChangeSet()` | Captures a child-aware submission preview without accepting, discarding, or detaching child drafts. Throws if a child conflicts or its target is missing. |
| `T.ChangeSet PreviewChangeSet()` | Captures the parent session's own draft without accepting child drafts. |
| `T.Patch CreatePatch()` | Projects captured changes to commands. |
| `IReadOnlyList<SparsePath> EnumerateChangedPaths()` | Captures and lists changed paths. |
| `void AcceptChanges()` | Advances baseline to the current live state. |
| `void AcceptChanges(T.ChangeSet changes)` | Advances baseline by that captured submission, retaining later edits as pending. |
| `bool TryReconcile(T.ChangeSet submitted, T persisted, out string? error)` | Reconciles the baseline and temporary-to-permanent key identities; does not apply ordinary authoritative field values to the live draft. |
| `bool TryReconcileAndApply(T.ChangeSet submitted, T persisted, out IReadOnlyList<SparseConflict>? conflicts)` | Also updates the live draft while retaining/rebasing later edits. |
| `RebaseResult<T.ChangeSet> Reload(T serverState)` | On success, adopts a fresh server baseline and rebases pending local edits. Conflicts leave the model and baseline unchanged. |
| `void RevertChanges()` | Restores baseline in place; throws if it cannot do so. |
| `bool TryRevertChanges(out IReadOnlyList<SparseConflict>? conflicts)` | Reports inability to restore in place. |
| `void ApplyInPlace(T.ChangeSet changes)` | Applies external changes to the live draft; throws on conflict or unsupported writes. |
| `bool TryApplyInPlace(T.ChangeSet changes, out IReadOnlyList<SparseConflict>? conflicts)` | Non-throwing conflict result. |
| `void BatchEdit(Action edit)` | Coalesces observable edits into one transition notification at the outer boundary. |

Baseline operations are in-memory acknowledgements. They do not prove persistence happened. Successful baseline updates clear Undo/Redo; a failed reconciliation must not be treated as acknowledgement. AcceptChanges(changes) rejects stale transitions with InvalidOperationException. TryMergeFrom rejects self-merges or unrelated session lineages with InvalidOperationException; Try APIs do not suppress programmer errors.

## State, history, and branches

| Member | Contract |
| --- | --- |
| `FieldState(SparsePath path)` | Returns boxed point-in-time field state. |
| `FieldState<TValue>(SparsePath<T,TValue> path)` | Typed field state. |
| `CollectionState(SparsePath collectionPath)` | Point-in-time collection item state. |
| `CaptureStateSnapshot()` | Reusable `SparseChangeSetQueryView` over one captured change set. |
| `ObserveField(path)`, `WatchField(path)` | Equivalent observer factories with typed and untyped overloads. Dispose observers. |
| `EnableHistory(EditHistoryOptions? options = null)` | Enables and returns session-owned ISparseEditHistory. |
| `DisableHistory()` | Disables session-owned history. |
| `CreateHistory(EditHistoryOptions? options = null)` | Creates independently disposable `SparseEditHistory<T.ChangeSet>`. |
| `EditSession Fork()` | Creates an isolated speculative branch. |
| `bool TryMergeFrom(EditSession fork, out EditSession? merged, out IReadOnlyList<SparseConflict>? conflicts)` | Returns a merged session for a compatible fork; does not imply persistence. |

See [state and observer contracts](edit-session-state.md) for query cost, caching, thread affinity, and lifetime.

## Child-capable sessions

Models containing generated child models, collection elements, or dictionary values can expose these additional members:

| Member | Contract |
| --- | --- |
| `HasOwnChanges`, `HasPendingChildSessions` | Separate parent edits from open child drafts. |
| `OpenChildSession(typedChildPath)` | Opens an independent child draft using generated path type inference. |
| `GetChildSessionStatus(typedChildPath)` | NotOpen, OpenUnchanged, OpenChanged, or TargetMissing. |
| `ResolveChildModel(SparsePath path)` | Resolves the current live target. |
| `TryPreflightChildSessions(out conflicts)` | Checks acceptance without committing it. |
| `TryAcceptChildSessions(out conflicts)` | Attempts to merge child drafts into the parent. |
| `ConfirmChildSessions()` | Confirms previously coordinated child acceptance. |
| `DiscardChildSessions()` | Abandons child drafts. |
| `TryCreateChangeSet(out T.ChangeSet? changes, out conflicts)` | Captures the child-aware preview on a detached candidate without accepting child drafts. |
| `TryApplyPendingState(T current, out T? updated, out conflicts)` | Projects own and child pending state onto current. |

Each conflicts output uses `IReadOnlyList<SparseConflict>?`. A missing child target can cause false with null conflicts; the draft is retained. See [child session contracts](session-contracts.md#child-sessions) and the [child editing workflow](../how-to/edit-session.md#edit-a-nested-section-in-a-child-session).

## Events

`PropertyChanged` implements INotifyPropertyChanged. `TransitionObserved` is `Action<T.ChangeSet>` for committed observable transitions; BatchEdit coalesces it. `HistoryChanged` is an Action event for session-owned history changes. Notifications run synchronously on the mutating thread; there is no UI dispatcher.

## Example: acknowledge only submitted changes

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-session -->
```csharp
var model = new ReferenceSettings { Theme = "light" };
var session = model.CreateEditSession();
session.Observable.Theme = "dark";
var submitted = session.CreateChangeSet();

session.Observable.RetryCount = 5;
session.AcceptChanges(submitted);
var pending = session.CreateChangeSet();
```
<!-- /sample -->

Theme is acknowledged; RetryCount remains pending. Calling parameterless AcceptChanges instead would acknowledge both. See [saving an in-flight snapshot](../how-to/edit-session.md#submit-a-snapshot-and-acknowledge-the-response) for response reconciliation.
