# T.ChangeSet

`T.ChangeSet` is an immutable before-to-after transition nested in a generated model. Create it through diff/capture factories rather than a public constructor. It implements `ISparseChangeSet<T>`.

## Factories and properties

Here `state` means `Optional<T.Fragment?>`. Both model and sparse-state overloads are available where listed.

| Member | Contract |
| --- | --- |
| `static ChangeSet Empty` | No transition. |
| `bool IsEmpty`, `bool IsChanged` | Complementary aggregate flags. |
| `DateTimeOffset? RecordedAt` | Optional time this transition was recorded. |
| `ChangeSet WithRecordedAt(DateTimeOffset? recordedAt)` | Returns a copy with a replacement recording time; `null` removes it. |
| Model-specific properties | Read-only scalar, nested, keyed, dictionary, or set transitions. |
| `static ChangeSet Between(T before, T after)` | Derives transitions from models and records the current UTC time. |
| `static ChangeSet Between(T before, T after, DateTimeOffset? recordedAt)` | Derives transitions with an explicit recording time, including `null`. |
| `static ChangeSet Between(state before, state after)` | Also represents missing/null root and member endpoints; records the current UTC time. |
| `static ChangeSet Between(state before, state after, DateTimeOffset? recordedAt)` | Presence-aware equivalent with an explicit recording time. |
| `static ChangeSet FromPatch(T baseline, T.Patch patch)` | Attaches a known baseline to commands. |
| `static ChangeSet FromPatch(state baseline, T.Patch patch)` | Presence-aware equivalent. |
| `static ChangeSet FromPayload(T.ChangePayload payload)` | Converts a fully known transition envelope. |

An unchanged scalar's Before and After are missing because there is no transition. They are not a request to remove the model property.

The timestamp is metadata for the whole transition; it does not affect emptiness, comparisons, conflicts, or patch application. Explicit overloads preserve the supplied offset and treat `null` as unknown. The shared `ChangeSet.Empty` has no timestamp, while a newly computed empty diff records the current UTC time.

## Conversion, composition, and inspection

| Member | Contract |
| --- | --- |
| `T.Patch ToPatch()` | Projects desired commands, discarding baseline checks. |
| `T.ChangePayload ToPayload()` | Exports the typed transport envelope, applying redaction rules. |
| `ChangeSet Invert()` | Reverses before and after. |
| `ChangeSet Compose(ChangeSet next)` | Combines sequential, continuous transitions. Inconsistent intermediate endpoints are rejected. |
| `static ChangeSet Compose(ChangeSet first, ChangeSet second)` | Static equivalent. |
| `state ApplyToBaseline(state baseline)` | Applies the transition to its expected baseline. |
| `IEnumerable<SparseChangeRecord> EnumerateChanges()` | Flattens transitions into deterministic rows. |
| `IReadOnlyList<SparsePath> EnumerateChangedPaths()` | Lists changed paths. |
| `SparseChangeRecord? Find(SparsePath path)` | Exact flattened-record lookup, or null. |
| `SparseChangeRecord? Find<TValue>(SparsePath<T,TValue> path)` | Typed-path equivalent. |

`SparseChangeRecord` is in `SparseTypes.Generated`; it exposes Path, PathText, Before, After, and Kind. Its constructor accepts path, presence-aware boxed endpoints, and SparseChangeKind. Kind is Changed, Added, Removed, or Order. Some model member names collide with convenience enumeration names; the `ISparseChangeSet` contract remains the generic inspection entry point.

`Invert()` retains the source timestamp. `Compose(next)` uses `next.RecordedAt`, even when `next` is empty or undated. `RebaseOnto(...)` retains the source timestamp for successful, empty, and conflicting results.

`ToPayload()` preserves changes to `[JsonIgnore]` members in the neutral payload; the JSON codec rejects serializing such a payload before writing bytes. Changes to `[SparseTransportIgnore]` members are rejected during payload export. Do not serialize ChangeSet directly as a substitute for `ToPayload()`.

## Rebase and application

| Member | Contract |
| --- | --- |
| `RebaseResult<ChangeSet> RebaseOnto(T current, options = null)` | Reconciles against current without modifying it. |
| `RebaseResult<ChangeSet> RebaseOnto(state current, options = null)` | Presence-aware equivalent. |
| `bool TryApplyTo(T current, out T? updated, options = null)` | Rebases and returns a new model only on success. |
| `bool TryApplyTo(T current, out T? updated, out IReadOnlyList<SparseConflict>? conflicts, options = null)` | Also reports structured conflicts. |
| `bool TryApplyInPlace(T current, out IReadOnlyList<SparseConflict>? conflicts, options = null)` | Rebases and writes supported members into current. |
| `void ApplyInPlace(T current, options = null)` | Throwing equivalent; conflicts or immutable changed members cause InvalidOperationException. |
| `static Resolution BeginResolution(RebaseResult<ChangeSet> rebase, T current)` | Starts explicit conflict decisions. |
| `static Resolution BeginResolution(RebaseResult<ChangeSet> rebase, state current)` | Presence-aware equivalent. |

Every `options` parameter above is `ChangePayloadRebaseOptions?`. TryApplyTo does not mutate its input. In-place methods retain the root instance; unsupported immutable writes report InPlaceWriteUnavailable. Whole-root presence transitions that cannot yield a non-null model require the optional Fragment APIs.

`Resolution` also has constructors matching BeginResolution and exposes `Current` as optional Fragment state; inherited decisions are documented in [rebase API](rebase-api.md).

## Generic inspection contract

`ISparseChangeSet` exposes IsEmpty, IsChanged, and `EnumerateChangeRecords()`. Shape-specific Try methods take a CLR member name and an out result: `TryGetValueChange<TValue>`, `TryGetNestedChange`, `TryGetKeyedChange<TKey,TElement,TElementChange>`, and `TryGetDictionaryChange<TKey,TValue,TEdited>`. They return false for missing members or incompatible shapes/types.

See [state queries](edit-session-state.md) for FieldState and cached query views, and [generated transitions](generated-paths-and-transitions.md) for typed member details.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-changeset -->
```csharp
var before = new ReferenceSettings { Theme = "light" };
var after = new ReferenceSettings { Theme = "dark" };
var changes = ReferenceSettings.ChangeSet.Between(before, after);

var oldTheme = changes.Theme.Before.Value;
var newTheme = changes.Theme.After.Value;
var undo = changes.Invert();
var roundTrip = changes.Compose(undo);
```
<!-- /sample -->

The endpoints are `"light"` and `"dark"`. `roundTrip.IsEmpty` is true.
