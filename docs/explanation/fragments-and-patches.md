# Fragments and Patches

SparseTypes generates four core representation types for each `[SparseModel]`:

| Type | Question it answers | Mutability and baseline |
| --- | --- | --- |
| `T.Fragment` | *What is specified?* (missing, present value, or explicit `null` per member) | Immutable, baseline-free |
| `T.Patch` | *What should be changed?* (set value, set `null`, remove contribution, or keep) | Mutable, baseline-free |
| `T.ChangeSet` | *What changed from X to Y?* (observed before/after transition) | Immutable, baseline-aware |
| `T.ChangePayload` | *What travels over JSON?* (versioned wire envelope for `ChangeSet` and `Patch`) | Serializable DTO |

If you are new to the library, start with the [Tutorials](../tutorial/first-sparse-edit.md). For task-oriented recipes, see [Layered settings](../how-to/layered-settings.md) and [Partial updates](../how-to/partial-updates.md).

## Choose the Operation

Pick the representation that matches what your caller knows:

* **Two ordinary models, no baseline needed later**: `T.Fragment.Diff(before, after)` plus `ApplyChanges`. Use this to persist only values that differ from defaults.
* **A baseline-free command**: `new T.Patch { ... }` plus `Apply` (or `ApplyTo` for models). Use this when the caller writes desired values without having read a baseline.
* **A baseline-aware transition**: `beforeModel.CreateChangeSet(afterModel)` or `T.ChangeSet.Between(beforeSparse, afterSparse)`. Use this when later steps need undo (`Invert`), sequencing (`Compose`), or conflict-aware reconciliation (`RebaseOnto`, `TryApplyTo`).
* **Crossing a process boundary**: `T.ChangePayload` (`changes.ToPayload()`, `ChangePayload.FromPatch(patch)`). Always serialize `ChangePayload`, never `ChangeSet` or `Patch`.
* **Tracking unsaved edits in a running application**: `model.CreateEditSession()`. See [Edit sessions](edit-sessions.md) and [Manage an edit session](../how-to/edit-session.md).

<!-- sample: core-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class CounterSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}
```
<!-- /sample -->

## Create and Edit a Sparse Fragment

`new T.Fragment { ... }` specifies only the members you assign; unassigned members remain `Optional<T>.Missing`. By contrast, `T.Fragment.From(model)` snapshots every member of an ordinary model into a complete fragment contribution:

<!-- sample: core-create -->
```csharp
var current = CounterSettings.Fragment.From(
    new CounterSettings { Label = "a", RetryCount = 1 }
);

var sparse = new CounterSettings.Fragment
{
    RetryCount = 3, // present; Label stays missing
};

// sparse.Label.IsPresent == false
// sparse.RetryCount.Value == 3
```
<!-- /sample -->

To modify an existing fragment without mutating it, open a mutable builder with `ToBuilder()`, assign `Optional<T>.Present(...)` or `Optional<T>.Missing`, and call `Build()`:

<!-- sample: core-builder -->
```csharp
var original = new CounterSettings.Fragment { Label = "before" };

var builder = original.ToBuilder();
builder.Label = Optional<string?>.Present(null);

var result = builder.Build();
// result.Label.IsPresent == true, value null; original.Label stays "before"

builder.Label = Optional<string?>.Missing;
// builder.Build().Label.IsPresent == false; result still carries present null
```
<!-- /sample -->

## Layer Overrides and Diff Ordinary Models

`lower.Merge(higher)` overlays two fragments: present members of the higher layer win, and missing members fall through to the lower layer:

<!-- sample: core-layering -->
```csharp
var defaults = new CounterSettings.Fragment { RetryCount = 3 };
var environment = new CounterSettings.Fragment { RetryCount = 5 };
var user = new CounterSettings.Fragment { Label = "dark" };

var effective = defaults.Merge(environment).Merge(user);
// effective.Label == "dark"
// effective.RetryCount == 5
```
<!-- /sample -->

`Fragment.Diff(beforeModel, afterModel)` compares two ordinary models and returns a sparse `Fragment` holding only the changed after-values, which `ApplyChanges` replays onto a baseline fragment:

<!-- sample: core-diff -->
```csharp
var beforeModel = new CounterSettings { Label = "a", RetryCount = 1 };
var afterModel = new CounterSettings { Label = "a", RetryCount = 2 };

var diff = CounterSettings.Fragment.Diff(beforeModel, afterModel);
// diff.Label.IsPresent == false
// diff.RetryCount.Value == 2

var restored = CounterSettings.Fragment.From(beforeModel).ApplyChanges(diff);
// restored.RetryCount.Value == 2
```
<!-- /sample -->

## Apply Explicit Edits with Patch

A `Patch` is mutable and baseline-free. Assigning a value sets it (including an explicit `null`), `.Remove()` sets the member back to `Missing` so the next merge falls through to a lower layer, and untouched members stay unchanged:

<!-- sample: core-patch -->
```csharp
var basis = CounterSettings.Fragment.From(
    new CounterSettings { Label = "a", RetryCount = 1 }
);

var update = new CounterSettings.Patch { Label = (string?)null };
var updated = basis.Apply(update);
// updated.Label.IsPresent == true
// updated.Label.Value is null
// updated.RetryCount.Value == 1

var remove = new CounterSettings.Patch();
remove.RetryCount.Remove();
// !remove.Apply(basis).Value!.RetryCount.IsPresent
```
<!-- /sample -->

## Derive and Observe Transitions with ChangeSet

`T.ChangeSet.Between(beforeSparse, afterSparse)` (or `beforeModel.CreateChangeSet(afterModel)` for ordinary models) computes the immutable before-to-after transition, including presence transitions from present to missing:

<!-- sample: core-between -->
```csharp
var a = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a" })
);
var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

var removal = CounterSettings.ChangeSet.Between(a, b); // Label: present → missing
// !removal.ToPatch().Apply(a).Value!.Label.IsPresent
```
<!-- /sample -->

Generated properties on `ChangeSet` mirror the model shape, exposing `IsChanged`, `Before`, and `After` for each scalar member without reflection or boxing:

<!-- sample: core-typed -->
```csharp
var before = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var after = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
);

var changes = CounterSettings.ChangeSet.Between(before, after);

if (changes.Label.IsChanged)
{
    Console.WriteLine($"{changes.Label.Before} -> {changes.Label.After}");
}
// changes.Label.Before.Value == "a"
// changes.Label.After.Value == "b"
// changes.RetryCount.IsChanged == false
```
<!-- /sample -->

Nested models expose their own typed `ChangeSet` subtree:

<!-- sample: core-nested-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class DocsOrder
{
    public string? Name { get; set; }

    public DocsCustomer? Customer { get; set; }
}

public partial class DocsCustomer
{
    public string Name { get; set; } = string.Empty;
}
```
<!-- /sample -->

<!-- sample: core-nested -->
```csharp
var before = Optional<DocsOrder.Fragment?>.Present(
    DocsOrder.Fragment.From(
        new DocsOrder
        {
            Name = "a",
            Customer = new DocsCustomer { Name = "Ann" },
        }
    )
);
var after = Optional<DocsOrder.Fragment?>.Present(
    DocsOrder.Fragment.From(
        new DocsOrder
        {
            Name = "a",
            Customer = new DocsCustomer { Name = "Bob" },
        }
    )
);

var changes = DocsOrder.ChangeSet.Between(before, after);
// changes.Name.IsChanged == false
// changes.Customer.Name.IsChanged == true
// changes.Customer.Name.Before.Value == "Ann"
// changes.Customer.Name.After.Value == "Bob"
```
<!-- /sample -->

For generic consumers such as audit loggers or persistence mappers that operate across models without naming generated types, every `ChangeSet` implements `ISparseChangeSet` (`TryGetValueChange<T>`, `TryGetNestedChange`, `TryGetKeyedChange`, `TryGetDictionaryChange`, and `EnumerateChangeRecords()`):

<!-- sample: core-transition-contracts -->
```csharp
var before = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var after = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
);

// Generic consumers program against the contracts, not generated names.
ISparseChangeSet changes = CounterSettings.ChangeSet.Between(before, after);
// changes.IsChanged == true

if (changes.TryGetValueChange<string>("Label", out var label) && label.IsChanged)
{
    Console.WriteLine($"{label.Before} -> {label.After}");
}
// label.Before.Value == "a"
// label.After.Value == "b"
// changes.TryGetValueChange<int>("Label", out _) == false

var records = changes.EnumerateChangeRecords();
// records.Single().PathText == "Label"
```
<!-- /sample -->

## Convert, Compose, and Invert

`changes.ToPatch()` projects a `ChangeSet` to a baseline-free `Patch`, discarding the before-state. Conversely, `T.ChangeSet.FromPatch(baseline, patch)` attaches a known baseline to a `Patch` by applying it and diffing the result:

<!-- sample: core-changeset -->
```csharp
var start = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var finish = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
);

// Patch says "set these values": mutable and baseline-free.
var desired = new CounterSettings.Patch { Label = "b" };

// ChangeSet says "these values changed from X to Y": immutable and baseline-aware.
var transition = CounterSettings.ChangeSet.Between(start, finish);
// transition.IsEmpty == false
// transition.ToPatch().Apply(start) replays finish

// ChangeSet.FromPatch attaches a known baseline to an existing patch.
var fromPatch = CounterSettings.ChangeSet.FromPatch(start, desired);
// fromPatch.ToPatch().Apply(start) replays finish
```
<!-- /sample -->

Contiguous `ChangeSet` instances chain via `first.Compose(second)` and reverse without an external baseline via `Invert()`, while `Patch` instances chain as baseline-free operations:

<!-- sample: core-algebra -->
```csharp
var s0 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var s1 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
);
var s2 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 })
);

// ChangeSet + ChangeSet -> ChangeSet for contiguous transitions.
var first = CounterSettings.ChangeSet.Between(s0, s1);
var second = CounterSettings.ChangeSet.Between(s1, s2);
var combined = first.Compose(second);
// combined.ToPatch().Apply(s0) reaches s2

// ChangeSet inverts without an external baseline.
var undone = combined.Invert();
// undone.ToPatch().Apply(s2) walks back to s0

// Patch + Patch -> Patch stays baseline-free.
var local = new CounterSettings.Patch { Label = "x" };
var more = new CounterSettings.Patch { RetryCount = 5 };
var both = local.Compose(more);
// both.Label == "x", both.RetryCount == 5
```
<!-- /sample -->

## Serialize with ChangePayload

`ChangeSet` and `Patch` are in-memory types; serialize `T.ChangePayload` when crossing a process boundary. `changes.ToPayload()` packages a `ChangeSet` with its before and after endpoints, and `T.ChangePayload.FromPatch(patch)` packages a baseline-free `Patch` with redacted before-states:

<!-- sample: core-serialization -->
```csharp
using System.Text.Json;

var start = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var finish = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 })
);

var changes = CounterSettings.ChangeSet.Between(start, finish);

var json = JsonSerializer.Serialize(changes.ToPayload());
var restored = JsonSerializer
    .Deserialize<CounterSettings.ChangePayload>(json)!
    .ToChangeSet();
// restored.ToPatch().Apply(start) replays finish
```
<!-- /sample -->

Marking a property with `[SparseRedactBefore]` redacts its before-value in `ToPayload()` so sensitive prior values (such as passwords or tokens) never leave the sender:

<!-- sample: core-change-payload-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class LoginSettings
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
}
```
<!-- /sample -->

<!-- sample: core-change-payload -->
```csharp
var current = LoginSettings.Fragment.From(
    new LoginSettings { DisplayName = "a", Password = "before-password" }
);

var rotation = new LoginSettings.Patch { Password = "after-password" };

// A command needs no baseline: the envelope redacts what it never observed.
var command = LoginSettings.ChangePayload.FromPatch(rotation);
var applied = current.Apply(command.ToPatch());
// applied.DisplayName.Value == "a"
// applied.Password.Value == "after-password"

var transition = LoginSettings.ChangeSet.Between(
    Optional<LoginSettings.Fragment?>.Present(current),
    Optional<LoginSettings.Fragment?>.Present(applied)
);
var wire = JsonSerializer.Serialize(transition.ToPayload());
// wire carries "after-password" but never "before-password"
```
<!-- /sample -->

See [ChangePayload wire reference](../reference/change-payload.md) for the JSON schema and endpoint state rules.

## Which API for Which Task

| Need | API | Result |
| --- | --- | --- |
| Build a sparse override | `new T.Fragment { ... }` | `Fragment` |
| Edit a sparse fragment | `fragment.ToBuilder()` ... `Build()` | `Fragment` |
| Snapshot an ordinary model | `T.Fragment.From(model)` | `Fragment` |
| Combine lower/higher layers | `lower.Merge(higher)` | `Fragment` |
| Inspect which layer supplied a value | `effective.GetOrigin(path)` / `effective.EnumerateOrigins()` | Origin or entries |
| Compare two ordinary models | `T.Fragment.Diff(before, after)` | `Fragment` diff |
| Replay a `Fragment` diff | `fragment.ApplyChanges(changes)` | `Fragment` |
| Express explicit set/null/remove edits | `new T.Patch { ... }` | `Patch` |
| Apply a `Patch` to a `Fragment` or model | `fragment.Apply(patch)` / `patch.ApplyTo(model)` | `Fragment` / new model |
| Compose local operations | `patch.Compose(next)` | `Patch` |
| Compare ordinary present models | `beforeModel.CreateChangeSet(afterModel)` | `ChangeSet` |
| Compare sparse states (including missing/null roots) | `T.ChangeSet.Between(before, after)` | `ChangeSet` |
| Observe a member transition | `changes.Label.IsChanged` / `Before` / `After` | Member transition |
| Project a transition to operations | `changes.ToPatch()` | `Patch` |
| Attach a baseline to a patch | `T.ChangeSet.FromPatch(baseline, patch)` | `ChangeSet` |
| Reverse or chain transitions | `changes.Invert()` / `first.Compose(second)` | `ChangeSet` |
| Reconcile against newer state | `changes.TryApplyTo(model, out updated, out conflicts)` / `changes.RebaseOnto(current)` | Updated model or `RebaseResult` |

`Merge` composes layered contributions, `ApplyChanges` replays a `Diff` fragment, and `Apply` executes `Patch` operations. Unlike `ApplyChanges`, a `Patch` can explicitly `.Remove()` a member back to `Missing`.
