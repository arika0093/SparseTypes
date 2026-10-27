# T.Fragment and its builder

`T.Fragment` is a sealed partial-snapshot type nested in a generated model. It stores which members are present. Missing and present null are distinct.

## Construction and members

| Member | Contract |
| --- | --- |
| `Fragment()` | Empty snapshot without an origin label. |
| `Fragment(string? origin)` | Empty snapshot with an origin label. |
| `static Fragment Empty` | Snapshot with no present members. |
| `bool IsEmpty` | Whether no member contributes a value. |
| `string? Origin` | Fragment-level source label. |
| Model-specific init properties | Scalars use `Optional<TValue>`; nested generated values use child Fragment types. Collections follow their analyzed shape. |
| `static Fragment From(T value)` | Captures all supported non-ignored members as present; clones supported mutable state. Null class input throws. |
| `static Fragment Diff(T before, T after)` | Captures changed after-values only. For explicit removals or full history, use ChangeSet or Patch. |

## Conversion and operations

| Member | Contract |
| --- | --- |
| `T ToModel()` | Constructs a model; missing members use the generated construction/default path. |
| `T ToModel(T baseline)` | Constructs a model using the baseline for omitted values. |
| `void WriteTo(T model)` | Writes present values into an existing model. Emitted only for supported writable shapes. |
| `Fragment Merge(Fragment higherPriority)` | Returns the layered result using configured member merge strategies. |
| `Fragment ApplyChanges(Fragment changes)` | Applies present changed values; omitted members stay unchanged. |
| `Fragment Apply(T.Patch patch)` | Returns the patched fragment. Use the presence-aware Patch.Apply API for whole-root null/removal. |
| `T.Patch ToPatch()` | Converts present contributions to commands; missing members are untouched, not removal commands. |
| `Fragment DeepClone()` | Clones supported mutable values. |
| `ToBuilder()` | Creates mutable builder storage initialized from this Fragment. |

Fragment instances expose init-only member storage, but storing a mutable reference does not freeze the referenced object. Direct initialization, merge, patch, and materialization do not all create independent graphs. Use From or DeepClone for ownership isolation; see [cloning and ownership](../explanation/cloning-and-ownership.md).

From and Diff reject unsupported structural cycles with a path-bearing `NotSupportedException`; DeepClone supports cycles.

## Origins

| Member | Contract |
| --- | --- |
| `string? GetOrigin(SparsePath path)` | Looks up the effective source label. |
| `bool TryGetOrigin(SparsePath path, out string? origin)` | Distinguishes absence of an origin entry from an entry whose label is null. |
| `IEnumerable<FragmentOriginEntry> EnumerateOrigins()` | Enumerates path/source entries. |
| `Fragment GetByOrigin(string? origin)` | Selects contributions belonging to a source. |
| `IReadOnlyList<FragmentOriginGroup<Fragment>> SplitByOrigin()` | Groups contributions by source. |

`FragmentOriginEntry` has init properties Path and Origin. `FragmentOriginGroup<TFragment>` has constructor `(string? origin, TFragment fragment)` and init properties Origin and Fragment.

## Builder

The generated `T.FragmentBuilder` has a parameterless constructor, ref-returning `Optional<...>` members, and `T.Fragment Build()`. Normally obtain it with `fragment.ToBuilder()`. Assigning Missing clears a builder member; assigning Present(null) keeps it present. Build creates a new Fragment; subsequent builder assignments do not change its member storage. Referenced mutable values can still be shared.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-fragment -->
```csharp
var lower = new ReferenceSettings.Fragment { Theme = "light", RetryCount = 3 };
var higher = new ReferenceSettings.Fragment { Theme = "dark" };
var effective = lower.Merge(higher);

var builder = effective.ToBuilder();
builder.Theme = Optional<string?>.Present(null);
var edited = builder.Build();
var model = effective.ToModel();
```
<!-- /sample -->

`model` has Theme `"dark"` and RetryCount 3. `edited.Theme` is present null, while effective retains `"dark"`.

See [layered settings](../how-to/layered-settings.md) for removing an override and revealing a lower layer.
