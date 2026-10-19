# Tutorial 3: Layer settings and remove overrides

In this tutorial, you will define partial configuration layers with `Fragment`, distinguish omitted properties from explicit `null` overrides during `Merge`, clear an override with `Patch.Remove()` so the lower layer shows through again, and materialize the merged result into an ordinary C# object with `ToModel()`.

## Prerequisites and setup

This tutorial stands on its own and does not require `EditSession`. You need the .NET 8 SDK or later and a console project referencing `SparseTypes`:

```shell
dotnet new console -o SparseTutorial --framework net8.0
dotnet add SparseTutorial package SparseTypes
```

Use the `UserSettings` model at the bottom of `Program.cs`:

<!-- illustrative: UserSettings model declaration -->
```csharp
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public int RefreshInterval { get; set; }
}
```

## Step 1: Merge layers and compare missing vs explicit null

In an ordinary `UserSettings` object, a `null` property value is ambiguous: you cannot tell whether a configuration layer omitted the property or explicitly set it to `null`. `UserSettings.Fragment` wraps each property in `Optional<T>`, which distinguishes three states:

1. `Missing` (`IsPresent == false`): the layer makes no statement, so a lower layer's value falls through.
2. `Present(null)` (`IsPresent == true`, `Value == null`): the layer explicitly clears the value to `null`, overriding lower layers.
3. `Present(value)`: the layer sets a non-null value, overriding lower layers.

Call `lower.Merge(higher)` to overlay a higher-priority fragment onto a lower-priority fragment:

<!-- sample: tutorial-fragment-patch -->
```csharp
using SparseTypes;

// 1. Define a default layer and a user override layer
var defaultLayer = new UserSettings.Fragment
{
    DisplayName = "Guest",
    Theme = "light",
    RefreshInterval = 30,
};

// RefreshInterval is omitted (Missing), DisplayName is explicitly null, and Theme is "dark"
var userOverride = new UserSettings.Fragment
{
    DisplayName = (string?)null,
    Theme = "dark",
};

var effective = defaultLayer.Merge(userOverride);
Console.WriteLine(
    $"Merged -> DisplayName present: {effective.DisplayName.IsPresent} (value: {effective.DisplayName.Value ?? "null"}), Theme: {effective.Theme.Value}, RefreshInterval: {effective.RefreshInterval.Value}"
);
// Output:
// Merged -> DisplayName present: True (value: null), Theme: dark, RefreshInterval: 30
```
<!-- /sample -->

Because `userOverride` left `RefreshInterval` unassigned (`Missing`), the default value `30` falls through. Because `userOverride` assigned `(string?)null` to `DisplayName`, `effective.DisplayName` is `Present(null)` and overrides `"Guest"`.

## Step 2: Remove an override with Patch and materialize with ToModel

When a user resets a setting back to the default, setting the override property to `null` would override the default with `null`. Instead, create a `UserSettings.Patch` and call `.Remove()` on that property to return the override entry to `Missing`. Then re-merge the layers and call `ToModel()` to produce a standard `UserSettings` instance:

<!-- sample: tutorial-fragment-remove-and-materialize -->
```csharp
// 2. Remove the user's Theme override so the default layer shows through again
var resetTheme = new UserSettings.Patch();
resetTheme.Theme.Remove();

var updatedOverride = userOverride.Apply(resetTheme);
// updatedOverride.Theme.IsPresent == false

var revealed = defaultLayer.Merge(updatedOverride);
var finalSettings = revealed.ToModel();
Console.WriteLine(
    $"After Remove -> Theme: {finalSettings.Theme}, RefreshInterval: {finalSettings.RefreshInterval}"
);
// Output:
// After Remove -> Theme: light, RefreshInterval: 30
```
<!-- /sample -->

Calling `resetTheme.Theme.Remove()` and applying the patch removes `Theme` from `updatedOverride` (`IsPresent == false`). When `defaultLayer.Merge(updatedOverride)` runs, `defaultLayer.Theme` (`"light"`) is revealed again.

> [!NOTE]
> `ToModel()` constructs a new root `UserSettings` instance and copies present values into it. If your model contains reference-type properties (such as a `List<T>` or a custom class not promoted as a nested sparse model), `ToModel()` copies those references without deep-cloning them. Call `finalSettings.DeepClone()` when you need an isolated object graph that can be mutated without affecting the source fragment. See [Cloning and ownership](../explanation/cloning-and-ownership.md).

## Where to go next

* **Multi-tier and nested configuration**: See [Layer settings](../how-to/layered-settings.md) for three-tier merges (`library.Merge(application).Merge(user)`), nested model fragments, and `Fragment.Diff`.
* **Collection merge modes**: Learn how `[SparseMerge(MergeMode.Append)]`, `MergeMode.SetUnion`, and custom `FragmentMergeStrategy<T>` control collection merging in [Merge strategies](../explanation/merge-strategies.md).
* **Core algebra**: Understand how `Fragment`, `Patch`, `ChangeSet`, and `ChangePayload` relate in [Fragments and patches](../explanation/fragments-and-patches.md).
