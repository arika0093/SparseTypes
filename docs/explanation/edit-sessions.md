# Edit sessions and state boundaries

An `EditSession` is a live editing context for one model instance. It retains a
baseline snapshot, exposes an observable editing view, and derives changes by
comparing that baseline with the current model. It does not perform transport
or persistence. The application decides when a draft is submitted and what
server response is authoritative.

## The session has a live model and a retained baseline

`session.Observable` writes through to the model instance passed to
`CreateEditSession`; `session.Current` reads the same live values through a
read-only view. The session retains a separate fragment snapshot as its
baseline, so later mutations do not change what the session considers the
starting state.

`HasChanges` and `CreateChangeSet()` compare the baseline with the live model.
They describe semantic differences rather than control activity. An edit that
returns a member to its baseline value is clean even if a UI framework still
marks that field as touched or modified. `ChangeSet` therefore answers what
changed; a framework's dirty flag answers whether its own binding pipeline
observed input activity.

The session owns neither the model nor its lifecycle. It keeps a reference to
the model and expects the caller to manage the model instance. Revert, reload,
and supported in-place application update that instance so bindings holding
the reference remain connected. A successful save may instead replace the
session with a new one built from the server's authoritative model.

## Capturing, applying, and acknowledging are different operations

Capturing a `ChangeSet` does not mutate the model, change the baseline, or
contact a server. `AcceptChanges()` advances the baseline to the current model;
`AcceptChanges(submitted)` advances it by a previously captured transition
without replacing current values. The latter lets edits made after submission
remain pending, provided the submitted transition still matches the retained
baseline.

The caller persists the captured payload separately. This distinction keeps
network outcomes explicit: a failed request does not acknowledge the draft,
and a successful response does not automatically determine whether the
session should adopt normalized values, assigned IDs, or later local edits.
Choose among accepting, reconciling, reloading, or replacing the session based
on the response. The decision table in [Work with an edit session](../how-to/edit-session.md#submit-a-snapshot-and-acknowledge-the-response)
describes those cases.

`BatchEdit` defines a notification boundary. It coalesces observable writes
into one net transition notification, but it does not roll back writes if the
delegate throws and does not itself define one Undo step. Edit history is
opt-in; its `Group` and `BeginGroup` APIs define history boundaries. This is
why an application may use a batch for one notification and a history group
for one undo action, or use either boundary independently.

## Choosing a baseline acknowledgement

The acknowledgement operation determines how the session treats the current
model and future edits:

| Operation | Live model | Retained baseline | Typical use |
| --- | --- | --- | --- |
| `AcceptChanges()` | Unchanged | Current model | Current model is exactly what was persisted |
| `AcceptChanges(submitted)` | Unchanged | Advanced by the submitted transition | The server accepted that transition unchanged while editing continued |
| `TryReconcile(submitted, persisted, ...)` | Retargets temporary IDs | Authoritative persisted state | Persisted rows received assigned keys; other server values need not replace the local model |
| `TryReconcileAndApply(submitted, persisted, ...)` | Adopts persisted values and rebases later edits | Authoritative persisted state | Server normalization or other returned values must be adopted during continued editing |
| `Reload(serverState)` | Rebases pending edits in place | Server state | A fresh server state arrived outside the submitted-save acknowledgement |
| Fresh session from persisted model | New model/session | Persisted state | Editing can stop while saving, or no later draft must be retained |

These operations do not replace persistence concurrency control. An ETag,
revision, or database concurrency token still belongs to the application and
must protect the write atomically.

## Child sessions

A child session is an independent draft rooted at a nested model reached by a
typed path. It has its own baseline, notifications, and optional history. The
parent model remains live while the child edits its isolated copy. Accepting
the child rebases its changes against the parent's current state, so unrelated
parent edits survive and overlapping edits become structured conflicts.

The child remains open after a conflict. If its target is deleted or replaced,
the child draft is retained rather than silently discarded. The caller can
restore the target and retry, explicitly discard the child's changes, or
dispose the handle. Keyed and temporary-key paths continue to identify the
same row after reordering.

Child changes have two levels of ownership. A child `AcceptChanges` merges a
draft into its parent; it does not persist the parent. `TryAcceptChildSessions`
preflights and merges all pending descendants into the parent. A
`CreateChangeSet()` preview can include pending child drafts without accepting
them, while `PreviewChangeSet()` reports only changes owned directly by the
session. Use `HasOwnChanges` and `HasPendingChildSessions` when the UI needs to
distinguish those states.

Blazor's `SparseSessionScope` retains child sessions by typed path. In
`Draft` mode, child edits wait for explicit acceptance. In `PassThrough` mode,
each child edit is accepted into the parent, and parent state drives the
changed display. Mode changes are rejected while child edits are pending.
`SparseChildEdit` and `SparseChildEditDefault` connect a section to this scope;
they are UI components, while `OpenChildSession` is the framework-neutral API.

Enterprise editing of the [playground](https://arika0093.github.io/SparseTypes/enterprise-editing)
shows separate section drafts, nested keyed editors, and a root save preview.
The group coordinator is a separate feature for submissions across distinct
models; see [Edit-session groups](edit-session-groups.md).

## Observation and inspection

Session notifications report observable edit transitions. `onChanged` signals
that an edit occurred, while `TransitionObserved` supplies its committed
`ChangeSet`. `PropertyChanged` reports changes to session properties such as
`HasChanges`. These notifications run synchronously on the mutating thread;
they do not dispatch to a UI thread.

`HasChanges` describes the net difference between the live model and the
retained baseline, so it can return to `false` after an edit restores the
baseline value. It is not an edit counter. Raw writes through the model bypass
the observable notification pipeline. Use `Observable` for edits that must
participate in notifications and session history. Accessing a raw model
reference can also disable cached `HasChanges` checks so later reads detect
direct mutations.

`FieldState` and `CollectionState` are point-in-time queries. An
`ObserveField` watcher subscribes to invalidations and must be disposed.
Repeated field queries can share a captured `SparseChangeSetQueryView`; that
view describes one snapshot and does not update after later edits. The
[state reference](../reference/edit-session-state.md) documents exact,
ancestor, descendant, and collection-item state, along with notification and
watcher contracts. For steps to connect notifications to an application
handler, see [Monitor edits](../how-to/edit-session.md#monitor-edits).

For framework binding, in-place collection editing, save workflows, and
notification details, see [UI framework integration](ui-frameworks.md).
