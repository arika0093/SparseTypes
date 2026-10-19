# Work with an edit session

An edit session tracks a live model against a retained baseline. Use it for an
in-memory draft, then choose how to acknowledge the server response. This guide
covers the session lifecycle; the [EditSession explanation](../explanation/edit-sessions.md)
describes the state boundaries and child-session model.

## Start a draft and edit it

Call `CreateEditSession()` on the model that the UI will edit. The session keeps
that model instance as its live state and takes an isolated baseline snapshot.
Bind writes to `session.Observable`; read current values from `session.Current`.
The session does not replace the live model when it reverts or reloads.

`HasChanges` compares the live state with the retained baseline. It reports
semantic differences, not whether a control was touched: changing a value and
restoring its baseline value leaves the session clean. Capture the transition
with `CreateChangeSet()` when you need to preview, save, or inspect pending
edits. Capturing does not accept them.

For a single user action that changes several values, use `BatchEdit` to
coalesce session notifications into one net transition. If Undo should also
treat those edits as one step, enable history and use `History.Group` or
`History.BeginGroup`; batching notifications alone does not create an undo
group. History is opt-in and should be disposed or disabled when it is no
longer needed. See [edit history](../explanation/ui-frameworks.md#edit-history-lifecycle-and-grouping)
for limits and lifecycle details.

## Submit a snapshot

Capture the `ChangeSet` before starting asynchronous work and send its
`ChangePayload` through the application's transport. The captured transition
is immutable: edits made while the request is in flight do not change the
submitted snapshot. Disable editing during the request if the application
cannot retain or reconcile later edits.

On a transport failure, leave the session baseline alone. The draft remains
dirty and can be retried. On success, choose the acknowledgement based on the
server response:

| Server response and editing policy | Session operation |
| --- | --- |
| The user stops editing and the server returned authoritative state, including normalization or assigned IDs | Create a fresh session from that state and rebuild UI-bound state such as Blazor's `EditContext` |
| The server persisted the submitted transition unchanged; editing continued during the request | `AcceptChanges(submitted)` advances the baseline by that snapshot and leaves later edits pending |
| The server returned authoritative values and editing continued during the request | `TryReconcileAndApply(submitted, persisted, out conflicts)` adopts the response and rebases later edits onto it |
| The only server-side change to correlate is an assigned key for a pending `[SparseTemporaryKey]` row | `TryReconcile(submitted, persisted, out error)` assigns keys and preserves later edits without adopting other server-normalized values |
| A fresh server state arrived outside a save, and pending edits should remain | `Reload(serverState)` rebases pending edits in place; inspect and handle any conflicts |

`AcceptChanges()` acknowledges the current live model as the new baseline.
Use it only when that current state is the state the server accepted. It does
not send data or confirm persistence.

`AcceptChanges(submitted)` is for a response that confirms exactly the captured
transition. It does not copy server values into the live model. If the server
normalized values or assigned persistent keys, use reconciliation or start a
new session from the authoritative response instead.

Both reconciliation methods preserve edits made after `submitted` when their
preconditions can be satisfied. `TryReconcile` focuses on temporary-key
correlation; `TryReconcileAndApply` also adopts authoritative property values.
Check the result and surface conflicts or errors. A failed reconciliation does
not advance the baseline. For the keyed-row lifecycle and server-side mapping,
see [saving pending additions](../explanation/keyed-collections.md#saving-pending-additions-with-reconcile).

## Discard a draft

Call `RevertChanges()` to restore the retained baseline in the existing model.
It does not contact the server or advance the baseline. Use
`TryRevertChanges(out conflicts)` when the model may contain members that
cannot be written in place; a failed attempt leaves the draft unchanged.

If pending edits cannot be written in place, replace the session with one
created from the desired model state. Recreate framework objects bound to the
old session as well.

## Edit a nested section independently

Use `OpenChildSession` when a nested editor needs its own baseline, paths, or
undo history. A child session keeps edits separate until its handle accepts
them into the parent. The parent can continue editing other members; accepting
the child rebases its changes onto the parent's current state and reports
overlaps as conflicts.

Before saving a parent that has child sessions, decide whether the child drafts
should remain separate or be included. `CreateChangeSet()` creates a
child-aware preview without accepting drafts. `TryAcceptChildSessions` merges
all pending child sessions into the parent; call it when the parent state
itself must be updated before capture. See [independent child sessions](../explanation/edit-sessions.md#child-sessions)
for missing targets, nested drafts, and Blazor's draft and pass-through modes.

For framework binding, collection facades, field state, and notifications, see
[UI framework integration](../explanation/ui-frameworks.md). For coordinating
different models in one submission, see [EditSessionGroup](edit-session-groups.md).
