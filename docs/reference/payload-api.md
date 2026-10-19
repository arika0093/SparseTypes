# Payload runtime APIs

Package and namespace: `SparseTypes`. These serializer-independent types support [generated ChangePayload](generated-payload.md). The [wire specification](change-payload.md) defines JSON layout and composition rules.

## ChangePayloadEndpoint<T>

| Member | Contract |
| --- | --- |
| Parameterless constructor | Creates the default Missing endpoint. |
| `ChangePayloadState State { get; set; }` | Missing, Null, Value, or Redacted. |
| `T? Value { get; set; }` | Payload value for the Value state. |
| `bool IsRedacted` | Whether State is Redacted. |
| `FromOptional(Optional<T> value)` | Static conversion preserving missing, null, and value. |
| `Redacted()` | Static value-free endpoint factory. |
| `GetInvariantError()` | Message for a value-free state carrying a non-default value, or null. |
| `Validate()` | Throws `InvalidOperationException` for that invariant violation. Does not replace aggregate validation. |
| `ToOptional()` | Converts a valid known endpoint. Throws for redacted, invalid state/value combinations, and null on a non-nullable value type. |

For value types, default(T) is allowed in value-free endpoint storage. A redacted endpoint contains no undisclosed plaintext. It is not a Missing endpoint and cannot become a known ChangeSet before-value.

## Validation results

`ChangePayloadValidationResult(IReadOnlyList<ChangePayloadValidationError> errors)` exposes `Errors` and `IsValid`.

`ChangePayloadValidationError` has a parameterless constructor and init properties `Kind`, `Path`, and `Message`. `ChangePayloadValidationKind` contains `Version`, `Changes`, `Duplicate`, `Endpoint`, `Item`, and `Order`.

Use `payload.Validate()` for the aggregate result before conversion when reporting client input errors. Endpoint.Validate checks only one endpoint's invariant.

## Wire tokens

`ChangePayloadWire` defines version `"0.1"` and these constant groups:

| Constants | Tokens |
| --- | --- |
| `VersionProperty`, `ChangesProperty`, `DiscriminatorProperty`, `RootDiscriminator` | `version`, `changes`, `member`, `$root` |
| `StateProperty`, `ValueProperty` | `state`, `value` |
| `MissingState`, `NullState`, `ValueState`, `RedactedState` | `missing`, `null`, `value`, `redacted` |
| `AddKind`, `RemoveKind`, `EditKind`, `ReorderKind` | `add`, `remove`, `edit`, `reorder` |

`ToToken` accepts `ChangePayloadState` or `ChangePayloadItemKind`. `TryParseState(string? token, out state)` and `TryParseKind(string? token, out kind)` return false for unknown tokens. `StatePropertyUtf8()`, `ValuePropertyUtf8()`, `StateTokenUtf8(state)`, and `KindTokenUtf8(kind)` expose ReadOnlySpan<byte> tokens for serializers.

`ChangePayloadItemKind` values are Add, Remove, Edit, and Reorder.

## Mixed-operation contracts

These advanced APIs describe composition without knowing generated CLR member types:

| Type | Public surface |
| --- | --- |
| `MixedMemberOperation` | Constructors `(path, history, after)` and `(path, history, after, isWholeRoot)`; init properties `Path`, `History`, `After`, `IsWholeRoot`. |
| `MixedHistoryKind` | `Transition`, `BlindSet`. |
| `MixedAfterKind` | `Value`, `Null`, `Missing`. |
| `MixedComposeResult` | Constructor for all properties; `Succeeded`, `Path`, `ResultHistory`, `ResultAfter`, `RequiresContinuityCheck`, `ResultIsWholeRoot`, `FailureReason`. |
| `MixedSequenceComposition` | Constructors taking succeeded, composed operations, failures, and optionally pending continuity checks; read-only `Succeeded`, `IsFullyComposable`, `Composed`, `Failures`, `PendingContinuityChecks`. |
| `MixedRollbackPlan` | Constructor `(reversiblePaths, skippedPaths)`; read-only `ReversiblePaths`, `SkippedPaths`, `IsComplete`. |
| `RedactedBeforePolicy` | `Passthrough` is the only supported value. |

`MixedChangeAlgebra` exposes `WholeRootPath`, `Compose(first, second)`, `ComposeSequences(first, second)`, `CanFormChangeSet(operations, out blindPaths)`, `CreateRollbackPlan(operations)`, `IsAncestorOrDescendant(first, second)`, and `EnsurePassthrough(policy, parameterName = null)`.

Composition planning alone cannot verify endpoint values. `PendingContinuityChecks` means the typed layer still has to check continuity; `Succeeded` is not the same as `IsFullyComposable`. Prefer generated payload conversions and mixed application for ordinary application code.
