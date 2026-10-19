# Optional and fragment operations

Package and namespace: `SparseTypes`. These value types distinguish presence from value and distinguish a stored value from a command to change it.

## Optional<T>

`Optional<T>` is a readonly record struct. Its default value is missing. `Present(null)` is present, as is `Present(0)`; neither is equivalent to missing.

| Member | Result or effect |
| --- | --- |
| `Missing` | Static missing value; equivalent to `default(Optional<T>)`. |
| `Present(T? value)` | Static factory for a present value, including null or the type's default. |
| Implicit conversion from `T?` | Same as `Present(value)`. |
| `bool IsPresent` | Whether a value was supplied. |
| `T? Value` | Stored value; throws `InvalidOperationException` when missing. |
| `T? GetValueOrDefault(T? fallback = default)` | Returns the stored value when present, including null; uses the fallback only when missing. |
| `void CopyTo(ref Optional<T> destination)` | Copies presence and value. Does not clone a referenced object. |
| `string ToString()` | `Missing` or `Present(...)`. Not a wire format. |
| Equality, `==`, `!=` | Record value equality. Does not perform a generated model diff. |

`OptionalExtensions` supplies ref mutation helpers for variables and ref-returning builder members:

| Extension | Effect |
| --- | --- |
| `Set(value)` | Replaces storage with a present value. |
| `Remove()` | Replaces storage with missing. |
| `CopyFrom(source)` | Copies presence and value. |
| `CopyFrom<TSource, TDestination>(source, map)` | Preserves missing; calls `map` only for present values, including present null. A null mapper throws `ArgumentNullException`. |

## FragmentOperation<T>

`FragmentOperation<T>` is a readonly struct. Its default value is `Keep`.

| Member | Result or effect |
| --- | --- |
| `Keep` | Static command that leaves the member unchanged. |
| `Set(T? value)` | Static command that makes the member present, including null or default. Stores references without cloning. |
| `Remove` | Static command that removes the source contribution. |
| Implicit conversion from `T?` | Same as `Set(value)`. |
| `FragmentOperationKind Kind` | `Keep`, `Set`, or `Remove`. |
| `T? Value` | Value of a Set command; throws `InvalidOperationException` for Keep or Remove. |
| `Optional<T> Apply(Optional<T> current)` | Keep returns current; Set returns present; Remove returns missing. Does not mutate current. |

`FragmentOperationExtensions` supplies ref helpers `Keep()`, `Set(value)`, `Remove()`, `CopyFrom(source)`, and `CopyFrom<TSource, TDestination>(source, map)`. Conversion preserves Keep and Remove and maps only Set values.

Removing a member from an override layer lets a lower layer contribute it after merging. It does not mean assigning null to the effective model.

## Example

<!-- sample: reference-optional -->
```csharp
var missing = Optional<string?>.Missing;
var explicitNull = Optional<string?>.Present(null);
Optional<int> zero = 0;

var fallback = missing.GetValueOrDefault("fallback");
var stillNull = explicitNull.GetValueOrDefault("fallback");
var operation = FragmentOperation<string?>.Set(null);
var applied = operation.Apply(Optional<string?>.Missing);
```
<!-- /sample -->

`fallback` is `"fallback"`, `stillNull` is null, `zero.IsPresent` is true, and `applied` is present null.

See [T.Fragment](generated-fragment.md) for storage and [T.Patch](generated-patch.md) for model-specific commands.
