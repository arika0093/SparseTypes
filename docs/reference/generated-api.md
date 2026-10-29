# Generated API overview

SparseTypes adds model-specific APIs to supported partial models. The tables use `T` as a placeholder for your model name; the [example model](attributes.md#example-model) generates `ReferenceSettings.Fragment`, not a generic `Fragment<T>`.

## Model members and extensions

| Entry point | Contract |
| --- | --- |
| `T DeepClone()` | Instance method returning an independent clone of supported mutable state. Preserves supported reference cycles. |
| `T.SparsePath` | Static root navigation builder; reading paths does not read the mutable model. |
| `baseline.CreateChangeSet(T current)` | Extension equivalent to `T.ChangeSet.Between(baseline, current)`. |
| `model.CreateEditSession(Action? onChanged = null)` | Extension capturing a baseline snapshot and editing the supplied model live. Class models only. |
| `baseline.CreateEditSession(T current, Action? onChanged = null)` | Extension using a separate baseline and live current model. Class models only. |
| `Optional<T?>.ToObservable(Action? onChanged = null)` | Extension mapping a present model to an Observable and preserving missing/present-null states. Class models only. |
| `session.EditCollection<TElement>(SparsePath collectionPath)` | Extension returning a path-addressed collection editor. |
| `session.EditCollection<TElement,TKey>(SparsePath collectionPath)` | Extension returning a keyed collection editor. |
| `session.CreateEditor(options = null)` | Creates an optional UI-neutral save and validation controller. |

Import the model namespace to bring its generated extensions into scope. Their containing class names have generated hash suffixes; use extension syntax rather than naming those classes.

## Companion types

| Type | Role |
| --- | --- |
| [T.Fragment](generated-fragment.md) | Partial snapshot with presence-aware members. |
| [T.Patch](generated-patch.md) | Mutable, baseline-free command. |
| [T.ChangeSet](generated-changeset.md) | Immutable before-to-after transition. |
| [T.ChangePayload](generated-payload.md) | Mutable transport envelope with generated JSON support. |
| [T.EditSession](generated-edit-session.md) | Live class-model editing against a retained baseline. |
| [T.Observable](generated-views.md) | Notification-producing proxy; public setters only where the model supports them. |
| [T.ReadOnlyView](generated-views.md) | Live read-only projection, not an immutable snapshot. |

Builders and typed navigation helpers reside in a per-model container under `SparseTypes.Generated`. Operation kernels and generated wire DTOs are implementation support. Use `var`, typed path interfaces, and model-facing factories instead of writing container names.

## Generation conditions

| Condition | API consequence |
| --- | --- |
| Supported class model | Session, observable, and read-only companion surfaces are available. |
| Struct model | Fragment/Patch/ChangeSet and cloning do not imply class-only session APIs. |
| Init-only or constructor-bound member | Snapshot construction can succeed even when in-place edits cannot write that member. Check result/capability APIs. |
| Reachable supported partial nested type | Receives promoted model companions without its own SparseModel attribute. |
| Keyed collection or dictionary | Generates shape-specific patch, transition, and path members. |
| System.Text.Json referenced | Emits optional `ChangePayload` JSON serialization support. `Fragment` has no dedicated JSON converter. See [optional JSON support](change-payload.md#optional-json-support). |

## Name collisions

An existing member named Observable or EditSession changes the corresponding companion type name to SparseObservable or SparseEditSession. Patch whole-operation names (`Set`, `SetNull`, `Remove`, `IsEmpty`) and algebra names (`Between`, `Compose`, `Invert`, `Rebase`) can gain a Sparse prefix to avoid member collisions. Reference pages use the ordinary, non-colliding names.

Names that cannot coexist with generated companions produce [SPF009](analyzer.md#spf009-member-conflicts-with-generated-api). Do not infer every generated helper name from the model name.

For declaration constraints and supported language versions, see [model shapes](model-shapes.md).
