# 01 Keyed patch construction and conversion

## Initial inspection

`SparseKeyedSequenceApplyEmitter.EmitKeyedBetween` in `src/SparseTypes.Generator.Shared/Emitters/Patch/SparseKeyedSequenceApplyEmitter.cs` builds before/after dictionaries and order lists, then derives removals, additions, and edits with keyed lookups. Its main loops are linear in the collection sizes; the current implementation does not support a blanket claim that keyed diffing is quadratic.

The shared `SparseKeyedCollectionEmitter.AddUniqueEntry` helper in `src/SparseTypes.Generator.Shared/Emitters/Patch/SparseKeyedCollectionEmitter.cs` checks `ContainsKey` before `Add`. That performs two hash-table probes for each accepted keyed item. `FromPatch` in `SparseChangeSetPatchSyncEmitter.AppendFromPatch` applies the patch to the baseline and calls `Between`, so granular keyed conversion materializes the result and then derives a second keyed diff.

## Repair plan

Measure keyed `Between`, `Apply`, and `FromPatch` at small and large sizes using the existing keyed benchmarks. Replace the duplicate-check/add pair with a single comparer-aware insertion operation where target framework compatibility permits. Evaluate direct keyed `FromPatch` conversion against the current apply-then-diff route; preserve the public API and all duplicate, null, comparer, order, temporary-identity, and exception behavior. Add focused tests only for changed behavior, and compare time and allocations before and after.

## Disposition

### Closed: retain the portable implementation

The proposed `Dictionary.TryAdd` optimization was tested and rejected. The generated consumer probe at `artifacts/performance-all/netstandard-keyed-probe/` targets `netstandard2.0`; its build failed at 12 generated call sites with CS1061 because `Dictionary<TKey,TValue>.TryAdd` is not in that reference surface. A per-call `#if` fallback compiled the probe, but expanded every generated insertion and was rejected in review; it also required careful statement-boundary handling in the string emitter. `TryAdd` evaluates the value argument before checking for a duplicate, whereas the current generated `ContainsKey` guard does not evaluate the value expression on the duplicate path. Keeping the current implementation preserves both framework compatibility and evaluation order.

The actual source remains `SparseKeyedCollectionEmitter.AddUniqueEntry` / `AddUniqueTempEntry` and the add path in `SparseKeyedSequenceApplyEmitter.EmitKeyedApply`; there is no production diff for this topic. The keyed comparison maps still use comparer-aware dictionaries and linear passes, so there is no evidence for a quadratic keyed diff.

Regression checks against the retained implementation:

- `dotnet build artifacts/performance-all/netstandard-keyed-probe/NetStandardKeyedProbe.csproj --configuration Release --no-restore -nodeReuse:false /p:UseSharedCompilation=false` — passed after restoring the portable emission.
- `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/SparseTypes.Tests/KeyedAlignedBetweenTests/*" --show-test-results passed` — 4/4 passed, including duplicate-key and null-key behavior.
- `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/SparseTypes.Tests/KeyedCollectionPatchTests/*" --show-test-results none` — 20/20 passed.

The Size=2048 ShortRun reference and current run are:

| Shape | Before mean / allocation | Current mean / allocation |
|---|---:|---:|
| AllEdits | 98.469 µs / 528.47 KB | 99.68 µs / 528.47 KB |
| OneEdit | 88.267 µs / 336.4 KB | 85.045 µs / 336.4 KB |

Both current runs used BenchmarkDotNet ShortRun (3 iterations). The allocations are identical, and the timing differences (about +1.2% and -3.7%) are within the short-run noise; no speedup is attributed because the portable implementation was retained unchanged. The current runs are in `artifacts/performance-all/keyed-between-after-all-edits.log` and `keyed-between-after-one-edit.log`; the original runs are in `keyed-between-before-all-edits.log` and `keyed-between-before-one-edit.log`. A real single-probe optimization would require generation-time target API capability plumbing (or a new generated helper contract) and a separate evaluation-order design; that larger change is outside a safe surgical fix.

Added a uniquely named `KeyedFromPatchBenchmarks` in `benchmarks/SparseTypes.Benchmarks/KeyedFromPatchBenchmarks.cs` to measure the conversion path at 16 and 2,048 items for one-edit and all-edits cases. Its setup verifies that `FromPatch().ToPatch().Apply(baseline)` reaches the requested endpoint. Command: `dotnet run --no-build --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release -- --filter "*KeyedFromPatchBenchmarks*" --job short`. Current ShortRun results:

| Size | Shape | Mean | Allocated |
|---:|---|---:|---:|
| 16 | OneEdit | 989.3 ns | 4.42 KB |
| 16 | AllEdits | 1.909 µs | 8.76 KB |
| 2,048 | OneEdit | 157.489 µs | 563.3 KB |
| 2,048 | AllEdits | 428.934 µs | 1,155.17 KB |

The short-run confidence margins are wide, so these are scale/cost reference points, not a basis for a timing claim. This measures the generated `FromPatch` path, which applies the patch to the baseline and derives a `Between` change set; it does not establish that a direct conversion is worthwhile. No `FromPatch` implementation change was retained, so there is no distinct before/after measurement to report. The output is in `artifacts/performance-all/keyed-from-patch-after.log`; the benchmark project Release build and CSharpier check passed.
