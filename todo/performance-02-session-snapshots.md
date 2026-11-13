# EditSession snapshot sharing

## Findings and plan

The current implementation already shares a captured state query view across repeated field reads. `SparseEditContext.TryGetStateSnapshot` retains one view per session version and model instance, while `SparseChangeSetQueryView` flattens and indexes its captured change set once. `RefreshStateSnapshotForParameterUpdate` captures the live model again and compares the new records before retaining the old view, so raw model edits are detected on parameter refresh. `InvalidateStateSnapshot` is available for raw edits outside that lifecycle.

I will measure the existing live-query and shared-snapshot paths at small and large model sizes, then run the focused snapshot-sharing tests. I will not add another cache: caching across session versions would miss observable edits, and caching across raw-model edits would require invalidation that the runtime cannot infer.

## Completion

No runtime change was needed. `SparseEditContext.TryGetStateSnapshot` reuses a view only while both the session version and live model reference match (`src/SparseTypes.Blazor/Sessions/SparseEditContext.cs:153-218`). Parameter refresh captures from the live model and compares the new records before retaining the prior view (`:226-271`); unnotified edits outside that lifecycle require `InvalidateStateSnapshot` (`:221-224`). `SparseChangeSetQueryView` retains the captured set, lazily flattens it once, and builds its segment index on the first query (`src/SparseTypes/State/SparseChangeSetQueryView.cs:15-39, 67-85, 140-143, 196-222`).

The existing Blazor tests cover view identity within a generation, invalidation after session edits, and raw scalar, nested replacement, keyed reorder, and delete detection on parameter refresh (`tests/SparseTypes.Blazor.Tests/SparseSnapshotSharingTests.cs:19-344`). I added `EditSessionSnapshotSharingBenchmarks.cs` to compare one fresh full-model capture per field with one retained indexed snapshot over a model containing 1,000 items. The invalid benchmark call was corrected to `CaptureStateSnapshot`.

Verification:

- CSharpier check passed all five files included in the check command.
- `dotnet build tests/SparseTypes.Blazor.Tests/SparseTypes.Blazor.Tests.csproj --configuration Release --no-restore -nodeReuse:false /p:UseSharedCompilation=false` succeeded with 0 warnings and 0 errors.
- `dotnet tests/SparseTypes.Blazor.Tests/bin/Release/net10.0/SparseTypes.Blazor.Tests.dll --treenode-filter "/*/*/SparseSnapshotSharingTests/*"` passed 5/5.
- BDN `short` (3 warmups, 3 measurements), model with 1,000 items:

| Fields | Fresh capture per field | One shared snapshot | Time ratio | Fresh allocation | Shared allocation |
|---:|---:|---:|---:|---:|---:|
| 1 | 54.76 µs | 52.69 µs | 0.96× | 204.94 KB | 204.88 KB |
| 10 | 518.28 µs | 54.63 µs | 0.11× | 2,049.38 KB | 204.88 KB |
| 100 | 5,325.30 µs | 54.41 µs | 0.01× | 20,493.75 KB | 204.88 KB |

Command: `dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release --no-build --no-restore -- --filter "*EditSessionSnapshotSharingBenchmarks*" --job short --artifacts artifacts/performance-all/state-bdn/snapshot-short`. This confirms existing generation-level snapshot reuse: one capture has nearly constant cost as field count grows, while fresh capture per field scales linearly. No runtime change was needed.
