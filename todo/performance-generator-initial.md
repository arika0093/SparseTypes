# Generator performance: initial batch

## Scope and method

This batch examined the generator's split-source partitioning path. Before the change, `SparseTypesGeneratedFilePartitioner.Partition` called `AddFile` up to five times after parsing each rendered model. Each call walked the compilation-unit members, filtered members independently, and materialized namespace members with `Where(...).ToArray()`. For wide generated models, that repeated classification and allocated temporary arrays after the renderer had already built a large source string.

The change preserves generated file categories, hints, member order, and source text while classifying members in one pass. Existing split-output tests and before/after generator runs verified emitted behavior, output sizes, and hashes. A separate probe measured elapsed time and allocation.

## Findings and plan

| Status | Finding | Correction |
|---|---|---|
| Complete | `src/SparseTypes.Generator/SparseTypesGeneratedFilePartitioner.cs:52-128` previously traversed and filtered the parsed compilation unit separately for up to five output files, allocating a filtered array for each namespace/output pair. | Classify each member once into output buckets and create only non-empty files. Cache serializer suffix strings per model in `PartitionCriteria` (`:27-50`). Keep member order, namespace usings, trivia, and hints unchanged. |
| Deferred | No pre-existing generator correctness defect was verified in this bounded pass. | Keep the remaining leads below unverified; investigate them in their own focused batch. |

## Deferred leads

At the start of this batch, `todo/TODO.md` linked to `performance-01` through `performance-11` investigation notes, but those files were absent from `todo/`. Their claims were not treated as verified findings. Keyed patch construction, traversal, transition projection, and other listed runtime areas remain outside this batch.

## Verification record

Baseline and post-change output from `BaselineGeneratorBenchmarks.WriteSizes` were measured with:

```sh
dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release --no-restore -- --baseline-sizes artifacts/generator-sizes-before.json
dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release --no-restore -- --baseline-sizes artifacts/generator-sizes-after.json
```

The generated-file count, source bytes, SHA-256, and assembly bytes matched for all three model counts:

| Models | Generated files | UTF-8 source bytes | Assembly bytes | Output SHA-256 |
|---:|---:|---:|---:|---|
| 1 | 6 | 380,414 | 147,456 | `96D628EC4DF98514F9443EB12D932C2D8B8B39CAA95C1B88AEE8C2987C1C2598` |
| 4 | 21 | 1,345,925 | 399,872 | `BE86FC4731F23471ED03B387D589C61661A79626B099DC8655A35177176DD270` |
| 16 | 81 | 5,211,167 | 1,500,672 | `0339B68AC4ADE7382A4AC426A50146458E2C8C00B3C57949ACA9C76565E5997B` |

A second probe generated 1, 4, and 16 global models with 24 scalar properties each, then ran five fresh generator executions per shape. It counted physical generated-source lines, UTF-8 bytes, files, and output hashes. Allocation is the average per fresh execution.

| Models | Files | Source bytes | Lines | Allocation before → after | Elapsed time before → after (5 runs) | Output SHA-256 |
|---:|---:|---:|---:|---:|---:|---|
| 1 | 6 | 1,195,882 | 13,266 | 32,361,132 → 32,350,228 B | 617.4 → 608.2 ms | `5389C344B89A7864983397B88F14D88C4F3663CAD78C4B00AE23A77DC46E15A6` |
| 4 | 21 | 4,642,942 | 51,129 | 129,593,008 → 129,548,235 B | 1,159.9 → 1,413.8 ms | `305B48C768588C45B7ECF34A91321D7C1CD2CDADF3D61778574C712795316DAE` |
| 16 | 81 | 18,436,762 | 202,581 | 518,813,734 → 518,678,318 B | 3,133.3 → 3,312.4 ms | `02668CAEE2B3E9AA757FCED57ECAB803D892314253C40DF95A868437CF610A79` |

Allocation fell by 0.026–0.035%; elapsed time was inconsistent and does not establish a generator-wide speedup. The output-size and hash checks confirm exact generated-source preservation. Treat the runtime effect as small; most measured work is outside partitioning.

Focused validation:

- Original and edited source measurements used `artifacts/generator-partition-metrics/GeneratorPartitionMetrics.csproj`; the original run temporarily compiled the `HEAD` partitioner, then restored the working source. The probe was cleaned after measuring.
- `dotnet build tests/SparseTypes.Tests/SparseTypes.Tests.csproj --configuration Release --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet run --project tests/SparseTypes.Tests/SparseTypes.Tests.csproj --configuration Release --no-build -- --filter-uid 'SparseTypes.Tests.Generation.GeneratedFilePartitionerTests.1.1.NamespaceScopedUsingsRemainWithGeneratedDeclarations.1.1.0' --filter-uid 'SparseTypes.Tests.Generation.PayloadImplementationSplitTests.1.1.ModelOutputUsesResponsibilityBasedFiles.1.1.0' --filter-uid 'SparseTypes.Tests.Generation.PayloadImplementationSplitTests.1.1.ModelFilesOmitEmptyOperationContainersAndPreserveNestedPayloads.1.1.0'` — 3 passed.
- `dotnet tool run csharpier -- check src/SparseTypes.Generator/SparseTypesGeneratedFilePartitioner.cs tests/SparseTypes.Tests/Generation/GeneratedFilePartitionerTests.cs` — passed.
- `dotnet pack src/SparseTypes.Generator.Shared/SparseTypes.Generator.Shared.csproj --configuration Release --output artifacts/generator-partition-metrics/feed` — packed `SparseTypes.Generator.Shared` 0.1.0-rc1.172.
- Built copied `PackageShared.Generator` and `PackageShared.Consumer` fixture projects with these commands, setting `NUGET_PACKAGES` to an isolated cache under `artifacts/`:

  ```sh
  NUGET_PACKAGES="$PWD/artifacts/generator-partition-metrics/nuget-packages" dotnet build artifacts/generator-partition-metrics/consumer/PackageShared.Generator.csproj --configuration Release -p:SparseSharedPackageVersion=0.1.0-rc1.172 -p:RestoreAdditionalProjectSources="$PWD/artifacts/generator-partition-metrics/feed"
  NUGET_PACKAGES="$PWD/artifacts/generator-partition-metrics/nuget-packages" dotnet build artifacts/generator-partition-metrics/consumer/consumer/PackageShared.Consumer.csproj --configuration Release -p:SparseSharedPackageVersion=0.1.0-rc1.172 -p:RestoreAdditionalProjectSources="$PWD/artifacts/generator-partition-metrics/feed"
  ```

  Both builds passed with 0 warnings/errors; the assets contain the packed source-only package and no `SparseTypes`, `SparseTypes.Core`, or `SparseTypes.Fragments` runtime package. The repository script was not invoked because it uses `mktemp`; equivalent package, restore, build, and isolation checks ran without temporary-directory helpers.

No separate product-specific correctness bug was found. `tests/SparseTypes.Tests/Generation/GeneratedFilePartitionerTests.cs:6-32` protects namespace-scoped using directives and split-file behavior. The Shared package probe passed; no Shared source or README change was needed.
