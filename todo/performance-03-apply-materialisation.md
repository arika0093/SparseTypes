# 03 Apply materialisation

## Initial inspection

`SparseFragmentPatchEmitter.AppendApplyToBody` emits model-level application through `Fragment.From(current).Apply(patch).ToModel()`. That path returns a new model, so the final model conversion is part of the API contract. In-place application instead calls `Fragment.Apply(...).__SparseWriteWritableTo(current)`; `SparseFragmentSurfaceOrderingEmitter.AppendWritableMemberWriter` writes directly from the fragment and does not allocate an intermediate model. Keyed collection application in `SparseKeyedSequenceApplyEmitter.EmitKeyedApply` builds a key map and a result list, then reuses that list for list-shaped members; array-shaped members require `ToArray`. `SparseChangeSetPatchSyncEmitter.AppendFromPatch` applies to the baseline and subsequently calls `Between`.

These conversions preserve a useful separation between fragment semantics and mutable model properties, but they can create several full-size collection/model representations in one operation. The cost depends on whether a patch is whole-value or granular and on the declared collection type.

## Repair plan

Measure `ApplyTo`, in-place `ApplyInPlace`, and keyed granular application separately. The in-place writer already avoids constructing a model; do not replace the required `ToModel` in `ApplyTo` without a way to preserve the new-instance contract. Look for an avoidable keyed result copy, retaining defensive snapshots and all model/property semantics. Verify missing and null collections, custom comparers, duplicate identities, ordering, exceptions, and source collections that change without notifications. Add focused regression cases only for changed behavior and report whole and granular results separately.

## Disposition

### Closed: existing materialisation boundaries are required

The implementation was traced through `SparseFragmentPatchEmitter.AppendApplyToBody`, `SparseFragmentSurfaceOrderingEmitter.AppendWritableMemberWriter`, and `SparseKeyedSequenceApplyEmitter.EmitKeyedApply`. `ApplyTo` must return an independently materialized model; the in-place writer writes directly into the caller's model; list-shaped keyed apply reuses its result list, while array-shaped properties necessarily convert to an array. `FromPatch` applies to a baseline and then computes `Between` because a change set describes both endpoints. No extra full-size intermediate was identified that can be removed without changing ownership or endpoint semantics.

Focused validation on the existing implementation:

- `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/SparseTypes.Tests/ApplyInPlaceTests/*" --show-test-results none` — 11/11 passed.
- `dotnet tests/SparseTypes.Tests/bin/Release/net10.0/SparseTypes.Tests.dll --treenode-filter "/*/SparseTypes.Tests/KeyedCollectionPatchTests/*" --show-test-results none` — 20/20 passed.

The existing keyed apply benchmark measured `KeyedPatchBenchmarks.Apply_OneEdit(Size: 2048)` at 92.439 µs and 226.9 KB allocated/op (ShortRun, 3 iterations; `artifacts/performance-all/keyed-apply-after-one-edit.log`). No source change was made, so this is a current baseline, not a before/after delta. The existing materialization contracts are the concrete blocker to removing these copies; any replacement must first demonstrate an avoidable allocation while preserving new-model independence, collection snapshots, null/missing behavior, order, and array semantics.
