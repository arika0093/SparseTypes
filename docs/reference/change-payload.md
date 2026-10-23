# ChangePayload wire reference

For method and property lookup, see [generated ChangePayload](generated-payload.md) and [payload runtime APIs](payload-api.md). This page specifies the wire representation and conversion rules.

A `ChangePayload` is the versioned JSON envelope that carries a `ChangeSet` transition, a `Patch` command, or a mix of both across a process boundary. It names members and carries typed values, so each side converts the envelope into its own `Patch` or `ChangeSet`. The library performs no domain-specific model mapping, and the client and server models may differ.

The current format token is `"version": "0.1"`. It identifies the documented grammar, not the sender and receiver model schemas, the library package version, or concurrency revisions. This format makes breaking wire changes without changing the token, so it cannot identify payloads produced by an earlier grammar; deploy compatible senders and receivers together. Schema evolution across models is also the application's responsibility, as the [version section](#version-and-malformed-payloads) explains. For the in-memory transition and command types, see [Fragments and patches](../explanation/fragments-and-patches.md). For reconciling a received transition with current state, see [ChangeSet rebase](../explanation/rebase.md).

<!-- sample: payload-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class WireSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}

[SparseModel]
public partial class WireOrder
{
    public string? Name { get; set; }

    public WireCustomer? Customer { get; set; }
}

public partial class WireCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class WireFleet
{
    public List<WireServer> Servers { get; set; } = new();
}

public partial class WireServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}

[SparseModel]
public partial class WireSecret
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
}
```
<!-- /sample -->

Serialize and deserialize the generated `T.ChangePayload`, not `T.ChangeSet` or `T.Patch`. The typed member variants are suitable for OpenAPI endpoint schemas.

## Envelope and model changes

The envelope carries `version` and one `change`. A model update contains a typed `members` object. Only changed members appear, and each member name uses camel case unless `[JsonPropertyName]` supplies another name. Nested model changes use the same `update` shape without another version token.

<!-- json-specimen: payload-envelope -->
```jsonc
{
  "version": "0.1",
  "change": {
    "kind": "update",
    "members": {
      "label": {
        "kind": "replace",
        "before": { "state": "value", "value": "before" },
        "after": { "state": "value", "value": "after" }
      }
    }
  }
}
```
<!-- /json-specimen -->

| Field | Meaning |
| --- | --- |
| `version` | Required format token, currently `"0.1"`. |
| `change` | Root model operation. Its `kind` is `update` or `replace`. |
| `members` | Optional changed-member object for an `update`. Unspecified members are unchanged. |
| `kind` | Operation discriminator. Member replacements use `replace`; nested and collection changes use `update`. |
| `before` / `after` | Endpoints for a value replacement or model snapshot replacement. |
| `items` | Keyed or dictionary item operations for a collection update. |
| `beforeOrder` / `afterOrder` | Key sequences for keyed collection updates. |

Generated structure names use a fixed contract and are not changed by serializer naming policies. Leaf values use their configured JSON metadata. Optional members are omitted, and value-free endpoints contain only `state`.

## Endpoint states

Each endpoint carries a `state`: `missing` (known absent), `null` (explicit null), `value` (with a `value`), or `redacted` (deliberately undisclosed). Only the `value` state has a `value` property.

| State | JSON | Meaning |
| --- | --- | --- |
| `missing` | `{"state":"missing"}` | Known absent. An addition arrives with a missing before-state; a removal leaves a missing after-state. |
| `null` | `{"state":"null"}` | Explicit null. Present, and the value is null. |
| `value` | `{"state":"value","value":"after"}` | Present with a value. |
| `redacted` | `{"state":"redacted"}` | Undisclosed before-state. Never read as missing. |

Redacted is never read as missing. A redacted before-state must arrive with an explicit desired after-state; envelopes that omit it, or that redact an after-state, are rejected.

## Scalar set

<!-- sample: payload-scalar-set -->
```csharp
var setBefore = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 }));
var setAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 }));
var setPayload = WireSettings.ChangeSet.Between(setBefore, setAfter).ToPayload();
var setJson = JsonSerializer.Serialize(setPayload, WireSettings.PayloadJson.Default);
// setJson == {
//   "version": "0.1",
//   "change": {
//     "kind": "update",
//     "members": {
//       "label": {
//         "kind": "replace",
//         "before": { "state": "value", "value": "before" },
//         "after": { "state": "value", "value": "after" }
//       }
//     }
//   }
// }
var setRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(setJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
// setRestored.ToPatch().Apply(setBefore) replays setAfter
```
<!-- /sample -->

The JSON above is actual serializer output. Only the changed member travels. An addition mirrors this shape with a missing before-state:

<!-- json-specimen: payload-scalar-add -->
```jsonc
{
  "version": "0.1",
  "change": {
    "kind": "update",
    "members": {
      "label": {
        "kind": "replace",
        "before": { "state": "missing" },
        "after": { "state": "value", "value": "after" }
      }
    }
  }
}
```
<!-- /json-specimen -->

## Explicit null

An explicit null stays present. It is distinct from both a value change and a removal.

<!-- sample: payload-explicit-null -->
```csharp
var nullBefore = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 }));
var nullAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = null, RetryCount = 1 }));
var nullPayload = WireSettings.ChangeSet.Between(nullBefore, nullAfter).ToPayload();
var nullJson = JsonSerializer.Serialize(nullPayload, WireSettings.PayloadJson.Default);
// nullJson == {
//   "version": "0.1",
//   "change": {
//     "kind": "update",
//     "members": {
//       "label": {
//         "kind": "replace",
//         "before": { "state": "value", "value": "before" },
//         "after": { "state": "null" }
//       }
//     }
//   }
// }
var nullRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(nullJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
// nullRestored.ToPatch().Apply(nullBefore) replays nullAfter
```
<!-- /sample -->

## Removal

A removal leaves a missing after-state. Missing never equals a present value, not even a present null or default, so the transition stays observable.

<!-- sample: payload-remove -->
```csharp
var removeBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var removeAfter = Optional<WireSettings.Fragment?>.Present(new WireSettings.Fragment { RetryCount = 1 });
var removePayload = WireSettings.ChangeSet.Between(removeBefore, removeAfter).ToPayload();
var removeJson = JsonSerializer.Serialize(removePayload, WireSettings.PayloadJson.Default);
// removeJson == {
//   "version": "0.1",
//   "change": {
//     "kind": "update",
//     "members": {
//       "label": {
//         "kind": "replace",
//         "before": { "state": "value", "value": "before" },
//         "after": { "state": "missing" }
//       }
//     }
//   }
// }
var removeRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(removeJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
// !removeRestored.ToPatch().Apply(removeBefore).Value!.Label.IsPresent
```
<!-- /sample -->

## Nested changes

A nested member recurses through the same typed shape under its `change` property.

<!-- sample: payload-nested -->
```csharp
var nestedBefore = Optional<WireOrder.Fragment?>.Present(
    WireOrder.Fragment.From(
        new WireOrder
        {
            Name = "a",
            Customer = new WireCustomer { Name = "Ann" },
        }
    )
);
var nestedAfter = Optional<WireOrder.Fragment?>.Present(
    WireOrder.Fragment.From(
        new WireOrder
        {
            Name = "a",
            Customer = new WireCustomer { Name = "Bob" },
        }
    )
);
var nestedPayload = WireOrder.ChangeSet.Between(nestedBefore, nestedAfter).ToPayload();
var nestedJson = JsonSerializer.Serialize(nestedPayload, WireOrder.PayloadJson.Default);
Console.WriteLine("NESTED=" + nestedJson);
// nestedJson carries an optional customer update with no inner version
var nestedRestored = JsonSerializer.Deserialize<WireOrder.ChangePayload>(nestedJson, WireOrder.PayloadJson.Default)!.ToChangeSet();
// nestedRestored.Customer.Name.After.Value == "Bob"
```
<!-- /sample -->

<!-- json-specimen: payload-nested-json -->
```jsonc
{
  "version": "0.1",
  "change": {
    "kind": "update",
    "members": {
      "customer": {
        "kind": "update",
        "change": {
          "kind": "update",
          "members": {
            "name": {
              "kind": "replace",
              "before": { "state": "value", "value": "Ann" },
              "after": { "state": "value", "value": "Bob" }
            }
          }
        }
      }
    }
  }
}
```
<!-- /json-specimen -->

## Keyed changes

Keyed members rebase element-wise where keys line up. Each item names its `key` and operation. An index is present only when that side has a position; additions omit `beforeIndex`, and removals omit `afterIndex`. The example moves `["a","b"]` to `["b","c"]`, so the added key `c` has `afterIndex` 1.

<!-- sample: payload-keyed -->
```csharp
var keyedBefore = Optional<WireFleet.Fragment?>.Present(
    WireFleet.Fragment.From(
        new WireFleet
        {
            Servers = new()
            {
                new WireServer { Id = "a", Host = "A" },
                new WireServer { Id = "b", Host = "B" },
            },
        }
    )
);
var keyedAfter = Optional<WireFleet.Fragment?>.Present(
    WireFleet.Fragment.From(
        new WireFleet
        {
            Servers = new()
            {
                new WireServer { Id = "b", Host = "B2" },
                new WireServer { Id = "c", Host = "C" },
            },
        }
    )
);
var keyedPayload = WireFleet.ChangeSet.Between(keyedBefore, keyedAfter).ToPayload();
var keyedJson = JsonSerializer.Serialize(keyedPayload, WireFleet.PayloadJson.Default);
// keyedJson contains an update that moves b and changes its Host,
// an addition of c at index 1, and a removal of a from index 0.
// beforeOrder is ["a","b"] and afterOrder is ["b","c"].
var keyedRestored = JsonSerializer.Deserialize<WireFleet.ChangePayload>(keyedJson, WireFleet.PayloadJson.Default)!.ToChangeSet();
// keyedRestored.Servers.GetChange("b").IsEdited == true
// keyedRestored.Servers.GetChange("c").IsAdded == true
// keyedRestored.Servers.GetChange("a").IsRemoved == true
```
<!-- /sample -->

<!-- json-specimen: payload-keyed-json -->
```jsonc
{
  "version": "0.1",
  "change": {
    "kind": "update",
    "members": {
      "servers": {
        "kind": "update",
        "items": [
          {
            // b moves from position 1 to 0 and its Host changes.
            "key": "b",
            "kind": "update",
            "beforeIndex": 1,
            "afterIndex": 0,
            "change": {
              "kind": "update",
              "members": {
                "host": {
                  "kind": "replace",
                  "before": { "state": "value", "value": "B" },
                  "after": { "state": "value", "value": "B2" }
                }
              }
            }
          },
          {
            // c is new, so it has no before position.
            "key": "c",
            "kind": "add",
            "afterIndex": 1,
            "after": {
              "state": "value",
              "value": { "Id": "c", "Host": "C" }
            }
          },
          {
            // a is removed, so it has no after position.
            "key": "a",
            "kind": "remove",
            "beforeIndex": 0,
            "before": {
              "state": "value",
              "value": { "Id": "a", "Host": "A" }
            }
          }
        ],
        // These arrays record the complete key sequence before and after.
        "beforeOrder": ["a", "b"],
        "afterOrder": ["b", "c"]
      }
    }
  }
}
```
<!-- /json-specimen -->

A pure reorder carries `update` items marked with `isReordered`, indexes, and the before and after key sequences:

<!-- json-specimen: payload-reorder -->
```jsonc
{
  "version": "0.1",
  "change": {
    "kind": "update",
    "members": {
      "servers": {
        "kind": "update",
        "items": [
          {
            "key": "b",
            "kind": "update",
            "beforeIndex": 1,
            "afterIndex": 0,
            "isReordered": true
          },
          {
            "key": "a",
            "kind": "update",
            "beforeIndex": 0,
            "afterIndex": 1,
            "isReordered": true
          }
        ],
        "beforeOrder": ["a", "b"],
        "afterOrder": ["b", "a"]
      }
    }
  }
}
```
<!-- /json-specimen -->

Scalar dictionary members use the same item vocabulary without positions. The example below edits key `b`, removes key `a`, and adds key `c` on a `Dictionary<string, int>` member:

<!-- json-specimen: payload-dict -->
```jsonc
{
  "version": "0.1",
  "change": {
    "kind": "update",
    "members": {
      "scores": {
        "kind": "update",
        "items": [
          {
            "key": "c",
            "kind": "add",
            "after": { "state": "value", "value": 4 }
          },
          {
            "key": "a",
            "kind": "remove",
            "before": { "state": "value", "value": 1 }
          },
          {
            "key": "b",
            "kind": "replace",
            "before": { "state": "value", "value": 2 },
            "after": { "state": "value", "value": 3 }
          }
        ]
      }
    }
  }
}
```
<!-- /json-specimen -->

Per-item redacted endpoints in keyed and dictionary members are rejected with a typed error. Send a whole-member blind set for those members. Blind whole-collection removal has no patch projection.

Additions of unassigned elements with `[SparseTemporaryKey]` travel as keyed
add items carrying the temporary Guid, so each pending addition keeps its own
identity through serialization instead of sharing the sentinel plus a position.
`ToChangeSet()` restores the additions under their Guids, and later edits,
removals, and reorders address them the same way. The conversion round-trip is
shown in [Keyed collections](../explanation/keyed-collections.md#temporary-identity-for-pending-additions).

## Whole-root transitions

Whole-model transitions use `kind: "replace"` with before and after snapshot endpoints. Snapshot members are keyed objects whose values are endpoints:

<!-- json-specimen: payload-root -->
```jsonc
{
  "version": "0.1",
  "change": {
    "kind": "replace",
    "before": { "state": "missing" },
    "after": {
      "state": "value",
      "value": {
        "label": { "state": "value", "value": "present" },
        "retryCount": { "state": "value", "value": 3 }
      }
    }
  }
}
```
<!-- /json-specimen -->

Whole-model replacements use the model root path during validation and conflict reporting.

## Baseline-free commands and redaction

A `Patch` enters the envelope with `ChangePayload.FromPatch(patch)`, which redacts every before-state by construction. Mark members whose previous values must not travel with `[SparseRedactBefore]`; `ToPayload()` then emits a redacted before-state while the after-state still travels, including nested models, keyed items, dictionaries, and whole-root before snapshots. A redacted `ToPayload()` transition emits the same shape as a baseline-free command.

<!-- sample: payload-command -->
```csharp
var rotation = new WireSecret.Patch { Password = "rotated-value" };
// A command needs no baseline: the envelope redacts what it never observed.
var command = WireSecret.ChangePayload.FromPatch(rotation);
var commandJson = JsonSerializer.Serialize(command, WireSecret.PayloadJson.Default);
// commandJson == {
//   "version": "0.1",
//   "change": {
//     "kind": "update",
//     "members": {
//       "password": {
//         "kind": "replace",
//         "before": { "state": "redacted" },
//         "after": { "state": "value", "value": "rotated-value" }
//       }
//     }
//   }
// }
var applied = WireSecret.Fragment.From(new WireSecret { DisplayName = "a", Password = "previous-placeholder" }).Apply(command.ToPatch());
// applied.Password.Value == "rotated-value"
```
<!-- /sample -->

`ToChangeSet()` rebuilds a complete `ChangeSet` and rejects redacted or otherwise incomplete histories instead of fabricating the missing baseline. `ToPatch()` is the explicit baseline-discarding projection and accepts them. `ChangeSet.FromPayload` accepts only fully baseline-aware envelopes and throws a typed error naming the redacted paths.

Redacting the before-value does not protect a sensitive after-value. The after-state still travels, and logs, history displays, diagnostics, validation, and UI must not echo it. Give those surfaces their own handling, and keep placeholder values out of committed samples and logs.

## Conversions

<!-- sample: payload-conversions -->
```csharp
var convertBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var convertAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 }));
// ChangeSet -> envelope -> ChangeSet keeps the before-state.
var transition = WireSettings.ChangeSet.Between(convertBefore, convertAfter);
var envelope = transition.ToPayload();
var roundTripped = JsonSerializer
    .Deserialize<WireSettings.ChangePayload>(
        JsonSerializer.Serialize(envelope, WireSettings.PayloadJson.Default),
        WireSettings.PayloadJson.Default
    )!
    .ToChangeSet();
// roundTripped.ToPatch().Apply(convertBefore) replays convertAfter
// ChangeSet -> Patch discards the before-state explicitly.
// Patch + known baseline -> ChangeSet reattaches it.
var operations = transition.ToPatch();
var reattached = WireSettings.ChangeSet.FromPatch(convertBefore, operations);
// reattached.ToPatch().Apply(convertBefore) replays convertAfter
// Envelope -> ChangeSet requires complete history: FromPayload rejects redacted envelopes.
var complete = WireSettings.ChangeSet.FromPayload(envelope);
// complete.ToPatch().Apply(convertBefore) replays convertAfter
```
<!-- /sample -->

| Conversion | Result | Information kept |
| --- | --- | --- |
| `changes.ToPayload()` | Envelope | Full transition, ready to serialize. |
| `ChangePayload.FromPatch(patch)` | Envelope | Desired operations only; before-states redacted by construction. |
| `payload.ToChangeSet()` | ChangeSet | Complete history. Rejects redacted or incomplete envelopes. |
| `ChangeSet.FromPayload(payload)` | ChangeSet | Same acceptance as `ToChangeSet()`, as a static entry point. |
| `payload.ToPatch()` | Patch | Desired operations from a valid payload. Discards baseline information; validates version and envelope structure (see below). |
| `ChangeSet.FromPatch(baseline, patch)` | ChangeSet | Attaches a known baseline to an existing patch. |
| `payload.TryApplyMixedTo(model, out updated, out outcome)` | Model plus outcome | Rebases ordinary members, passes write-only members through, applies nothing on conflict. |
| `payload.InvertReversibleChanges(out skipped)` | ChangeSet plus skipped paths | Inverts reversible transitions; write-only paths stay out and appear in `skipped`. |
| `payload.Validate()` | Validation result | Nothing converted; structural errors in payload order. |

`payload.ToPatch()` projects a well-formed payload into a baseline-free `Patch`, even when before-states are redacted. Unlike `ChangeSet.ToPatch()` on an in-memory transition, it validates the envelope structure and wire version, throwing `ArgumentException` on malformed payloads, version mismatches, or unsupported operations such as blind whole-collection removal. `FromPayload` and `ToChangeSet` fail on any redacted before-state. After-states must stay concrete (a value, an explicit null where valid, or a missing endpoint for removal); a redacted after-state is malformed. `Redacted` does not mean `Missing`: a redacted before-state paired with a missing after-state is a blind remove that validates against nothing. A merged operation regains complete history only when one side supplied a real baseline; history is never inferred from a redacted endpoint.

## Mixed requests

A member whose before-state arrives redacted is a write-only operation: the sender could not disclose the previous value. Ordinary members in the same request keep ordinary baseline-aware validation and rebase.

When composing two payloads or operations on the same member path, endpoint states combine according to these rules:

| First operation | Second operation | Merged operation | Value-level contiguity check |
| --- | --- | --- | --- |
| Transition (`before` -> `after`) | Transition (`before` -> `after`) | Transition from first `before` to second `after` | Required: first `after` must equal second `before` |
| Transition (`before` -> `after`) | Blind set (`redacted` -> `after`) | Transition from first `before` to second `after` | Not required |
| Blind set (`redacted` -> `after`) | Transition (`before` -> `after`) | Blind set (`redacted` to second `after`) | Required: first `after` must equal second `before` |
| Blind set (`redacted` -> `after`) | Blind set (`redacted` -> `after`) | Blind set (`redacted` to second `after`) | Not required |

Disjoint member paths merge without contiguity checks. A merged operation retains a complete baseline only when the first operation supplied an unredacted `before` endpoint.

<!-- sample: payload-mixed -->
```csharp
var mixed = JsonSerializer.Deserialize<WireSecret.ChangePayload>(
    """
    {
      "version": "0.1",
      "change": {
        "kind": "update",
        "members": {
          "displayName": {
            "kind": "replace",
            "before": { "state": "value", "value": "a" },
            "after": { "state": "value", "value": "b" }
          },
          "password": {
            "kind": "replace",
            "before": { "state": "redacted" },
            "after": { "state": "value", "value": "rotated-value" }
          }
        }
      }
    }
    """,
    WireSecret.PayloadJson.Default
)!;
var mixedCurrent = new WireSecret { DisplayName = "a", Password = "current-placeholder" };
if (!mixed.TryApplyMixedTo(mixedCurrent, out var mixedUpdated, out var mixedOutcome))
{
    throw new InvalidOperationException("The change conflicts with the current model.");
}
// mixedUpdated.DisplayName == "b"
// mixedUpdated.Password == "rotated-value"
// mixedOutcome.WriteOnlyPaths reports ["Password"]
```
<!-- /sample -->

`TryApplyMixedTo` rebases the ordinary members onto the current model and passes the write-only members through without historical comparison. When any ordinary member conflicts, the call returns `false` and applies nothing: the write-only subset is never committed on its own, and the current model is left untouched. The outcome names the write-only paths in `WriteOnlyPaths` and carries the structured conflicts in `Conflicts`. Strict rejection of redacted members is separate opt-in work and does not change the pass-through default.

## Rollback

`InvertReversibleChanges` inverts the reversible transitions and reports the write-only paths it had to skip. A non-empty skipped list means the result is not a complete inverse. `ChangeSet.Invert()` on a complete change set stays a true inversion.

<!-- sample: payload-invert -->
```csharp
var invertBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var invertAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 }));
var invertible = WireSettings.ChangeSet.Between(invertBefore, invertAfter).ToPayload();
// Fully baseline-aware envelopes invert with nothing skipped.
var rollback = invertible.InvertReversibleChanges(out var skipped);
// skipped is empty
// rollback.ToPatch().Apply(invertAfter) walks back to invertBefore
// Write-only members have no prior value, so they stay out of the rollback.
var writeOnly = WireSecret.ChangePayload.FromPatch(new WireSecret.Patch { Password = "rotated-value" });
var partial = writeOnly.InvertReversibleChanges(out var skippedWriteOnly);
// skippedWriteOnly reports ["Password"]
// partial carries no member change
```
<!-- /sample -->

## Validation

`payload.Validate()` checks the envelope structure without converting it, applying it, or reading model state. It needs no JSON serializer. The result carries one entry per problem, each with a `Kind`, a `Path`, and a safe `Message`, in payload order. A redacted or otherwise baseline-free envelope validates cleanly. Validity never implies that `ToChangeSet()` can rebuild the history.

<!-- sample: payload-validate -->
```csharp
var validateBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var validateAfter = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
);
var validateEnvelope = WireSettings.ChangeSet.Between(validateBefore, validateAfter).ToPayload();
var validation = validateEnvelope.Validate();
// validation.IsValid is true
// validation.Errors is empty
// Write-only envelopes validate too: validity is not convertibility.
var writeOnlyEnvelope = WireSecret.ChangePayload.FromPatch(new WireSecret.Patch { Password = "rotated-value" });
var writeOnlyValidation = writeOnlyEnvelope.Validate();
// writeOnlyValidation.IsValid is true
// writeOnlyEnvelope.ToChangeSet() still throws
validateEnvelope.Version = "0.2";
var versionValidation = validateEnvelope.Validate();
// versionValidation.IsValid is false
// versionValidation.Errors carries one Version error with an empty path
```
<!-- /sample -->

`Validate()` mirrors what the mixed interpretation paths (`ToPatch`, `InvertReversibleChanges`, `TryApplyMixedTo`) accept. `ToChangeSet()` and `ChangeSet.FromPayload` reject any redacted before-state even when validation passes, because they need complete history. `TryApplyMixedTo` returns `false` for rebase conflicts on a valid envelope. That answer reports concurrent state; the envelope itself is still well-formed.

## Version and malformed payloads

Every interpretation path (`ToChangeSet`, `ToPatch`, `InvertReversibleChanges`, `TryApplyMixedTo`) validates the envelope wire version before interpreting anything, and rejection never mutates a supplied model. The version token is not a concurrency revision: `TryApplyMixedTo` rebases ordinary members and reports conflicts, but revision checks such as ETag or rowversion comparisons stay with the application.

<!-- sample: payload-version -->
```csharp
var guarded = WireSettings
    .ChangeSet.Between(
        Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "before" })),
        Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after" }))
    )
    .ToPayload();
guarded.Version = "0.2";
// Every interpretation path rejects the same unknown version before touching model state.
var versionRejected = 0;
try
{
    guarded.ToChangeSet();
}
catch (ArgumentException)
{
    versionRejected++;
}
try
{
    guarded.ToPatch();
}
catch (ArgumentException)
{
    versionRejected++;
}
// versionRejected == 2
```
<!-- /sample -->

Only `"0.1"` is accepted, compared ordinally. A missing, null, non-string, or different version fails: envelopes that deserialize still fail validation before producing a `ChangeSet`.

The `"0.1"` token covers the wire grammar only, not the model schemas on either side. A receiver skips an unknown name in `members`, so adding a field can result in an older model silently ignoring that part of a change. Coordinate model evolution across deployments, add adapters, or agree on a separate schema handshake; the wire version does not carry that information.

The reader skips unknown metadata properties and unknown member names, including names nested in child models. A known member excluded from the JSON contract is rejected rather than treated as an unknown name. Unknown operation kinds and endpoint states, missing required fields, and duplicate JSON properties are rejected. Every endpoint object must carry an explicit `state`; `{}` or `{"value":"x"}` is not an explicit `"state":"missing"`. Depending on where input fails, deserialization throws `JsonException` or conversion throws `ArgumentException`. Other malformed payloads include null items, invalid endpoints, duplicate item identities, keyed additions without an after-endpoint, keyed removals without a before-endpoint, redacted after-states, redacted before-states without an after-state, value-carrying missing, null, or redacted endpoints, and per-item redacted endpoints in keyed and dictionary members. Diagnostics carry member paths and reasons only, never secret before, current, or after values.

### Payload export and JsonIgnore members

A `[JsonIgnore]` member remains part of the neutral payload produced by `ToPayload()` or `ChangePayload.FromPatch(patch)`. The generated JSON codec rejects serialization of that payload with `JsonException` before writing any bytes, rather than silently dropping the change. `[SparseTransportIgnore]` excludes a member from every payload format; exporting a change to it through `ToPayload()` or `ChangePayload.FromPatch(patch)` throws `InvalidOperationException`. Use `[SparseIgnore]` to exclude a property from sparse tracking entirely.

## Optional JSON support

The base package does not depend on `System.Text.Json`. JSON attributes, converters, and the endpoint bridge are generated only when the consuming compilation references a compatible API set. `Fragment`, `Patch`, `ChangeSet`, and the `ChangePayload` CLR projections (`ToPayload`, `FromPatch`, `ToPatch`, `ToChangeSet`) work without it. A compilation with a partial API set gets the same omission plus a diagnostic that names the missing members.

The payload codec is generated from the wire contract and does not use polymorphic serializer attributes. It needs `JsonSerializer`, `JsonConverter<T>`, `JsonPropertyName`, `JsonPropertyOrder`, `JsonIgnore`/`JsonIgnoreCondition`, `Utf8JsonReader`, `Utf8JsonWriter`, `JsonTokenType`, `JsonTypeInfo`, `JsonSerializerOptions.GetTypeInfo`, and the scalar codec members called by generated converters. A partial `System.Text.Json` reference omits JSON artifacts and reports [SPF028](analyzer.md#spf028-invalid-downstream-emission-plan) with missing API names; a compilation with no reference stays silent and builds the serializer-independent core.

The scalar-set payload from the earlier section serializes through the generated resolver like this:

<!-- sample: payload-context -->
```csharp
var contextJson = JsonSerializer.Serialize(setPayload, WireSettings.PayloadJson.Default);
var contextRestored = JsonSerializer.Deserialize(contextJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
// contextRestored.ToPatch().Apply(setBefore) replays setAfter
```
<!-- /sample -->

`WireSettings.PayloadJson.Default` is the shared `JsonTypeInfo<WireSettings.ChangePayload>` for the generated payload shape. Endpoint values with a covered scalar or enum type use generated context-free codecs with canonical JSON encoding and need no extra registration. Ordinary custom objects reachable from endpoints (endpoint values, key additions and removals, dictionary and list values, and nested custom objects) resolve through generated leaf codecs with the same default contract `System.Text.Json` would produce, so they need no application context either. Only shapes outside the generated contract resolve through `JsonSerializerOptions.GetTypeInfo`: combine `WireSettings.PayloadJson.Resolver` with a source-generated context for those types, or add a `JsonConverter` for the type to the serializer options. The endpoint and item-kind mapping lives in the shared generated JSON codecs invoked from per-model converters. They write state before value, always include an explicit value, and use the lower-case tokens defined by `ChangePayloadWire`. Serializing `ChangePayloadEndpoint` or the state and kind enums directly no longer produces those tokens on its own; serialize the `ChangePayload` DTOs, which reference the converters, or register a generated endpoint converter for standalone use.

Each model exposes a nested `T.PayloadJson` helper. Its `Default` property returns `JsonTypeInfo<T.ChangePayload>`, and its `Resolver` property returns the resolver used to create that metadata. Use `T.PayloadJson.Default` with serializer overloads and `T.PayloadJson.Resolver` when composing resolver chains.

## NativeAOT

NativeAOT and trimmed applications need serializer metadata at build time. Configure the application project with `PublishAot`, `IsAotCompatible`, and `EnableTrimAnalyzer`; set `JsonSerializerIsReflectionEnabledByDefault` to `false` to catch accidental reflection fallback during development.

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
</PropertyGroup>
```

Declare a source-generated `JsonSerializerContext` next to the models only for custom value shapes outside the generated leaf contract (polymorphic `object`/interface members, custom converters, extension data, and other [SPF036](analyzer.md#spf036-custom-leaf-type-needs-external-json-metadata) shapes). Payload shapes resolve through the generated `T.PayloadJson.Default` entry point, so the context never registers `T.ChangePayload`. Primitive, enum, and collection endpoint values use the generated fast paths and need no registration, and ordinary custom objects use the generated leaf codecs. `Fragment` does not have a generated JSON converter; serialize its model projection when a JSON view is needed.
The following endpoint, root-snapshot, key, and collection-element types round-trip with the generated resolver alone.

| Category | Covered types | Wire encoding |
| --- | --- | --- |
| Integers | `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, and their nullable counterparts | JSON number |
| Floating point | `float`, `double`, `decimal`, and their nullable counterparts | JSON number |
| Boolean | `bool` and `bool?` | JSON boolean |
| Text | `string`, `char`, and their nullable counterparts | JSON string; a `char` is a single-character string |
| Identifiers and time | `Guid`, `DateTime`, `DateTimeOffset`, and their nullable counterparts | JSON string in round-trip format |
| Enums | Every CLR-valid underlying integral type, including `[Flags]` combinations, and their nullable counterparts | JSON number holding the underlying value |

Covered scalar and enum values use this canonical 0.1 encoding deliberately: custom converters (including `JsonStringEnumConverter<T>`), `JsonNumberHandling`, and naming policies do not change them, so every receiver interprets the same bytes. A string token where a number belongs, a multi-character string for a `char`, or a quoted number where a strict number belongs fails with `JsonException`. Fragment property names still honor `JsonPropertyName`, naming policies, and case-insensitivity; non-scalar values resolve through options metadata and honor the registered converters and contexts.

Structural collection containers need no metadata either: sequences, sets, and dictionaries use a generated container codec, and covered element types stay context-free while custom element types fall back to options metadata. Dictionary property-name keys cover `string`, `char`, `bool`, `Guid`, and the integer widths; keyed-collection item keys and key orders cover the full scalar and enum set above. Enum and custom dictionary keys resolve through options metadata instead.

A source-generated context remains the opt-in for the unsupported shapes; it lives in the model project and registers only the custom leaf types used by the AOT examples:

<!-- sample: payload-native-aot-models -->
```csharp
using System.Text.Json.Serialization;

[JsonSerializable(typeof(AotServer))]
[JsonSerializable(typeof(PayloadItem))]
public sealed partial class PayloadLeafContext : JsonSerializerContext { }
```
<!-- /sample -->

Pass the generated `JsonTypeInfo<T>` to the `JsonSerializer` overloads. This avoids asking the serializer to discover metadata at runtime:

<!-- sample: payload-native-aot -->
```csharp
using System.Text.Json;
using SparseTypes.NativeAotFixtures;

Optional<PayloadRoot.Fragment?> WidgetState(string label, string name) =>
    Optional<PayloadRoot.Fragment?>.Present(
        PayloadRoot.Fragment.From(
            new PayloadRoot
            {
                Label = label,
                Child = new PayloadChild { Name = name, Count = 1 },
            }
        )
    );

var widgetBefore = WidgetState("old", "h1");
var widgetAfter = WidgetState("new", "h2");
var changes = PayloadRoot.ChangeSet.Between(widgetBefore, widgetAfter);
var json = JsonSerializer.Serialize(changes.ToPayload(), PayloadRoot.PayloadJson.Default);
var restored = JsonSerializer.Deserialize(json, PayloadRoot.PayloadJson.Default)!.ToChangeSet();
await Assert.That(PayloadRoot.Patch.Between(restored.ToPatch().Apply(widgetBefore), widgetAfter).IsEmpty).IsTrue();
```
<!-- /sample -->

Use `T.PayloadJson.Default` for models whose endpoints are covered scalars, enums, supported collections, nested models, or ordinary custom objects; no application context is needed for those cases. For shapes outside the generated contract, combine the generated resolver with the leaf context and use the resulting `JsonTypeInfo`: `new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(PayloadCollection.PayloadJson.Resolver, PayloadLeafContext.Default) }`. The application project holds no serializer context of its own for the supported shapes. Nested changes use the parent payload resolver; they do not need a separate registration. Verify the path with `JsonSerializerIsReflectionEnabledByDefault` disabled before publishing.

### Custom leaf objects

Ordinary custom endpoint value types, keyed additions and removals, dictionary and list values, and nested custom objects round-trip through `T.PayloadJson.Default` with no manual `JsonSerializerContext`:

<!-- sample: payload-poco-leaf -->
```csharp
var pocoBefore = Optional<PocoWidget.Fragment?>.Present(
    PocoWidget.Fragment.From(
        new PocoWidget
        {
            Leaf = new PocoLeafDto
            {
                Name = "before",
                Count = 1,
                Label = "t",
                Secret = "s",
            },
        }
    )
);
var pocoAfter = Optional<PocoWidget.Fragment?>.Present(
    PocoWidget.Fragment.From(
        new PocoWidget
        {
            Leaf = new PocoLeafDto
            {
                Name = "after",
                Count = 2,
                Label = "t",
                Secret = "s",
            },
        }
    )
);
var pocoPayload = PocoWidget.ChangeSet.Between(pocoBefore, pocoAfter).ToPayload();
var pocoJson = JsonSerializer.Serialize(pocoPayload, PocoWidget.PayloadJson.Default);
var pocoRestored = JsonSerializer.Deserialize(pocoJson, PocoWidget.PayloadJson.Default)!.ToChangeSet();
// pocoRestored.ToPatch().Apply(pocoBefore) replays pocoAfter
```
<!-- /sample -->

The generator traverses the reachable custom types at build time (transitively, with cycle detection and deduplication) and emits trim-safe readers and writers for each supported shape. Supported shapes are public classes and structs with an accessible parameterless or property-bound constructor and public serializable properties, including nested custom objects, repeated shared leaf types, nullable values, collections, and arrays. The generated contract honors `[JsonPropertyName]`, `[JsonIgnore]` (including `WhenWritingNull`/`WhenWritingDefault`), naming policies, null and default omission, and case-insensitive reading, and matches the default `System.Text.Json` wire for those shapes. A `JsonConverter<T>` added to the serializer options takes precedence over the generated codec for its type.

Shapes outside the contract keep the explicit fallback instead of a silent schema change: polymorphic `object`/interface members, custom `[JsonConverter]` and `[JsonPolymorphic]` declarations, `[JsonExtensionData]` and `[JsonInclude]` fields, `[JsonRequired]` members, types without an accessible constructor, and public fields under `IncludeFields` report [SPF036](analyzer.md#spf036-custom-leaf-type-needs-external-json-metadata) or fail at serialization time with an explicit exception naming the opt-in. Combine the generated resolver with an external `JsonTypeInfoResolver` for those values; the caller-supplied resolver stays an advanced escape hatch, never the default requirement for normal objects.

## Choosing a conversion

Use `Validate()` when an envelope arrives from an untrusted sender and the receiver needs structural feedback before interpretation. `ToPatch()` also validates the wire version and envelope structure; it projects desired operations without requiring a complete before-state. Use `ToChangeSet()` when the receiver needs complete history for rebase, conflict reporting, or full inversion. It rejects redacted before-states. Use `TryApplyMixedTo` when ordinary transitions and write-only operations share an envelope. Use `InvertReversibleChanges` to invert reversible members of a mixed envelope while reporting skipped write-only paths. These operations do not perform authorization, domain validation, or persistence concurrency checks.
