# Model attributes

Package and namespace: `SparseTypes`. Attributes configure source generation. Invalid combinations produce [analyzer diagnostics](analyzer.md).

| Attribute and constructor | Target | Members and contract |
| --- | --- | --- |
| `[SparseModel]` | Class or struct | Parameterless. Starts generation for a supported partial model. Not inherited. |
| `[SparseIgnore]` | Property | Parameterless. Removes the member from generated tracking, fragments, patches, cloning, and payloads. Inherited with the property. |
| `[SparseKey]` | Property | Parameterless; optional settable `object? Unassigned` declares a sentinel. Exactly one readable key per element-patched model. |
| `[SparseTemporaryKey]` | Property | Parameterless. Marks one public read/write `Guid?` property without an initializer, paired with an unassigned key. |
| `[SparseMerge(MergeMode mode)]` | Property | Read-only `Mode` and `StrategyType`. Configures the member merge operation. |
| `[SparseMerge(Type strategyType)]` | Property | Selects a concrete `FragmentMergeStrategy<TMember>`; `Mode` is Custom. |
| `[SparseCompare(Type valueType, Type comparerType)]` | Assembly, class, or struct | Read-only `ValueType` and `ComparerType`. Repeatable, not inherited. Maps an exact type to a concrete `IEqualityComparer<T>` with an accessible parameterless constructor. Null constructor arguments throw. |
| `[SparseRebasePolicy(Type policyType)]` | Property | Read-only `PolicyType`. Selects a concrete `FragmentRebasePolicy<TMember>` with an accessible parameterless constructor. |
| `[SparseRedactBefore]` | Property | Parameterless. Hides before-state on payload export; does not remove the local transition. |
| `[SparseCloneReferenceSafe]` | Property | Parameterless. Opts into sharing that reference when cloning instead of requiring a deep copy. |

Except for `SparseModel` and `SparseCompare`, the attributes above are inherited with property declarations. `SparseKey`, `SparseTemporaryKey`, `SparseIgnore`, and `SparseRedactBefore` do not allow multiple instances on one property.

## Example model

These models are shared by the generated API examples:

<!-- sample: reference-models -->
```csharp
[SparseModel]
public partial class ReferenceSettings
{
    public string? Theme { get; set; }
    public int RetryCount { get; set; }
    public List<ReferenceItem> Items { get; set; } = new();
}

public partial class ReferenceItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
```
<!-- /sample -->

`ReferenceItem` is promoted from the root model's collection. Its key identifies rows independently of list position.

## Interactions

`SparseIgnore` excludes a member from the generated surface. `System.Text.Json.Serialization.JsonIgnoreAttribute` retains local tracking but excludes Fragment JSON. Exporting a changed JsonIgnore member to a payload throws instead of dropping it.

`SparseMerge` controls layering and collection semantics. `SparseCompare` controls semantic equality. `SparseRebasePolicy` handles concurrent changes; configuring a merge mode does not grant unconditional overwrite permission during rebase.

See [model shapes](model-shapes.md) for constructor, key, promotion, and language requirements; [merge policies](merge-and-policies.md) for customization; and [cloning and ownership](../explanation/cloning-and-ownership.md) for the consequences of sharing references.

The EF Core package also supplies [`SparseFragmentMapperAttribute<TEntity,TDto>`](efcore-mapping.md), with read-only `EntityType` and `DtoType`, for partial mapper classes.
