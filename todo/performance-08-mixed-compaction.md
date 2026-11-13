# 08 Mixed composition compaction

## Initial inspection

`MixedSequenceComposer.Compose` keeps removed staged ordinals in a list. When any entries were superseded, it allocates a `bool[]` the size of `staged`, marks removed positions, and performs one in-place compaction pass. This is linear, not quadratic, but allocates an additional array even when only a few staged entries are removed.

## Repair plan

Use the existing mixed composition benchmarks and add a focused benchmark shape for sparse and dense replacement if needed. Compare the current bitmap compaction with an alternative that avoids a full-size temporary array while preserving insertion order, first-overlap selection, failure and pending ordering, and the exact retained operations. Make the change only if measurements show a repeatable reduction at realistic sizes; retain the current implementation if alternatives add sorting, hashing, or bookkeeping that costs more.

## Disposition

### Closed: retain linear bitmap compaction

`MixedSequenceComposer.Compose` collects removed staged ordinals and uses one `bool[]` plus a stable in-place compaction pass. This is O(staged + removed) and preserves the staged order. Replacing it with `List.RemoveAt` would shift entries repeatedly; a sparse-index alternative would require sorting/branching, while a `HashSet<int>` adds substantially more per-index storage than the byte-per-slot bitmap. The current benchmark `MixedSequenceCompactionBenchmarks` exercises replacement and retained-entry ordering at sizes 16 and 512; no safer lower-cost compaction strategy was established from source inspection.

Regression checks:

- `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/SparseTypes.Tests.Mixed/MixedComposeSequencesIndexTests/*" --show-test-results none` — 27/27 passed, including first-overlap selection, insertion ordering, replacements, failures, and pending operations.

The implementation remains linear and unchanged. After the other benchmark source was stabilized, `MixedSequenceCompactionBenchmarks.Compose(Size: 512)` measured 414.819 µs and 2.05 MB allocated/op (ShortRun, 3 iterations; `artifacts/performance-all/mixed-compose-after.log`). The earlier failed attempt was due to concurrent edits of `SparsePathPrefixStorageBenchmarks.cs` and did not produce results. This current measurement does not establish a lower-cost replacement; no algorithm change is proposed.
