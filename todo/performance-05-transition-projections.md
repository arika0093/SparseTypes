# 05 Collection transition projections

## Initial inspection

`SparseChangeSetKeyedProjectionEmitter.AppendProjection` and `SparseChangeSetDictProjectionEmitter` project canonical sparse storage into typed transition views. Granular keyed projections count stored items in one pass to size `Added`, `Removed`, and `Edited`, then traverse the stored items again to fill them. Keyed transition getters cache granular projections; whole endpoints are rebuilt so their mutable collection containers remain independent snapshots.

`SparseChangeSetKeyedBuildEmitter.AppendBuild` handles whole before/after endpoints by building full keyed maps, order arrays, and item records. These are separate from the granular projection path and should not be optimized by weakening snapshot or identity semantics.

## Repair plan

Measure repeated transition-property access and first-access projection cost, including empty and large transitions. Remove a pass or allocation only when the replacement retains exact comparer, order, null, unassigned-key, temporary-key, duplicate, and snapshot behavior. Keep the existing caching boundary for mutable whole-value snapshots unless a test demonstrates an equivalent ownership contract.

## Disposition

### Closed: cached exact-capacity projection is retained

`SparseChangeSetKeyedProjectionEmitter.AppendProjection` and `SparseChangeSetDictProjectionEmitter.AppendProjection` count granular entries before constructing exact-capacity lists/dictionaries, then fill those containers in a second pass. This trades one extra linear read for avoiding over-sized transition arrays and repeated growth. The generated getter caches granular projections; whole-value getters intentionally rebuild independent snapshots. Replacing the count pass with maximum-capacity buffers would allocate for the full storage count in all three views and weaken the current allocation profile.

Regression checks:

- `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/SparseTypes.Tests/KeyedProjectionOwnershipTests/*" --show-test-results none` — 5/5 passed, including repeated lookup and detached mutable-view ownership.
- `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/SparseTypes.Tests/KeyedCollectionPatchTests/*" --show-test-results none` — 20/20 passed.

`KeyedTransitionProjectionBenchmarks.FirstView(Size: 2048, Shape: AllEdits)` measured 129.075 µs and 592.72 KB allocated/op (ShortRun, 3 iterations; `artifacts/performance-all/keyed-projection-after-first.log`). No projection source change was made, so this is the current baseline rather than an attributable before/after delta. The exact-capacity two-pass algorithm is retained because a one-pass replacement would trade a scan for three potentially oversized backing arrays; no measured workload justifies that trade.
