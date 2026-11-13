# Performance work: integrated validation

## Scope and completion criteria

The follow-up covers performance topics 01 through 11 in `TODO.md`, including the initial runtime fixes. Each topic needs a source-backed finding, an implementation or justified no-change decision, and the relevant regression checks and measurements. An unvalidated implementation is not complete.

Generated code measurements include file count, UTF-8 bytes, and assembly bytes. Timing comparisons must state their workload and uncertainty. Allocation improvements must not depend on dropping validation, changing ownership, missing raw-model mutations, or changing exception behavior.

## Baseline checkpoint

Before the follow-up builds, the existing Release unit-test binary passed all 2,321 tests in 1 minute 15 seconds. The command was `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --progress off --timeout 15m`; its output is in `artifacts/performance-all/unit-before.log`. This binary includes the preceding initial batch.

The existing benchmark binary captured `artifacts/performance-all/generated-before.json` with `--baseline-sizes` and `artifacts/performance-all/payload-before.json` with `--payload-sizes`.

| Models | Generated files | Generated source bytes | Assembly bytes |
|---:|---:|---:|---:|
| 1 | 6 | 380,414 | 147,456 |
| 4 | 21 | 1,345,925 | 399,872 |
| 16 | 81 | 5,211,167 | 1,500,672 |

## Final validation

Fresh Release builds and all 2,882 tests passed across the seven Linux managed suites:

| Suite | Passed |
|---|---:|
| SparseTypes.Tests | 2,328 |
| SparseTypes.Fragments.Tests | 5 |
| SparseTypes.Blazor.Tests | 262 |
| SparseTypes.EntityFrameworkCore.Tests | 218 |
| SparseTypes.OpenApi.AspNetCore.Tests | 21 |
| SparseTypes.OpenApi.Swashbuckle.Tests | 23 |
| SparseTypes.Tests.PublicApi | 25 |

Each project was built with `dotnet build tests/<suite>/<suite>.csproj -c Release --no-restore -nodeReuse:false -p:UseSharedCompilation=false`, then its `bin/Release/net10.0/<suite>.dll` was executed with `--progress off --timeout 15m`. Logs are in `artifacts/performance-all/integration/`.

`dotnet csharpier check src/` checked 435 files. A separate check passed for all 38 changed or newly added C# files, including tests, benchmarks, and the isolated consumer fixture. `git diff --check` passed.

Fresh Core, Fragments, and SparseTypes packages were built into `artifacts/performance-all/final-packages`. The existing runtime, Fragments, and C# language-version consumer scripts passed against these packages. Runtime execution used `SPARSE_CONSUMER_FRAMEWORKS=net10.0`; the scripts also compiled the netstandard2.0 and serializer-free consumers. The C# 9.0 probe passed, its net48 compilation passed, and the C# 8.0 negative probe failed as intended. The runtime-independent Shared package probe passed separately after compiling the generated guard and its caller.

NativeAOT publication, the repository's AOT diagnostic check, and all 51 native tests passed. The initial `--no-restore` publish lacked `linux-x64` assets. Restoring those assets then exposed NU1903 for the installed SDK's `Microsoft.DotNet.ILCompiler` 10.0.12. Advisory [GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9) identifies 10.0.14 as the fixed .NET 10 version. The successful command was `dotnet publish tests/SparseTypes.Tests.NativeAot/SparseTypes.Tests.NativeAot.csproj --configuration Release --framework net10.0 --use-current-runtime -p:RuntimeFrameworkVersion=10.0.14 -nodeReuse:false -p:UseSharedCompilation=false --output artifacts/performance-all/nativeaot`. The restored assets confirm both ILCompiler packages are version 10.0.14. No repository dependency was changed and no audit warning was suppressed.

The native binary was executed with `--progress off --timeout 15m`; its logs are `artifacts/performance-all/integration/nativeaot-publish.log` and `nativeaot-tests.log`. Together with the managed suites, 2,933 tests passed. The temporary baseline worktree and repository-root lock files created during this work were removed. All 14 performance-report links in `todo/TODO.md` resolve to non-empty files.

Windows desktop binding tests and net48 execution require Windows and were not run on this Linux host. The generated consumer's net48 compile check passed.

## Review checkpoints

ValidationStore's retained query path is cleared on invalidation, so clearing or disposing the store does not retain the last queried key graph through the cache. Its focused suite passed all 23 cases after this correction.

Observable-list range pruning no longer builds a removed-key set. Duplicate references and the raw-model-access fallback passed focused regression checks.

The path implementation was revised after review found that caching every prefix could retain quadratic storage. The revised path disconnects materialized prefixes on append and preserves efficient bulk suffix concatenation. An identical-method comparison against actual commit `3f62fab5` then measured shallow append and interleaved reads. The interleaved workload was effectively unchanged within ShortRun uncertainty; shallow append allocated less. Synthetic array/formatting kernels are labelled separately in the path report.

The generated `TryAdd` experiment was rejected because it broke the `netstandard2.0` reference surface and changed duplicate-path value evaluation. The retained generated-once helper shares only the duplicate guard, leaving value evaluation after validation. It adds no files and saves 4,652 generated bytes for 16 keyed models; one keyed model grows by 463 bytes because of the fixed helper cost. The isolated Shared consumer compiles the helper and its caller without a SparseTypes runtime reference.
