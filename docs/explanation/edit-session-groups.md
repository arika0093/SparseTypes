# Edit-session groups

An edit session tracks changes to a single root model. When a user screen edits
several distinct root models at once, such as an account profile and
notification settings, each model has its own generated session and `ChangeSet`
type. Saving those sessions one by one without coordination creates
partial-validation problems: one section can pass validation and start a network
request while another section is still invalid or mid-draft.

`EditSessionGroup` coordinates several typed edit sessions as sections of one
submission. It captures change sets and changed paths across heterogeneous
sessions, validates them together, and lets the application acknowledge or
reconcile each section independently after persistence. It does not provide
transport, persistence, cross-session database transactions, or a global edit
lock.

For a runnable workflow covering registration, submission, and independent acknowledgement, see [Coordinate multi-session submissions](../how-to/edit-session-groups.md). For the API surface, see [Session contracts, history, and groups](../reference/session-contracts.md#editsessiongroup).

## Groups and child sessions solve different problems

SparseTypes provides two ways to coordinate drafts: child sessions and
edit-session groups. Choosing between them depends on whether the models share a
single root graph.

| Aspect | Child session (`OpenChildSession`) | Edit-session group (`EditSessionGroup`) |
| --- | --- | --- |
| Target | A nested model reached by a typed path within one root model | Separate root models, often of different types |
| Isolation | Edits an isolated copy of the nested model | Wraps existing live sessions in place |
| Acceptance | Merges the child draft into the parent session | Advances or reconciles each section's own baseline after save |
| Combined output | Contributes to the parent's single `ChangeSet` | Produces an `EditSessionGroupSubmission` with per-section typed entries |

A section inside an `EditSessionGroup` can also have open child sessions of its
own. The group treats each registered root session as a section, and that
section's capture step projects its pending child drafts into the section's
`ChangeSet`. See [Edit sessions and state boundaries](edit-sessions.md#child-sessions)
for how child drafts relate to their parent session.

## Registration and typed handles

Because a group holds sessions of different model and `ChangeSet` types,
`Register` returns a typed `EditSessionSectionHandle<TModel, TChangeSet>`. The
group stores sections by a stable, non-empty string identifier using ordinal,
case-sensitive comparison, while the caller keeps the typed handle to read
captured changes and acknowledge results without casting.

An `EditSessionGroupRegistration<TModel, TChangeSet>` supplies the session and
the delegates that connect its generated types (`CreateChangeSet`, `IsEmpty`,
`EnumerateChangedPaths`, and optional validation or `TryReconcile` callbacks).
For generated sessions, `group.Register(sectionId, session)` wires the capture
delegates automatically. A custom session implementation can implement
`ISparseEditSessionWithBaselineRevision` so the group can detect baseline
changes made outside the group.

## Capture purity and child sessions

Capturing a submission never mutates a registered session or accepts its child
drafts. When the group calls a generated session's `CreateChangeSet`, the
session builds a child-aware preview: it composes its own changes with the
projected edits from any open descendant child sessions, while leaving those
child drafts open and pending.

This purity matters when validation or persistence fails. If capture merged
child drafts into the parent immediately, a rejected submission would leave the
user's modal or sub-form drafts already collapsed into the parent model. Keeping
capture side-effect-free means a failed preparation or a failed network save
leaves every parent session and child draft untouched.

If an open child session conflicts with concurrent parent edits or points to a
target item that was removed from the parent collection, `CreateChangeSet`
cannot compose the preview. The group catches that failure during preparation,
reports the message as a validation issue for that section, and leaves all
drafts pending.

Child drafts are merged only when a section is acknowledged after a save, or
when the caller explicitly accepts them on the session with
`TryAcceptChildSessions` or on an individual child handle with `AcceptChanges`.
The older `TryPrepareChangeSet` and `PrepareChangeSet` aliases were removed so
that capture methods never hide side effects. Callers that previously relied on
capture-time child acceptance call `TryAcceptChildSessions` before capturing.

## Readiness and submission validation

Group validation separates checks that depend on live UI or session state from
checks that inspect the immutable payload being submitted. A registration can
supply three hooks:

<!-- illustrative: shows the three validation hook signatures on a registration -->
```csharp
var registration = new EditSessionGroupRegistration<MyModel, MyModel.ChangeSet>(session)
{
    Validate = () => viewModel.CanSubmit ? null : "Section is locked.",
    ValidateChanges = changes => changes.IsEmpty ? null : ValidateRules(changes),
    ValidateAsync = (changes, ct) => remoteValidator.CheckAsync(changes, ct),
};
```

`Validate` is the live-session readiness hook. It takes no arguments and inspects
current mutable state such as UI locks or permissions on the live session. It
does not receive the captured submission.

`ValidateChanges` is the synchronous submission validator, and `ValidateAsync` is
its asynchronous counterpart. Both receive the exact immutable `ChangeSet`
captured for the submission. They must inspect that snapshot because the user
may continue editing the live session while `PrepareAsync` awaits asynchronous
validation.

`Prepare` and `PrepareAsync` run capture, readiness, and submission validation
in a fixed order:

1. Capture runs synchronously across every registered section, producing
   immutable `ChangeSet` snapshots and changed-path lists while leaving live
   sessions and child drafts untouched.
2. Readiness runs `Validate` across every registered section, including sections
   whose captured `ChangeSet` is empty, because a clean section can still block
   submission when it is in an unready UI state.
3. Submission validation runs `ValidateChanges` (and `ValidateAsync` in
   `PrepareAsync`) against each section's captured `ChangeSet`.

`Prepare` rejects registrations that configure `ValidateAsync`; callers with
asynchronous rules use `PrepareAsync(cancellationToken)`. If a validator throws
an exception other than cancellation, the group converts the exception message
into a validation issue for that section. Validation issues across all sections
are collected and thrown together as `EditSessionGroupValidationException`,
while cancellation propagates as `OperationCanceledException`.

### Concurrency during asynchronous preparation

Because `PrepareAsync` captures all sections before its first `await`, edits
made to a live session or its child sessions while asynchronous validation is
in flight do not alter the captured submission. Those post-capture edits stay
pending on the live session for a future submission.

After its awaits complete, `PrepareAsync` re-checks structural invariants before
returning the submission:

* If a section was unregistered or replaced during validation, preparation fails
  with `EditSessionGroupValidationException`.
* If a section's baseline revision advanced during validation (for example,
  because an earlier submission was acknowledged while the second preparation was
  awaiting validation), preparation fails with
  `EditSessionGroupValidationException`.

Ordinary local edits do not invalidate the in-flight preparation, and the live
`Validate` readiness hook is not run a second time after awaits.

The group never holds its internal lock across user callbacks or awaits, so a
validator can inspect the group without deadlocking. The internal lock only
serializes registration and acknowledgement bookkeeping; callers should still
coordinate model edits on the UI thread while calling `Prepare` or starting
`PrepareAsync` so that the initial synchronous capture sees a consistent point
in time across sections.

### Migrating existing `Validate` callbacks

Existing `Validate` callbacks keep their meaning as live-session readiness
checks and need no signature change when they inspect UI state or permissions.
When a `Validate` callback reads a change set from the live session, move that
check to `ValidateChanges` so it inspects the captured `ChangeSet` directly. If
`ValidateAsync` is configured and the same synchronous rule should also run in
`Prepare`, register that rule in `ValidateChanges` alongside `ValidateAsync`.

## Independent acknowledgement and reconciliation

`Prepare` and `PrepareAsync` return an `EditSessionGroupSubmission`. It exposes
ordered section metadata, typed section entries via `submission.Get(handle)`,
and aggregate changed paths that pair each section identifier with its original
`SparsePath`.

When an application persists a multi-section submission, different sections may
be saved by different backend services or fail independently. The group
acknowledges one handle at a time so each section can advance on its own
schedule:

* `AcceptChanges(submission, handle)` merges that section's currently pending
  child sessions into the live model, then advances only that section's baseline
  by the exact captured `ChangeSet`.
* `TryReconcile(submission, handle, persisted, out error)` invokes the section's
  registered `TryReconcile` callback with the captured `ChangeSet` and the
  server's authoritative model, adopting server-assigned keys or normalized
  values while preserving local edits made after capture.

Because acknowledgements are per-section, a section whose backend call failed
remains dirty (`HasChanges == true`) while successful sections advance their
baselines. Edits made to a live session or its child sessions after `Prepare`
are preserved: child edits are merged into the live parent model during
`AcceptChanges`, and both those child edits and any post-capture parent edits
remain dirty relative to the newly advanced baseline. If a post-capture edit
conflicts with a still-pending child draft when `AcceptChanges` tries to merge
children, the acknowledgement throws and leaves the section unacknowledged.

## Baseline revision tracking and stale submissions

Every registered section tracks a baseline revision number. When `Prepare`
captures a section, the submission records the section's baseline revision at
that moment.

Acknowledging or reconciling a submission checks that the section's current
baseline revision still matches the revision recorded in the submission. If
another submission was already acknowledged for that section, or if the
underlying session's baseline changed directly, the recorded revision is older
than the current revision. `AcceptChanges` then throws
`EditSessionStaleSubmissionException`, and `TryReconcile` returns `false` with a
stale-submission error. This prevents an out-of-order network response or a
double-click save from re-applying an obsolete transition on top of a newer
baseline. Keep at most one outstanding submission per section when reconciling,
while other sections in the same group can still be acknowledged independently.

Generated sessions implement `ISparseEditSessionWithBaselineRevision` and expose
their committed baseline revision directly to the group. When code updates the
baseline on the generated session directly outside the group, the session's
revision advances and the group automatically treats earlier group submissions
for that section as stale. If a baseline commit succeeds on the session and a
subsequent notification handler throws, the group still records the committed
revision before rethrowing the handler exception; a rejected acknowledgement
leaves the revision unchanged.

Custom session implementations should implement
`ISparseEditSessionWithBaselineRevision` to get the same protection against
direct baseline changes. When a custom session does not implement that
interface, the group tracks only the acknowledgements routed through the group
itself, so callers must treat the group as the sole baseline owner while that
session is registered.

## Unregistering and replacing sections

Screen lifecycles sometimes swap out one section's model while keeping the rest
of the group active. `Unregister` and `Replace` require an explicit
`EditSessionGroupRemovalPolicy` so that unsaved edits are not dropped by
accident:

* `RequireClean` rejects the removal or replacement if the section has pending
  own changes or pending child sessions.
* `DetachWithPendingChanges` removes or replaces the registration while leaving
  the old session and its child sessions untouched.

Replacing a section returns a new handle and invalidates both the previous
handle and any in-flight submissions captured from it. Attempting to acknowledge
an unregistered or replaced handle, or a submission created by a different
`EditSessionGroup` instance, fails with `EditSessionStaleSubmissionException`.

