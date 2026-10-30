# Session contracts, history, and groups

Package and namespace: `SparseTypes`. Applications normally use [generated EditSession](generated-edit-session.md). These interfaces let framework code consume session capabilities without naming generated types.

## Capability interfaces

| Interface | Members |
| --- | --- |
| `ISparseEditSession<TModel>` | `Model`, `HasChanges`, `AcceptChanges()`. Model is covariant and constrained to class. |
| `ISparseEditSession<TModel,TChangeSet>` | Adds `AcceptChanges(TChangeSet changes)`. ChangeSet is a class and contravariant. |
| `ISparseEditSessionWithChangeSet<TModel,TChangeSet>` | Adds `CreateChangeSet()`, `IsEmpty(changes)`, and `EnumerateChangedPaths(changes)`. |
| `ISparseEditSessionWithPaths<TModel,TPaths>` | Adds `Paths`. |
| `ISparseEditSessionWithBaselineRevision` | `long BaselineRevision`. |
| `ISparseEditSessionWithBatchEdit` | `BatchEdit(Action edit)`. |
| `ISparseEditSessionWithDescriptors` | `IDescriptorSet Descriptors`. |
| `ISparseEditSessionWithFieldState<TModel>` | `FieldState(SparsePath)` and typed `FieldState<TValue>(SparsePath<TModel,TValue>)`. |
| `ISparseEditSessionWithCollectionState` | `CollectionState(SparsePath collectionPath)`. |
| `ISparseEditSessionWithStateSnapshot` | `CaptureStateSnapshot()`. |
| `ISparseEditSessionWithHistory` | `History`, `EnableHistory(options = null)`, `DisableHistory()`, `HistoryChanged`. |
| `ISparseEditSessionWithPendingState<TModel,TConflict>` | `TryApplyPendingState(current, out updated, out conflicts)`. |
| `ISparseEditSessionWithChildren<TModel,TConflict>` | `HasPendingChildSessions`, `OpenChildSession(path)`, `TryAcceptChildSessions(out conflicts)`, `ConfirmChildSessions()`, `DiscardChildSessions()`. |
| `ISparseEditSessionWithChildStatus<TModel,TConflict>` | Adds `HasOwnChanges`, `GetChildSessionStatus(path)`, `ResolveChildModel(path)`, `TryPreflightChildSessions(out conflicts)` and pending-state capability. |
| `ISparseEditSessionModelAccessor<TModel>` | `GetModelForFrameworkAccess()` for infrastructure reads. |
| `ISparseEditController<TModel>` | Read-only save state and validation capability exposed to UI adapters. |

`Model` is the live mutable object, not a snapshot. Accessing it through the minimal interface can disable session dirty caching because later raw mutations cannot notify. Frameworks use the dedicated model accessor when they control how the reference is used.

## History

`EditHistoryOptions` has a parameterless constructor and settable `MaxEntries`, default 50, minimum 1.

`ISparseEditHistory` exposes the complete application history surface:

| Member | Contract |
| --- | --- |
| `CanUndo`, `CanRedo`, `UndoCount`, `RedoCount`, `MaxEntries` | Current stack state and capacity. |
| `Undo()`, `Redo()` | Replay in place; throw `InvalidOperationException` on empty stacks or conflicts. |
| `TryUndo(out conflicts)`, `TryRedo(out conflicts)` | Non-throwing conflict/availability result; conflicts use `IReadOnlyList<SparseConflict>?`. |
| `BeginGroup()` | Returns an IDisposable scope combining edits into one entry. |
| `Group(Action action)` | Scoped group around the callback. |
| `Clear()` | Empties both stacks. |
| `StateChanged` | Action event when history state changes. |

`session.EnableHistory()` owns the history lifetime. `CreateHistory()` instead returns a separately disposable `SparseEditHistory<TChangeSet>`; that concrete type additionally exposes `OnTransition(changes)`, `Dispose()`, and `Disposed`. Its `(options, hooks)` constructor is for custom adapters. `SparseEditHistoryHooks<TChangeSet>` supplies `Compose`, `Invert`, `IsEmpty`, `Subscribe`, `Unsubscribe`, and `TryApply` delegates; generated sessions wire these automatically.

See the [history example](../how-to/edit-session.md#record-undo-and-redo-history) and [notification contract](edit-session-state.md#notification-and-cache-contract).

## Child sessions

`ISparseEditChildSession<TSession>` is the short contract for code that stores an opened child session:

| Member | Contract |
| --- | --- |
| `Session` | Independent editable child session. |
| `HasChanges`, `IsDetached`, `IsTargetMissing` | Draft state and current target availability. |
| `TryAcceptChanges()` | Merges into the current parent target. Failure retains the draft. |
| `AcceptChanges()` | Throwing acceptance. |
| `DiscardChanges()`, `Dispose()` | Abandons the draft. |

`ISparseEditChildSession<TModel,TConflict,TSession>` adds typed conflict details and `TryApplyPendingState(current, out updated, out conflicts)`. The path-bound `ISparseEditChildSession<TModel,TPaths,TConflict,TSession>` extends that contract for integrations that need the generated path type.

The implementation `SparseEditChildSession<...>` also exposes `TryPreflightAcceptChanges(out conflicts)` and `ConfirmAccept()` for coordinated submission. Its delegate-based constructor is an adapter API; obtain children through generated `OpenChildSession`.

`SparseChildSessionStatus` is `NotOpen`, `OpenUnchanged`, `OpenChanged`, or `TargetMissing`. A missing target is distinct from detachment: acceptance can fail with null conflicts because there is no target, while the draft remains open.

## EditSessionGroup

`new EditSessionGroup()` coordinates independent sessions. It performs no persistence.

| Member | Contract |
| --- | --- |
| `Register<TModel,TChangeSet>(sectionId, session)` | Registers a generated session and returns a typed handle. |
| `Register<TModel,TChangeSet>(sectionId, registration)` | Registers explicit readiness, capture, validation, and reconciliation hooks. |
| `Unregister(handle, policy)` | Removes a registration. |
| `Replace(handle, registration, policy)` | Replaces registration and invalidates old handles/submissions. |
| `Prepare()` | Validates and captures a submission synchronously. |
| `PrepareAsync(CancellationToken cancellationToken = default)` | Also runs configured asynchronous validation. |
| `AcceptChanges(submission, handle)` | Advances only that section by the captured change set. |
| `TryReconcile(submission, handle, persisted, out string? error)` | Reconciles the captured section against authoritative persisted state. |

Section IDs are non-empty, ordinal, and case-sensitive. Duplicate IDs and registering the same session twice throw `ArgumentException`. Removal policies are `RequireClean` and `DetachWithPendingChanges`.

`EditSessionGroupRegistration<TModel,TChangeSet>` has parameterless and generated-session constructors. Init properties are `Session`, `CreateChangeSet`, `IsEmpty`, `EnumerateChangedPaths`, optional `Validate` (`Func<string?>`), `ValidateChanges` (`Func<TChangeSet,string?>`), `ValidateAsync` (`Func<TChangeSet,CancellationToken,Task<string?>>`), and `TryReconcile` (`Func<TChangeSet,TModel,(bool Success,string? Error)>`). Null validation messages mean success.

The returned `EditSessionSectionHandle` exposes `SectionId` and `Session`. `EditSessionGroupSubmission` exposes `Sections`, `ChangedPaths`, and `Get(handle)`. The typed entry has `Changes`; entries also expose `SectionId`, `BaselineRevision`, and `ChangedPaths`. Group changed-path entries expose `SectionId`, `Path`, and `PathText`.

Validation failures throw `EditSessionGroupValidationException` with `Issues` (`SectionId`, `Message`). Invalidated baseline revisions or registrations produce `EditSessionStaleSubmissionException`; do not acknowledge a stale submission as though it were current.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-group -->
```csharp
var session = new ReferenceSettings { Theme = "light" }.CreateEditSession();
var group = new EditSessionGroup();
var handle = group.Register("settings", session);
session.Observable.Theme = "dark";

var submission = group.Prepare();
var submitted = submission.Get(handle).Changes;
group.AcceptChanges(submission, handle);
```
<!-- /sample -->

`submitted` retains the Theme transition, and the session is clean after acknowledgement. In an application, acknowledgement follows successful persistence. See [group submission workflow](../how-to/edit-session-groups.md).
