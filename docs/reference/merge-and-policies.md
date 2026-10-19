# Merge, comparison, and rebase policies

Package and namespace: `SparseTypes`. Configure model members with the [attributes](attributes.md); generated code calls the selected implementations.

## MergeMode

| Value | Meaning |
| --- | --- |
| `Default` | Deep for nested generated models; Replace for scalars and ordinary collections. Structural keyed collections also require supported identity. |
| `Replace` | The higher present contribution replaces the lower contribution. |
| `Deep` | Merge nested generated model members independently. |
| `Append` | Concatenate supported sequence contributions in priority order. |
| `SetUnion` | Combine supported collections with duplicate elimination. |
| `Custom` | Use a configured strategy type; select it with `SparseMerge(typeof(...))`. |

Modes are constrained by member shape. Unsupported combinations fail generation rather than falling back to another mode. See [merge strategies](../explanation/merge-strategies.md) for collection examples and equality rules.

## FragmentMergeStrategy<T>

An abstract class with a protected parameterless constructor. A configured strategy must be concrete, accessible, and constructible without arguments.

| Member | Contract |
| --- | --- |
| `Optional<T> Merge(Optional<T> lowerPriority, Optional<T> higherPriority)` | Abstract. Returns the effective contribution, preserving the distinction between missing and present null. |
| `bool AreEqual(T? left, T? right)` | Abstract. Defines semantic equality for values of this member. |
| `bool TryRebase(Optional<T> editBase, Optional<T> desired, Optional<T> current, out Optional<T> rebased, out string? reason)` | Virtual. Reconciles one member. False reports a conflict with an optional reason. |

The default `TryRebase` accepts an unchanged current value, an already-applied desired value, or an unchanged incoming value; otherwise it reports conflict. Override it when the domain has another reconciliation rule.

## FragmentRebasePolicy<T>

An abstract class with a protected parameterless constructor. It separates concurrent reconciliation from the merge strategy.

| Member | Contract |
| --- | --- |
| `bool AreEqual(T? left, T? right)` | Abstract semantic value equality. |
| `bool TryRebase(editBase, desired, current, out rebased, out reason)` | Abstract; all value arguments and the output use `Optional<T>`. |
| `FailOnConflict(IEqualityComparer<T>? comparer = null)` | Static factory for normal three-way conflict detection. |
| `PreferIncoming(IEqualityComparer<T>? comparer = null)` | Static factory that resolves competing values in favor of incoming. |
| `PreferCurrent(IEqualityComparer<T>? comparer = null)` | Static factory that resolves competing values in favor of current. |

The factories return policy instances for direct use. To select a policy with `[SparseRebasePolicy]`, provide a concrete type implementing the abstract methods. Generated code retains configured instances, so implementations must tolerate reuse and concurrent calls.

## Comparison rules

`[SparseCompare(typeof(T), typeof(Comparer))]` selects `IEqualityComparer<T>` for the exact configured type. Model-local rules take precedence over assembly rules. A comparer must implement consistent equality and hashing and support concurrent calls.

This configuration affects whether changes exist; it is not merely a display formatter. For example, a case-insensitive string rule can suppress a case-only transition. See the checked [custom comparer example](../explanation/merge-strategies.md) and diagnostic [SPF030](analyzer.md#spf030-invalid-comparison-strategy).

For per-call fallback policies, see [`ChangePayloadRebaseOptions`](rebase-api.md#rebase-options).
