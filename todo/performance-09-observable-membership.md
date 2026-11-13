# 09 ObservableList membership and pruning

## Findings

`SparseObservableList` already maintains reference-identity membership counts for models received through `INotifyCollectionChanged`. `RebuildMembershipIndex` and `UpdateMembership` keep counts current, and `Model` plus `UnsafeModel` invalidate the index so pruning falls back to scanning live models (`src/SparseTypes/Collections/SparseObservableList.cs`, membership-index helpers).

Bulk removal had a separate allocation: `PruneItems` built a reference-keyed set of removed models, then checked each key against the membership index or a live-set snapshot. The set was redundant. A proxy removed for an absent model is detached on its first check, and the index or live snapshot preserves a proxy while any duplicate reference remains.

`PruneItems` now checks old items directly against the valid membership index. When the index is invalid, it lazily builds one live-reference set after the first non-null item and checks each removed model against that set. Empty and all-null ranges do not build a live set. This retains duplicate-reference handling and the `UnsafeModel` fallback while removing the temporary removed-key set.

`tests/SparseTypes.Tests/Patch/ObservableProxyRegressionTests.cs` adds regressions for removing duplicate references together, retaining a duplicate still present after a range removal, and pruning after raw model access. The tests verify proxy attachment and notification counts.

## Measurements

BenchmarkDotNet ShortRun (.NET 10.0.12, three warmups and three measured iterations) compared the removed-key-set algorithm with direct pruning. The matrix covered model counts 512 and 4,096, removal batches 8, 128, and 512, repeated and unique references, and valid and invalid membership indexes. Full output is in `artifacts/performance-all/collection-bdn.log`.

| Models | Batch | References | Index | Removed-key set | Direct pruning |
| ---: | ---: | --- | --- | ---: | ---: |
| 4,096 | 512 | unique | valid | 9.35 µs / 34,480 B | 4.52 µs / 0 B |
| 4,096 | 512 | unique | invalid | 43.51 µs / 188,664 B | 40.90 µs / 154,184 B |
| 512 | 8 | repeated | invalid | 67.0 ns / 352 B | 49.7 ns / 176 B |

The valid-index unique-reference case removes the temporary set and approximately halves measured time. The invalid-index case still allocates its necessary live snapshot; direct pruning avoids the second set and reduces allocation. Measurements for small repeated batches are short-run estimates.

## Validation

- `dotnet build tests/SparseTypes.Tests/SparseTypes.Tests.csproj -c Debug --no-restore -p:MSBuildNodeReuse=false -nr:false`: passed, zero warnings and errors.
- The direct TUnit invocation ran the nine focused path and observable-list cases from `artifacts/performance-all/collection-focused-uids.txt`: 9 passed, 0 failed.
- Benchmark command: `dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj -c Release --no-restore -p:MSBuildNodeReuse=false -- --filter "*SparseObservableMembershipBenchmarks*" --job short`. Results are in `artifacts/performance-all/collection-bdn.log`.
- `git diff --check`: passed.
