# Partial updates

This guide shows how to accept baseline-free patch commands and baseline-aware change sets in a Web API, then choose how to validate, apply, and acknowledge them.

## Endpoint model

<!-- sample: partial-models -->
```csharp
using System.Text.Json;
using SparseTypes;

[SparseModel]
public partial class PartialProfile
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Phone { get; set; }
}
```
<!-- /sample -->

## Apply a baseline-free Patch command

When callers send desired values without having seen the current state (e.g. webhook or admin update), use `Patch`. It applies directly without conflict checking:

<!-- sample: partial-patch -->
```csharp
var stored = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var storedState = PartialProfile.Fragment.From(stored);

// The caller never saw a baseline: a Patch carries no preconditions.
var command = new PartialProfile.Patch { DisplayName = "Ada" };
var commanded = command.ApplyTo(stored);
// commanded.DisplayName == "Ada"
// commanded.Phone == "old-phone"
```
<!-- /sample -->

## Apply a baseline-aware ChangeSet with conflict detection

When an editor has seen the current baseline, capture and send a `ChangeSet`. The server uses the before-state to detect concurrent collisions:

<!-- sample: partial-changeset -->
```csharp
var editorBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var editorEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };

// The editor saw the baseline, so the transition keeps its before-state.
var editorChanges = editorBase.CreateChangeSet(editorEdited);
// editorChanges.DisplayName.IsChanged == true
// editorChanges.DisplayName.Before.Value == "Ann"
// editorChanges.DisplayName.After.Value == "Ada"
// editorChanges.Phone.IsChanged == false
```
<!-- /sample -->

## Wire transport with ChangePayload

Both inputs cross process boundaries using the generated `ChangePayload` envelope. Serialize the payload, not the in-memory `Patch` or `ChangeSet`; the payload carries the versioned wire representation and each endpoint converts it to the operation it needs.

<!-- sample: partial-payload -->
```csharp
var payloadBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var payloadEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };
var transition = payloadBase.CreateChangeSet(payloadEdited);

// Both inputs travel through the same envelope type.
var transitionPayload = transition.ToPayload();
var commandPayload = PartialProfile.ChangePayload.FromPatch(
    new PartialProfile.Patch { DisplayName = "Ada" }
);

// ToPatch is the information-loss boundary: the before-state is gone,
// so the projection can replay but no longer rebase or report conflicts.
var replay = transition.ToPatch();
var replayed = replay.ApplyTo(payloadBase);
// replayed.DisplayName == "Ada"
```
<!-- /sample -->

Choose the receiver operation from the guarantees the endpoint needs:

| Request contents and endpoint behavior | Receiver operation |
| --- | --- |
| Complete before/after transition; detect concurrent edits and return structured conflicts | `payload.ToChangeSet()` followed by `TryApplyTo(current, out updated, out conflicts)` |
| Baseline-free command; overwrite requested members without comparing historical values | `payload.ToPatch()` followed by `ApplyTo(current)` |
| Ordinary transitions mixed with write-only members whose before-values are redacted | `payload.TryApplyMixedTo(current, out updated, out outcome)` |
| Structural feedback before interpreting an untrusted envelope | `payload.Validate()` |

`Validate()` checks the payload's structure and wire version. It does not
check authorization, domain rules, or whether the before-values still match
the current model. A structurally valid payload can still be unsuitable for
`ToChangeSet()` when it contains redacted before-values, and a valid transition
can still conflict during rebase. Keep those checks as separate endpoint
steps.

For baseline-aware requests, apply the rebased result only when the endpoint's
conflict policy permits it. Persist the resulting model under the application's
own concurrency token; a successful rebase does not make a database write
atomic. Keep resource identifiers, revision tokens, and request metadata in
the application's endpoint contract rather than treating the payload's
`version` as a model revision.

## Audit and Undo

An in-memory `ChangeSet` retains the before-state needed for inversion.
Projecting it to a `Patch` discards that state:

<!-- sample: partial-audit -->
```csharp
var auditBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var auditEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };
var auditChanges = auditBase.CreateChangeSet(auditEdited);

// Undo needs the before-state, so only the complete ChangeSet inverts.
var undo = auditChanges.Invert();
var restored = undo.ToPatch().ApplyTo(auditEdited);
// restored.DisplayName == "Ann"
```
<!-- /sample -->

## Redacted members

`[SparseRedactBefore]` affects the wire representation, not the in-memory
transition. A complete in-memory `ChangeSet` can still be inverted, but its
payload withholds that member's before-value. Once received, the redacted
payload cannot reconstruct the complete history; project it to a `Patch` or
use `TryApplyMixedTo` when ordinary transitions and write-only operations share
one request.

<!-- sample: partial-redacted -->
```csharp
var secretBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var secretEdited = new PartialProfile { DisplayName = "Ann", Phone = "new-phone" };
var rotation = secretBase.CreateChangeSet(secretEdited);

var wire = JsonSerializer.Serialize(rotation.ToPayload(), PartialProfile.PayloadJson.Default);
// wire carries "new-phone" but never "old-phone"
var incoming = JsonSerializer.Deserialize<PartialProfile.ChangePayload>(wire, PartialProfile.PayloadJson.Default)!;

// The redacted payload still projects to a baseline-free command.
var replayed = incoming.ToPatch().ApplyTo(secretBase);
// replayed.Phone == "new-phone"

// The received payload cannot become a complete ChangeSet:
// its redacted before-state is not available to reconstruct.
var rejected = false;
try
{
    incoming.ToChangeSet();
}
catch (ArgumentException)
{
    rejected = true;
}
// rejected == true
```
<!-- /sample -->

Redacting a before-value does not hide the after-value. The requested phone
still travels to the server and may appear in client-side session history,
change previews, or logs. Mask those surfaces in the application; wire
redaction alone is not a display or retention policy.

For serialization format details, see [ChangePayload reference](../reference/change-payload.md). For server rebase rules, see [ChangeSet rebase](../explanation/rebase.md).
