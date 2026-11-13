# ValidationStore query indexing

## Findings and plan

`SparseValidationStore` already indexes paths by typed root and display-text trie. Typed subtree lookups still collect candidates into a new list and sort them on every query. Named `GetErrors` repeats that selection and sort, and all-errors queries repeatedly sort the complete path set. Exact errors are read from the live origin buckets after path selection.

I will add bounded, single-query caches for typed subtree results, named text-query results, and the globally sorted path list. Path-index additions, removals, and clears will invalidate those caches. Replacing messages at an existing path will leave path selection intact, while error values will still be read from live buckets. Focused tests will cover fresh values after replacement and correct ordering after structural changes. A separate benchmark will exercise repeated sparse-subtree queries at realistic store sizes.

## Completion

Implemented bounded query-result caches in `SparseValidationStore`: fields at `src/SparseTypes/Validation/SparseValidationStore.cs:31-35`, invalidation after index add/remove/clear at `:612, 642, 679`, typed and text result selection at `:699-759`, invalidation clears all retained query state (including resetting `_cachedTypedQueryPath` to `default`) at `:762-769`, and sorted global path caching at `:780-789`. `GetPathErrors`, `GetAllErrors`, and `GetErrors` still build fresh outputs from live buckets (`:364-380, 387-405, 417-460`); only path-entry lists are retained.

Added `RepeatedQueriesRefreshValuesAndPathOrderAfterMutations` at `tests/SparseTypes.Tests/Validation/SparseValidationStoreTests.cs:510-538`, which warms all three caches, replaces errors at an existing path, and adds/removes paths. The benchmark matrix is at `benchmarks/SparseTypes.Benchmarks/ValidationStoreRepeatedQueryBenchmarks.cs:7-72`, with 256 and 4,096 paths and 32 repeated reads per operation. CSharpier check passed all five files in the check command. `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/*/SparseValidationStoreTests/*"` passed 23/23, including the new mutation regression. A VSTest-style `dotnet test --filter FullyQualifiedName~...` invocation reported zero TUnit tests; direct TUnit tree filtering is the working runner command.

Release ShortRun comparison (3 warmups, 3 measurements; source baseline was `git show HEAD:src/SparseTypes/Validation/SparseValidationStore.cs`):

| Query / paths | Before | After | Time change | Allocated before | Allocated after |
|---|---:|---:|---:|---:|---:|
| typed subtree / 256 | 33.75 µs | 34.08 µs | +1.0% | 85 KB | 68.5 KB |
| property-name subtree / 256 | 67.08 µs | 35.87 µs | −46.5% | 173 KB | 80.5 KB |
| all errors / 256 | 711.82 µs | 533.77 µs | −25.0% | 902.25 KB | 836.5 KB |
| typed subtree / 4,096 | 680.54 µs | 626.27 µs | −8.0% | 1,243 KB | 1,103.5 KB |
| property-name subtree / 4,096 | 1,291.14 µs | 482.10 µs | −62.7% | 2,761.75 KB | 1,115.5 KB |
| all errors / 4,096 | 19,294.58 µs | 13,298.89 µs | −31.1% | 14,343.53 KB | 13,316.81 KB |

The commands were `dotnet build benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release --no-restore -nodeReuse:false /p:UseSharedCompilation=false`, followed by `dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release --no-build --no-restore -- --filter "*ValidationStoreRepeatedQueryBenchmarks*" --job short --artifacts artifacts/performance-all/state-bdn/validation-before-short` and the matching `validation-after-short` run after restoring the cache implementation. Results support keeping the bounded caches: the largest gains are repeated name-query selection and sorted all-error enumeration; the small typed query is effectively flat in time but allocates less.

Follow-up retention fix: `InvalidateQueryCachesLocked` now resets the cached `SparsePath` as well as nulling its entries. `dotnet tool run csharpier -- check src/SparseTypes/Validation/SparseValidationStore.cs` passed; `dotnet build tests/SparseTypes.Tests/SparseTypes.Tests.csproj --configuration Release --no-restore -nodeReuse:false /p:UseSharedCompilation=false` succeeded with 0 warnings/errors; `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/*/SparseValidationStoreTests/*"` passed 23/23 after the change.
