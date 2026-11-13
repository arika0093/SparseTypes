# Runtime performance and correctness: initial batch

## Scope

This batch changes `SparseObservableList` and focused tests. The generator and shared emitter are outside this batch. The work addresses one copy-time hotspot and one null-lookup failure; it does not attempt to complete the runtime audit.

## Changes and evidence

### Non-generic `CopyTo` uses typed staging for exact view arrays

Before the change, `ICollection.CopyTo(Array, int)` allocated an `object?[]`, wrapped every element into it, checked the full array with `CanStore`, and used `Array.SetValue` for the copy (`src/SparseTypes/Collections/SparseObservableList.cs:333-363`). For an exact `TView[]`, the `CanStore` checks are redundant because each `Wrap` result is a `TView`, and the final element-by-element `Array.SetValue` can be replaced with `Array.Copy`. The method now stages into a typed array and uses `Array.Copy` after the existing rank, lower-bound, and bounds checks. Other runtime array types retain the original preflight and copy path; the exact runtime-type check also avoids changing covariant-array behavior.

The direct-write version could partially change the destination when a later wrap failed. The public constructor accepts an arbitrary `Func<TModel, Action, TView>` (`SparseObservableList.cs:37-45`), so callers can supply a delegate that throws. The docs do not promise atomicity on every exception, but the prior implementation staged all wrapped values before writing, and the existing contract tests check that invalid copies leave the destination unchanged (`SparseObservableCopyToContractTests.cs:11-40`). I retained that observable behavior: the exact-array branch now stages in `TView[]` and copies with `Array.Copy`. A regression test throws on the second wrapped item and verifies the destination remains untouched (`SparseObservableCopyToContractTests.cs:53-68`).

`SparseObservableCopyToContractTests.cs:25-68` also covers incompatible destinations, the existing `object[]` path, and exact `TView[]` copying. The measured workload is in `benchmarks/SparseTypes.Benchmarks/SparseObservableListCopyBenchmarks.cs`.

BenchmarkDotNet ShortRun, .NET 10.0, `Size = 16, 256, 2048`; the before column measures the original implementation, and after measures typed staging plus `Array.Copy`:

| Size | Before mean | After mean | Before allocation | After allocation |
|---:|---:|---:|---:|---:|
| 16 | 147.4 ns | 112.6 ns | 816 B | 816 B |
| 256 | 1,955.2 ns | 1,470.7 ns | 12,336 B | 12,336 B |
| 2,048 | 16,565.1 ns | 11,470.7 ns | 98,352 B | 98,352 B |

Allocation did not change: the typed staging array has the same size as the prior `object?[]` for these reference values. The measured mean was lower at each size, but ShortRun confidence intervals were wide, so the timing results are directional rather than a precise speedup claim. The direct-write measurements from the first attempt are excluded because they did not preserve the previous failure behavior.

### Null view lookup no longer invokes a null-unsafe unwrap

`IndexOf(TView)` previously called `_unwrap(item)` unconditionally (`src/SparseTypes/Collections/SparseObservableList.cs:208-219`). The non-generic `IList` adapter accepts null for nullable view types (`src/SparseTypes/Collections/SparseObservableList.cs:291-313`), but an unwrap delegate for a proxy can dereference its input. The existing null lookup test used identity unwrap only (`tests/SparseTypes.Tests/Collections/SparseObservableListNullLookupTests.cs:17-34`).

The new test reproduced a `NullReferenceException` from `_unwrap` for `IndexOf(null)` when the model list contained null. The fix skips `_unwrap` for null view values, searches for the null/default model value when `TModel` permits null, and returns no match for a non-nullable model type. The regression cases cover generic and non-generic lookup, `Contains`, and `Remove`, including the non-nullable model case (`tests/SparseTypes.Tests/Collections/SparseObservableListNullLookupTests.cs:37-72`).

## Validation

- Reproduction before the fix: `dotnet run --project tests/SparseTypes.Tests/SparseTypes.Tests.csproj -c Release --no-build -- --filter-uid 'SparseTypes.Tests.SparseObservableListNullLookupTests.1.1.NullViewLookup_DoesNotInvokeNullUnsafeUnwrap.1.1.0'` failed with `NullReferenceException` at `IndexOf`.
- Build after the atomicity follow-up: `dotnet build tests/SparseTypes.Tests/SparseTypes.Tests.csproj -c Release --no-restore` succeeded with 0 warnings and 0 errors.
- Focused TUnit run: `dotnet run --project tests/SparseTypes.Tests/SparseTypes.Tests.csproj -c Release --no-build -- --filter-uid 'SparseTypes.Tests.SparseObservableListNullLookupTests.1.1.NonGenericLookup_FindsAndRemovesNullForNullableViews.1.1.0' --filter-uid 'SparseTypes.Tests.SparseObservableListNullLookupTests.1.1.NullViewLookup_DoesNotInvokeNullUnsafeUnwrap.1.1.0' --filter-uid 'SparseTypes.Tests.SparseObservableListNullLookupTests.1.1.NullViewLookup_DoesNotMatchDefaultOfNonNullableModel.1.1.0' --filter-uid 'SparseTypes.Tests.SparseObservableListNullLookupTests.1.1.NonGenericLookup_RejectsNullForNonNullableValueViews.1.1.0' --filter-uid 'SparseTypes.Tests.SparseObservableCopyToContractTests.1.1.NonGenericCopyTo_RejectsInvalidRankLowerBoundAndType.1.1.0' --filter-uid 'SparseTypes.Tests.SparseObservableCopyToContractTests.1.1.NonGenericCopyTo_CopiesIntoExactViewArray.1.1.0' --filter-uid 'SparseTypes.Tests.SparseObservableCopyToContractTests.1.1.NonGenericCopyTo_DoesNotMutateDestinationWhenWrappingFails.1.1.0'`; all seven passed.
- Formatting: `dotnet tool run csharpier -- check src/SparseTypes/Collections/SparseObservableList.cs tests/SparseTypes.Tests/Collections/SparseObservableCopyToContractTests.cs tests/SparseTypes.Tests/Collections/SparseObservableListNullLookupTests.cs benchmarks/SparseTypes.Benchmarks/SparseObservableListCopyBenchmarks.cs` passed.
- Before and after benchmark command: `dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj -c Release --no-restore -- --filter '*SparseObservableListCopyBenchmarks*' --job short`.
- No dependencies were installed. No commit or push was made.

## Deferred verified candidates

- `SparsePath.Append(SparsePathSegment)` allocates a new array and copies the current segments on every append (`src/SparseTypes/Paths/SparsePath.cs:117-125`). Repeated construction therefore copies a quadratic total number of segments in path depth. Reducing this safely needs a representation or construction API change because paths are immutable; no benchmark was added in this runtime-only list batch.
- `SparseObservableList.PruneItems` builds a reference-keyed `HashSet` for range removals and, when the membership index is invalid, builds a live set (`src/SparseTypes/Collections/SparseObservableList.cs:426-460`, `694-704`). It avoids repeated scans and preserves duplicate-reference behavior. Defer changes until a range-removal benchmark can show whether this temporary allocation is material.

## Limits

The benchmark uses a short three-iteration run and an exact `string[]` destination. It does not measure covariant or incompatible destinations, event-heavy lists, other view types, or GC behavior under concurrent load. Their functional contract remains covered by the focused tests.
