# T.ChangePayload

`T.ChangePayload` is the generated mutable transport envelope. Use it with System.Text.Json and generated conversion methods, not as a live edit session. See [generation conditions](generated-api.md#generation-conditions).

## Members

| Member | Contract |
| --- | --- |
| `ChangePayload()` | Creates an envelope with generated defaults. |
| `string? Version { get; set; }` | Wire version, currently `"0.1"`. |
| `Change { get; set; }` | Typed root model operation generated from the model contract. |
| `static ChangePayload FromPatch(T.Patch patch)` | Exports baseline-free commands with undisclosed before-state. |
| `T.ChangeSet ToChangeSet()` | Converts only when every necessary before endpoint is known. Blind/redacted-before requests are rejected. |
| `T.Patch ToPatch()` | Projects commands, dropping known history where present. |
| `ChangePayloadValidationResult Validate()` | Returns aggregate structural/version/endpoint errors. |
| `bool TryApplyMixedTo(T current, out T? updated, out result)` | Applies known transitions with rebase checks and blind commands with passthrough semantics. |
| `T.ChangeSet InvertReversibleChanges(out IReadOnlyList<string> skippedPaths)` | Inverts known history and reports blind/non-reversible paths instead of inventing before-values. |

The `result` of TryApplyMixedTo is a generated result type exposing `Conflicts`, `HasConflicts`, and `WriteOnlyPaths`. Use `out var result` rather than naming its hashed implementation container. Input current is not mutated; inspect the bool before using updated.

To export a ChangeSet, call `changes.ToPayload()`. FromPatch is intentionally not equivalent: it does not know the previous values.

Use the generated `T.PayloadJson.Default` metadata when serializing or deserializing this DTO. The DTO does not carry JSON attributes that define its wire format.

## Validation and failures

Validate reports expected input errors through `IsValid` and `Errors`. Conversion methods reject invalid payloads and incompatible history with exceptions. JSON deserialization can throw `JsonException` for malformed input. A version mismatch is an error, not a request to infer another format.

JSON-excluded changes remain in the neutral payload but make JSON serialization fail before bytes are written. Changes to `SparseTransportIgnore` members fail during payload export. `SparseRedactBefore` conceals before-state; it does not serialize a secret inside a redaction marker. Redacted endpoints cannot be recovered as known Optional values.

See [wire reference](change-payload.md) for endpoint layout, item records, whole-root changes, mixed composition, rollback, and AOT JsonSerializerContext requirements. Runtime endpoint/result types are listed in [payload runtime APIs](payload-api.md).

## Example

Uses the [reference models](attributes.md#example-model) and System.Text.Json.

<!-- sample: reference-payload -->
```csharp
var before = new ReferenceSettings { Theme = "light" };
var after = new ReferenceSettings { Theme = "dark" };
var payload = before.CreateChangeSet(after).ToPayload();
var json = JsonSerializer.Serialize(payload, ReferenceSettings.PayloadJson.Default);
var received = JsonSerializer.Deserialize<ReferenceSettings.ChangePayload>(json, ReferenceSettings.PayloadJson.Default)!;

var validation = received.Validate();
var changes = received.ToChangeSet();
```
<!-- /sample -->

The received payload is valid; its Theme transition retains both `"light"` and `"dark"`.
