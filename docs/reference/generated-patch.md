# T.Patch

`T.Patch` is a sealed mutable command type nested in a generated model. It has no retained baseline and does not detect concurrent overwrites by itself.

## Construction and member commands

| Member | Contract |
| --- | --- |
| `Patch()` | Empty command. |
| `Patch(T.Fragment fragment)` | Creates Set commands for supplied contributions. |
| `bool IsEmpty` | Whether neither whole-root nor member operations request changes. |
| Scalar member | Ref-returning `FragmentOperation<TValue>` supporting assignment, Set, Keep, and Remove. |
| Nested model member | Child Patch for member-level edits and whole-child operations. |
| Structural collection member | Shape-specific command supporting keyed or dictionary mutation. |
| `void Set(T value)` | Replaces the whole contribution with a captured model. |
| `void SetNull()` | Makes the whole contribution present null. |
| `void Remove()` | Removes the whole contribution. |
| Implicit conversion from `FragmentOperation<T.Fragment?>` | Wraps a whole-root command. |

`patch.Name = value` is a Set command. `patch.Name.Remove()` removes the member from the source contribution; Keep leaves it untouched. See [operation value types](values-and-operations.md).

## Application and algebra

Here `state` means `Optional<T.Fragment?>`, including whole-root missing and null states.

| Member | Result and behavior |
| --- | --- |
| `state Apply(state current)` | Returns the resulting presence-aware sparse state. |
| `T ApplyTo(T current)` | Returns a model result without modifying current. |
| `ApplyInPlaceResult ApplyInPlace(T current)` | Attempts writes to the existing model and reports unsupported members. |
| `static Patch Between(state before, state after)` | Derives commands, including removal, between sparse endpoints. |
| `Patch Compose(Patch next)` | Sequential composition: this, then next. |
| `static Patch Compose(Patch first, Patch second)` | Equivalent static form. |
| `Patch Invert(state baseline)` | Derives an inverse using the supplied baseline. |
| `static RebaseResult<Patch> Rebase(state baseState, Patch local, state currentState, ChangePayloadRebaseOptions? options = null)` | Three-way rebase with an explicit baseline. |

The notation `state` above is explanatory, not a C# type alias.

`ApplyInPlaceResult` exposes `bool Succeeded` and `IReadOnlyList<string> UnsupportedMembers`. Immutable members can prevent in-place application even when ApplyTo can construct a new model. Inspect the result before treating the operation as applied. Model-returning APIs cannot represent a missing or null model root; use Apply with optional fragments for those transitions.

Composition does not invent before-state. Invert needs the actual baseline from which the patch was applied. For retained before-values and baseline checks, use [T.ChangeSet](generated-changeset.md).

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-patch -->
```csharp
var layer = new ReferenceSettings.Fragment { Theme = "dark", RetryCount = 3 };
var patch = new ReferenceSettings.Patch();
patch.Theme.Remove();
patch.RetryCount.Set(5);

var updated = layer.Apply(patch);
var undo = patch.Invert(Optional<ReferenceSettings.Fragment?>.Present(layer));
var restored = updated.Apply(undo);
```
<!-- /sample -->

`updated.Theme` is missing and RetryCount is 5. Applying undo restores the original contribution, including Theme `"dark"`.

Collection commands vary by shape; see [generated paths and transitions](generated-paths-and-transitions.md) and [keyed collection semantics](../explanation/keyed-collections.md).
