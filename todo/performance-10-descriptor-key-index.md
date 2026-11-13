# Descriptor key indexes

## Findings and plan

The runtime fallback in `SparseArrayDescriptorLookup` scans the current collection and rejects duplicate keys. Generated descriptor accessors also provide live scan delegates for assigned and temporary keys. The live scan observes raw model edits, same-count reorders, duplicate introduction, null items, and key changes.

The runtime access contract has no collection or key revision that can invalidate a retained index. Comparing current keys to an index snapshot on every call would still inspect the whole collection and would add work; trusting only count, item identity, or a hash would permit stale results after raw key edits. I will measure the existing repeated lookups and run the focused raw-mutation and duplicate tests. I will not add an unsafe cache or alter the generator-owned emitters.

## Completion

No code change was made. Generated assigned-key and temporary-key delegates scan the live sequence and reject duplicates (`src/SparseTypes.Generator.Shared/Emitters/SparseObservableSequenceDescriptorEmitter.cs:391-435`). The runtime fallback uses the current count and key resolvers for every lookup (`src/SparseTypes/Generated/SparseArrayDescriptorLookup.cs:7-83`); it invokes the configured `KeyEquals` delegate for each assigned-key comparison (`src/SparseTypes/Generated/SparseDescriptors.cs:413-428`). `SparseArrayDescriptorAccess` exposes accessors and delegates but no collection/key revision or invalidation signal (`src/SparseTypes/Generated/SparseDescriptors.cs:367-428`). The existing tests cover raw key changes, same-count reorders, duplicate introduction, and null sequence elements (`tests/SparseTypes.Tests/Descriptors/DescriptorKeyLookupCacheTests.cs:6-65`). The blocker is semantic: raw key edits can occur without list/count/reference changes, so a persistent index cannot be invalidated reliably without changing the observation contract or API. Keeping live scans preserves those mutations, duplicate rejection, and the configured equality delegate.

Verification: `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/*/DescriptorKeyLookupCacheTests/*"` passed 2/2. Current live-scan measurements for 100 repeated lookups (BDN short, 3 warmups/3 measurements):

| Lookup | 16 items | 512 items |
|---|---:|---:|
| assigned key | 13.50 µs / 64.84 KB | 406.70 µs / 2,002.34 KB |
| temporary key | 13.55 µs / 62.50 KB | 393.30 µs / 2,000 KB |

Command: `dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release --no-build --no-restore -- --filter "*DescriptorKeyLookupBenchmarks*" --job short --artifacts artifacts/performance-all/state-bdn/descriptor-short`. The ~30× increase from 16 to 512 items confirms the linear scan cost, but does not justify an unsafe index: these same paths must observe raw key edits/reorders and reject duplicates while preserving the configured equality delegate. No before/after comparison applies because no safe index change was made.
