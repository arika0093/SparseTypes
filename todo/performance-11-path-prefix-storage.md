# 11 SparsePath prefix storage

## Findings and implementation

`SparsePath.Append(SparsePathSegment)` used to allocate and copy a full segment array on every append. Repeated construction to depth `d` therefore copied `d(d-1)/2` prior segments. Append-only paths now use immutable parent nodes and flatten lazily on reads. A materialized path appends by copying only its logical prefix into a fresh array, which disconnects the new value from cached arrays belonging to older prefixes.

The node representation initially retained every flattened prefix array when reads were interleaved with appends. A retention probe holding 24 final paths after full collections measured 102.0 MB at depth 512 for that strongly cached chain. The current representation materializes an array only for a read and copies from a materialized prefix on the next append. At depth 512 it retained 393,336 B, close to the 406,344 B array-copy baseline. Results by depth are in `artifacts/performance-all/path-retention-probe.log`.

Bulk suffix append uses a separate path. A non-root receiver copies the receiver prefix and suffix into one contiguous array instead of creating one node per suffix segment. A root receiver adopts the suffix's internal immutable segment array with a logical length and the receiver's root type. Public constructors still copy caller arrays, and `SparsePathSegments` does not expose a mutable array.

The tests in `tests/SparseTypes.Tests/Paths/SparsePathValueTests.cs` cover prefix views, appending after reads, caller-array detachment, concurrent first reads, and root/non-root bulk suffix append. The bulk test checks that `Append(SparsePath)` keeps the receiver root even when the suffix has a different root type.

## Measurements

BenchmarkDotNet ShortRun ran on .NET 10.0.12 with three warmups and three measured iterations. The path cases in `artifacts/performance-all/collection-bdn.log` compare final code with synthetic array-copy implementations; they are not actual before/after measurements.

| Workload | Depth | Final implementation | Synthetic array-copy comparison |
| --- | ---: | ---: | ---: |
| Append construction | 8 | 77.3 ns / 576 B | 800.9 ns / 1,344 B |
| Append construction | 128 | 1.21 µs / 9,216 B | 24.00 µs / 267,264 B |
| Append construction | 512 | 5.03 µs / 36,864 B | 218.48 µs / 4,214,784 B |
| Append then hash | 512 | 9.32 µs / 53,272 B | 218.80 µs / 4,214,784 B |
| Hash a materialized path | 8 | 20.79 ns / 0 B | 19.34 ns / 0 B |
| Enumerate a materialized path | 8 | 21.26 ns / 0 B | 19.47 ns / 0 B |
| Hash a materialized path | 512 | 1.629 µs / 0 B | 1.552 µs / 0 B |
| Enumerate a materialized path | 512 | 1.628 µs / 0 B | 1.563 µs / 0 B |
| Interleaved append/hash/enumeration/formatting | 512 | 1.967 ms / 7,134,512 B | 1.419 ms / 7,134,440 B |

Steady-state hash and enumeration remained within about 8% of the contiguous-array comparisons. The interleaved synthetic comparison's renderer handles members only, so its timing is not an isolated comparison of storage operations. The retention probe measures the storage consequence directly.

An identical-method comparison against actual `HEAD` resolves the interleaved timing concern and measures single-segment append at shallow depths. The detached baseline used `SparsePath.cs` from commit `3f62fab50369a4c3c580166e26a12cc0f8ed1b11`; the final run used the implementation in this working tree. Both builds used the same `SparsePathImplementationComparisonBenchmarks.cs` (verified identical), BenchmarkDotNet ShortRun, and .NET 10.0.12. The single-append method returns the resulting path so the JIT cannot eliminate the operation.

| Identical benchmark method | Depth | Actual HEAD | Final implementation |
| --- | ---: | ---: | ---: |
| `AppendOneSegment` | 1 | 109.7 ns / 88 B | 12.28 ns / 72 B |
| `AppendOneSegment` | 4 | 114.9 ns / 184 B | 12.15 ns / 72 B |
| `AppendAndReadInterleaved` | 128 | 120.3 µs / 326.49 KB | 118.8 µs / 326.56 KB |

The final implementation reduced shallow single-append time and allocation. The depth-128 interleaved workload was 1.2% faster with 72 B more allocation, a difference too small to separate from short-run noise. The exact-comparison logs are `artifacts/performance-all/path-head-bdn.log` and `artifacts/performance-all/path-final-bdn.log`.

The targeted bulk benchmark covers depths 1, 2, and 4, with a suffix materialized before each operation. Results are in `artifacts/performance-all/collection-bulk-bdn.log`.

| Depth | Share materialized suffix | Array-copy baseline |
| ---: | ---: | ---: |
| 1 | 10.79 ns / 0 B | 7.40 ns / 56 B |
| 2 | 9.89 ns / 0 B | 9.64 ns / 88 B |
| 4 | 10.52 ns / 0 B | 11.91 ns / 152 B |

The short-run timings at these depths are noisy; the bulk root append removes the per-operation array allocation. For a suffix that has not yet been read, the root append materializes its segments once and shares that resulting private array.

## Validation

- `dotnet build tests/SparseTypes.Tests/SparseTypes.Tests.csproj -c Debug --no-restore -p:MSBuildNodeReuse=false -nr:false`: passed, zero warnings and errors.
- The direct TUnit invocation ran the nine focused path and observable-list cases from `artifacts/performance-all/collection-focused-uids.txt`: 9 passed, 0 failed.
- Bulk benchmark command: `dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj -c Release --no-restore -p:MSBuildNodeReuse=false -- --filter "*SparsePathBulkSuffixBenchmarks*" --job short`. Six cases completed.
- Exact HEAD comparison used the same filter `*SparsePath*Append*Benchmarks*` in both trees; each run completed all three cases.
- `dotnet csharpier check` passed for `SparsePath.cs`, `SparsePathStorage.cs`, path tests, and all path benchmark files.
- `git diff --check`: passed.
