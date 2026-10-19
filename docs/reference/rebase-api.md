# Rebase results and conflict resolution

Package and namespace: `SparseTypes`. Generated [`ChangeSet.RebaseOnto`](generated-changeset.md) and [`Patch.Rebase`](generated-patch.md) return these runtime contracts.

## RebaseResult<TChange>

| Member | Contract |
| --- | --- |
| `RebaseResult(TChange rebased, IEnumerable<SparseConflict> conflicts)` | Creates a result and copies conflicts into a read-only list. Null conflicts throw `ArgumentNullException`. |
| `TChange Rebased` | Reconciled change, excluding conflicting members. |
| `IReadOnlyList<SparseConflict> Conflicts` | Structured unresolved conflicts. |
| `bool HasConflicts` | Whether Conflicts is non-empty. |
| `Success(TChange rebased)` | Static factory with no conflicts. |

A result with conflicts can still contain non-conflicting edits. Applying only `Rebased` is a decision to omit the conflicting edits, not a successful resolution of the entire request.

## SparseConflict

Constructor: `SparseConflict(SparsePath path, SparseConflictKind kind, Optional<object?> baseValue, Optional<object?> localValue, Optional<object?> currentValue, string? reason)`.

| Read-only member | Meaning |
| --- | --- |
| `Path`, `PathText` | Structured path and its textual form. |
| `Kind` | Conflict category. |
| `BaseValue` | Incoming baseline endpoint. |
| `LocalValue` | Incoming desired endpoint. |
| `CurrentValue` | Current authoritative endpoint. |
| `Reason` | Optional human-readable detail. |

`WithPathPrefix(string memberName)`, `WithPathPrefix(SparsePath prefix)`, `WithKeyPrefix<TKey>(TKey key)`, and `WithIndexPrefix(int index)` return copies with prefixed paths. A path prefix supplies the new root type.

`SparseConflictKind` values are `WholeContribution`, `Nested`, `Scalar`, `CollectionAppend`, `CollectionSetUnion`, `CustomStrategy`, `RedactedBefore`, `InPlaceWriteUnavailable`, and `Reconciliation`. Redacted conflicts never carry secret before-state plaintext.

## Rebase options

`ChangePayloadRebaseOptions` has a parameterless constructor and init-only properties:

| Member | Default and behavior |
| --- | --- |
| `DefaultRebaseMode` | `SparseRebaseMode.Default`. Fallback for scalar and whole-replace members without explicit configuration. |
| `RejectChangesWithRedactedBeforeValuesDuringRebase` | False. True rejects redacted-before changes instead of passing them through. |
| `RedactedBeforePaths` | Empty list. A dotted path covers its subtree; an empty string covers the whole contribution. |
| `IsRedactedBefore(string? pathText)` | Tests a dotted path against the configured redaction paths. |
| `IsRedactedBefore(IReadOnlyList<string> path)` | Same test with member segments. |
| `Nest(string segment)` | Returns options relative to a nested member; null throws `ArgumentNullException`. |

`SparseRebaseMode` contains `Default`, `FailOnConflict`, `PreferIncoming`, and `PreferCurrent`. Default retains normal conflict detection; preference modes are deliberate overwrite policies.

## Resolution API

`T.ChangeSet.BeginResolution(result, current)` creates a model-specific resolution backed by `SparseRebaseResolution<T.ChangeSet>`. Current may be a model or `Optional<T.Fragment?>`.

| Member | Result or effect |
| --- | --- |
| `Rebase`, `Conflicts`, `HasConflicts`, `DecidedCount` | Original result and decision summary. |
| `EnumerateConflicts()`, `EnumerateUnresolvedConflicts()` | All conflicts or those without decisions. |
| `EnumerateDecidedPaths()` | Paths with recorded decisions. |
| `FindConflict(path)`, `HasConflict(path)` | Exact-path lookup. |
| `HasConflictsUnder(path)` | Checks conflicts strictly below the path. |
| `HasConflictsAffecting(path)` | Includes exact, ancestor, and descendant conflicts. |
| `GetState(path)` | `NoConflict`, `Unresolved`, `ResolvedUseIncoming`, `ResolvedUseCurrent`, or `ResolvedCustom`. |
| `UseIncoming(path)`, `UseCurrent(path)` | Records which endpoint to retain. |
| `SetValue<TModel,TValue>(typedPath, value)` | Records a custom value; accepts `TValue?` or `Optional<TValue>`. |
| `Remove(SparsePath path)` | Records a missing endpoint. |
| `ClearDecision(SparsePath path)` | Removes a decision; returns whether one existed. |
| `TryBuild(out TChange? resolved, out RebaseResolutionFailure? failure)` | Builds when all conflicts are decided and choices are valid. |

Query methods and UseIncoming/UseCurrent accept `SparsePath` and `SparsePath<TModel,TValue>`. `RebaseResolutionFailure` exposes `UnresolvedConflicts` and `Reason`; its constructor takes those two values. `ResolvedChoice` stores a `Conflict` and an `Optional<object?> Desired` endpoint and has a constructor accepting both.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-rebase -->
```csharp
var baseline = new ReferenceSettings { Theme = "light" };
var incoming = baseline.CreateChangeSet(new ReferenceSettings { Theme = "dark" });
var current = new ReferenceSettings { Theme = "solarized" };
var result = incoming.RebaseOnto(current);

var resolution = ReferenceSettings.ChangeSet.BeginResolution(result, current);
resolution.UseIncoming(ReferenceSettings.SparsePath.Theme);
var resolved = resolution.TryBuild(out var changes, out var failure);
```
<!-- /sample -->

The rebase reports a Theme conflict. The explicit incoming decision produces a change from `"solarized"` to `"dark"`; `resolved` is true and `failure` is null.

See [rebase semantics](../explanation/rebase.md) for three-way rules and [validation stores](validation.md) for presenting conflicts.
