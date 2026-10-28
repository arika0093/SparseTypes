# Merge strategies

`Merge` combines a lower-priority Fragment with a higher-priority Fragment. Missing members in the higher layer fall through to the lower layer, while present members override or combine with the lower layer according to the member's merge mode. Most members need no attributes: `MergeMode.Default` selects shape-aware behavior automatically, and you add `[SparseMerge]` only when a member needs different combination semantics. For a task-oriented walkthrough of composing configuration layers and clearing overrides, see [Layered settings](../how-to/layered-settings.md).

## Choosing a merge mode

Each merge mode fits a specific data shape. Picking a mode that does not match how layers contribute to a member causes either lost base values or unwanted accumulation:

* Nested models use `Deep` by default so each layer can override individual leaf properties while inheriting sibling properties from lower layers. Setting `Replace` on a nested model treats the whole child object as one atomic value: a higher layer that sets only `Child.Host` wipes out `Child.Port` and every other sibling property from the base layer.
* Scalars and atomic values use `Replace` by default because a single setting has one effective value from the highest layer that specifies it. Leaving a multi-layer list on `Replace` when layers are meant to contribute independent items causes any higher-layer list to discard the base layer's items entirely.
* Ordered pipelines and search paths fit `Append`, which concatenates lower-priority items before higher-priority items. Using `Append` on unordered tags or capability flags duplicates entries whenever two layers name the same value, making collection length depend on how many layers repeated the item.
* Unique tags and enabled feature names fit `SetUnion`, which keeps the first occurrence of each distinct item in insertion order. Using `SetUnion` when repeated entries carry meaning, such as command-line arguments where flags appear multiple times, silently drops later occurrences.
* Dictionaries and keyed lists merge by key under `Default` and emit per-key operations in patches and change sets. Adding explicit `[SparseMerge(MergeMode.Replace)]` switches both `Merge` and [partial update payloads](../how-to/partial-updates.md) to whole-collection replacement, discarding all lower-layer keys whenever a higher layer provides the collection.
* Value-dependent rules such as choosing the stricter timeout or clamping numeric bounds require a custom `FragmentMergeStrategy<T>`, because built-in modes decide winners by layer priority alone without inspecting scalar values.

## Built-in modes

| `MergeMode` | Behavior | Applies to |
| --- | --- | --- |
| `Default` (`= 0`) | Shape-aware default: nested models merge member by member (`Deep`), everything else replaces | All members without explicit configuration |
| `Replace` | The higher layer's present value wins | Scalars and collections (type-dependent default) |
| `Deep` | Recursively merge nested fragments member by member | Nested models (type-dependent default for nested models) |
| `Append` | Concatenate collections from lowest to highest priority | Collections (not sets, not scalars) |
| `SetUnion` | Combine as an insertion-ordered set union | Collections and sets (not scalars) |
| `Custom` | Delegate to your own `FragmentMergeStrategy<T>` implementation | Any member via `[SparseMerge(typeof(Strategy))]` |

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
[SparseModel]
public partial class Settings
{
    public string? Label { get; set; }                    // Replace (default for scalars)

    public Child? Child { get; set; }                     // Deep (default for nested models)

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];   // concatenation

    [SparseMerge(MergeMode.SetUnion)]
    public ISet<string> Tags { get; set; } = new HashSet<string>(); // ordered union
}
```

With that shape, layering behaves member by member:

<!-- illustrative: excerpt; uses guide-local model names and does not compile as written -->
```csharp
var lower = new Settings.Fragment
{
    Label = "base",
    Child = new Child.Fragment { Host = "lower", Port = 1 },
    Plugins = new[] { "base-plugin" },
};
var higher = new Settings.Fragment
{
    Child = new Child.Fragment { Host = "higher" },
    Plugins = new[] { "extra-plugin" },
};

var merged = lower.Merge(higher);
// merged.Label == "base" (higher is missing, so the lower value falls through)
// merged.Child.Host == "higher", merged.Child.Port == 1 (Deep composes member by member)
// merged.Plugins == ["base-plugin", "extra-plugin"] (Append concatenates)
```

Applicability constraints (enforced at generation time, [SPF005](../reference/analyzer.md#spf005-unsupported-merge-mode)):

* `Deep` is only available for nested models (fragment models or structural types).
* `Append` cannot be used on set types (use an ordered collection or `SetUnion`) nor on non-collections.
* `SetUnion` cannot be used on non-collections.
* Out-of-range numeric mode values are rejected.

Structural sequences without a key cannot use the implicit default behavior: they must declare identity or explicitly select `Replace`, `Append`, `SetUnion`, or a custom strategy ([SPF011](../reference/analyzer.md#spf011-structural-sequence-without-usable-key)). An explicit `[SparseMerge(MergeMode.Replace)]` means the entire sequence or dictionary is replaced as one value; this also applies to keyed lists and dictionaries, and changes their ChangeSet payload JSON from granular entries to a whole-value operation. The implicit `Default` remains granular for keyed collections: it resolves to `Deep` for nested models and to `Replace` for everything else.

Merge modes and Patch/ChangeSet granularity answer different questions. A merge mode decides how two fragment layers combine when both are present. Patch and ChangeSet granularity decides how an edit is expressed and sent: per-key operations for keyed lists and dictionaries under the implicit default, or one whole-value replacement under explicit `Replace`. Choosing `Replace` on a keyed member keeps merge behavior simple but gives up per-item add/remove/edit tracking for that member; see [Keyed collections](keyed-collections.md#atomic-vs-keyed-collections).

### `Append`

Present collections concatenate from lowest to highest priority: merging `["base-plugin"]` with `["extra-plugin"]` yields `["base-plugin", "extra-plugin"]`. When either side is missing, the present side wins unchanged. Because `Append` is additive, an explicit empty collection `[]` in the higher layer contributes zero items and leaves the lower layer's items intact. Under `Replace`, that same empty collection replaces the lower layer with `[]`.

### `SetUnion`

Present collections combine as an insertion-ordered set union: lower-priority entries appear first, followed by higher-priority entries that are not already present. Membership uses the member's equality semantics (see below). Like `Append`, `SetUnion` is additive: a higher layer cannot remove an item contributed by a lower layer through `Merge` alone. When a higher layer needs to remove individual entries or replace the set wholesale, model the member as a keyed collection or use `Replace`.

## Custom strategies

Built-in modes decide how layers combine based on member shape and layer priority alone. When the combination rule depends on the values themselves, such as taking the minimum of two timeouts or clamping a numeric setting, derive from `FragmentMergeStrategy<T>` and implement `Merge` and `AreEqual`. `TryRebase` is an optional override for members that also need custom three-way conflict resolution (see [ChangeSet rebase](rebase.md)).

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
public sealed class LastWriteStrategy : FragmentMergeStrategy<string?>
{
    public override Optional<string?> Merge(
        Optional<string?> lowerPriority,
        Optional<string?> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(string? left, string? right) => left == right;
}
```

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
[SparseModel]
public partial class Policy
{
    [SparseMerge(typeof(LastWriteStrategy))]
    public string? Note { get; set; }
}
```

Custom strategies follow four contract rules:

* `Merge` and `TryRebase` receive `Optional<T>` so the strategy can distinguish `Missing` from a present `null` or `default`. In `TryRebase`, returning a present value produces a `Set` patch operation, returning `Missing` produces `Remove`, and returning a value equal to the current state produces `Keep` (a semantic no-op).
* The strategy type must derive from `FragmentMergeStrategy<TMember>` where `TMember` exactly matches the non-model member type, must be a concrete non-generic class, and must expose a `public` or `internal` parameterless constructor (enforced at generation time by `SPF004`).
* Generated code shares strategy instances and may invoke them concurrently across threads, so implementations must be stateless or thread-safe.
* The default `TryRebase` implementation succeeds when the desired state still equals the edit base (so the current state wins) or when the current state equals the edit base or the desired state (clean replay or already applied), and reports a `CustomStrategy` conflict otherwise.

A member can carry both a merge strategy and a rebase policy. The strategy continues to govern `Merge`, while the rebase policy takes precedence during rebase (see [ChangeSet rebase](rebase.md#custom-strategies-and-rebase-policies)). A member that only customizes rebase needs no merge strategy.

## Inspecting value origins

Layered configuration often raises diagnostic questions: when an effective setting has an unexpected value, operators and settings UIs need to know whether that value came from compiled defaults, an environment file, a tenant profile, or a user override. A fragment can carry an optional origin string to answer that question without changing how values merge or serialize. Pass the label at construction; the parameterless constructor leaves the origin as Unknown:

<!-- illustrative: excerpt; uses guide-local model names and does not compile as written -->
```csharp
var defaults = new Settings.Fragment("defaults") { Label = "base" };
var tenant = new Settings.Fragment("tenant") { Label = "custom" };

var effective = defaults.Merge(tenant);
var labelOrigin = effective.GetOrigin(Settings.SparsePath.Label); // "tenant"
```

`Merge` propagates origin labels using the same per-member rules that combine values:

* `Replace` attributes the member to whichever layer supplied the winning present value. An explicit present `null` is a deliberate reset and carries the origin of the layer that set `null`.
* `Deep` tracks attribution independently on each nested leaf property. A child fragment inherits its enclosing fragment's origin unless constructed with its own origin label, which then applies to that subtree.
* `Append` records element ranges so each index in the concatenated list keeps the origin of the layer that contributed it.
* `SetUnion` attributes each surviving element to the first layer that introduced it under the member's equality comparer; later layers that repeat an existing element do not take ownership of it.
* Custom `FragmentMergeStrategy<T>` members report Unknown after a merge because the strategy can synthesize a new value from both inputs.

Because a deep-merged child or an appended collection can contain values from multiple layers at once, querying the parent member path (`Settings.SparsePath.Child` or `Settings.SparsePath.Plugins`) returns Unknown whenever contributors differ. Query leaf or element paths for exact attribution, or project the effective fragment by origin:

<!-- illustrative: excerpt; uses guide-local model names and does not compile as written -->
```csharp
var baseLayer = new Settings.Fragment("defaults") { Plugins = new[] { "auth" } };
var userLayer = new Settings.Fragment("user") { Plugins = new[] { "metrics" } };
var merged = baseLayer.Merge(userLayer);

merged.TryGetOrigin(SparsePath.Parse<Settings>("Plugins[0]"), out var first); // "defaults"
merged.TryGetOrigin(SparsePath.Parse<Settings>("Plugins[1]"), out var second); // "user"
Settings.Fragment userOnly = merged.GetByOrigin("user"); // projects only values attributed to "user"
```

Use `GetOrigin` when Unknown and absent paths can both map to `null`, `TryGetOrigin` when you need to distinguish present-with-origin from Unknown or missing paths, `EnumerateOrigins` to list every attributable leaf and collection element path (`Plugins[0]`, `Child.Host`), `GetByOrigin` to extract a read-only fragment containing only one layer's winning values, and `SplitByOrigin` to partition the effective fragment into per-origin projections in first-seen order. Paths use `SparsePath`, either through generated typed properties (`Settings.SparsePath.Child.Host`) or parsed wire paths (`SparsePath.Parse<Settings>("Plugins[0]")`).

Origin tracking is a diagnostic projection of the winning state. Overwritten lower-priority values are discarded during `Merge`, so `SplitByOrigin` shows what each layer contributed to the final result and cannot reconstruct shadowed values from lower layers. Origins also stay outside semantic state: fragment equality, `Diff`, `ChangeSet` creation, `Patch` application, and JSON wire payloads ignore them. Applying a `Patch` resets touched paths to Unknown because patches carry operations without layer attribution.

## Semantic equality with [SparseCompare]

While `MergeMode` controls how two present values combine during `Merge`, equality controls whether two values count as different during `Diff`, `ChangeSet.Between`, `Compose`, and `Rebase`. By default, generated code compares values using standard .NET equality (`EqualityComparer<T>.Default` for scalars and structural comparers for collections). When a type needs domain-specific equality, such as case-insensitive tokens or floating-point tolerances, apply `[SparseCompare]` on a fragment model or at the assembly level:

<!-- sample: merge-compare-models -->
```csharp
public readonly struct MergeToken
{
    public MergeToken(string value) => Value = value;

    public string Value { get; }
}

public sealed class MergeTokenComparer : IEqualityComparer<MergeToken>
{
    public bool Equals(MergeToken left, MergeToken right) =>
        string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(MergeToken value) =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value);
}

[SparseCompare(typeof(MergeToken), typeof(MergeTokenComparer))]
[SparseModel]
public partial class MergeDocsCompareSettings
{
    public MergeToken Token { get; set; }
}
```
<!-- /sample -->

The generator enforces six rules on `[SparseCompare]`:

* The first argument must match the member type exactly; rules registered for a base class or an interface do not apply to derived or implementing members.
* The comparer type must implement `IEqualityComparer<T>` for that exact `T` and expose a `public` or `internal` parameterless constructor.
* A model-level attribute applies to members of that model, and an assembly-level attribute applies across the compilation. Unannotated nested models inherit a rule from their referencing root models only when every referencing root specifies the same comparer for `T`; a single unannotated root or conflicting root comparer prevents inheritance.
* Generated code caches one comparer instance per configured member and may call it concurrently, so implementations must be stateless or thread-safe.
* The comparer governs `Diff` change detection, `ChangeSet.Between` matching, `Compose` contiguity checks, and rebase conflict checks for `T`. Collection membership still uses the collection instance's own comparer, so a `HashSet<string>` constructed with `StringComparer.OrdinalIgnoreCase` evaluates membership case-insensitively regardless of `[SparseCompare]`.
* A member with a custom `[SparseMerge(typeof(...))]` strategy always uses that strategy's `AreEqual` method; `[SparseCompare]` supplies the default equality for members without a custom strategy.

When two snapshots differ only in ways the comparer treats as equivalent, `Diff` and `CreateChangeSet` suppress the transition so downstream consumers do not receive no-op updates:

<!-- sample: merge-compare -->
```csharp
var before = new MergeDocsCompareSettings { Token = new MergeToken("a") };
var after = new MergeDocsCompareSettings { Token = new MergeToken("A") };

var diff = MergeDocsCompareSettings.Fragment.Diff(before, after);
// diff.Token.IsPresent == false (comparer-equal: no change detected)
```
<!-- /sample -->

## Collection equality and comparer semantics

Set and dictionary members compare order-independently, and the element or key comparer is part of the collection's semantic value. These rules govern `Diff` detection, `SetUnion` deduplication and rebase, and default `AreEqual` checks so that equality is symmetric and never depends on operand order:

* Two collections with the same entries and the same comparer are equal regardless of enumeration order.
* Two collections with different comparers (such as `StringComparer.Ordinal` versus `StringComparer.OrdinalIgnoreCase`) are unequal even when their current entries would match under one of the comparers, because future lookups or merges on the two instances behave differently.
* Reversing the left and right operands always produces the same equality result.
* Custom `IReadOnlyDictionary<TKey, TValue>` implementations compare order-independently even when they do not implement non-generic `ICollection`. When a custom collection does not expose its comparer instance, equality requires lookups to succeed in both directions using each collection's own lookup semantics.
