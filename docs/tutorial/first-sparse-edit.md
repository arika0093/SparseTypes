# Tutorial 1: First edit and diff

In this tutorial, you will track live mutations on a scalar C# model with `EditSession`, inspect the before-and-after values of modified properties with `ChangeSet`, and restore the original state when edits are reverted or set back to their initial value.

## Prerequisites and setup

You need the .NET 8 SDK or later. Create a new console project and install the `SparseTypes` package:

```shell
dotnet new console -o SparseTutorial --framework net8.0
dotnet add SparseTutorial package SparseTypes
```

Open `SparseTutorial/Program.cs`. As you work through the steps below, place the `using` directives and executable statements at the top of `Program.cs` and the `UserSettings` class declaration at the bottom (C# requires top-level statements to precede type declarations). A complete `Program.cs` is provided at the end of this tutorial.

## Define the model

Add `[SparseModel]` to a standard C# `partial` class at the bottom of `Program.cs`:

<!-- sample: tutorial-models -->
```csharp
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }

    public string? Theme { get; set; }

    public int RefreshInterval { get; set; }
}
```
<!-- /sample -->

When you build the project, the source generator emits companion types (`EditSession`, `ChangeSet`, `ChangePayload`, `Fragment`, and `Patch`) nested inside `UserSettings`.

## Step 1: Start an edit session and mutate the model

Create a `UserSettings` instance and call `CreateEditSession()`. `CreateEditSession()` captures a snapshot of the initial property values as its comparison baseline, while `session.Observable` writes directly through to the `settings` instance.

Add this code at the top of `Program.cs`:

<!-- sample: tutorial-editsession -->
```csharp
using SparseTypes;

var settings = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};

// 1. Start an edit session (retains the initial baseline)
var session = settings.CreateEditSession();

session.Observable.Theme = "dark";
Console.WriteLine($"Live Theme: {settings.Theme}, HasChanges: {session.HasChanges}");
// Output: Live Theme: dark, HasChanges: True
```
<!-- /sample -->

Assigning `session.Observable.Theme = "dark"` updates `settings.Theme` in place and raises `PropertyChanged` notifications. Because `settings.Theme` (`"dark"`) now differs from the session's retained baseline (`"light"`), `session.HasChanges` evaluates to `true`.

## Step 2: Inspect the transition with ChangeSet

Call `session.CreateChangeSet()` to capture the difference between the session baseline and the current model values:

<!-- sample: tutorial-changeset -->
```csharp
// 2. Inspect what changed
var changes = session.CreateChangeSet();

Console.WriteLine(
    $"Theme changed: {changes.Theme.IsChanged} ({changes.Theme.Before.Value} -> {changes.Theme.After.Value})"
);
Console.WriteLine($"RefreshInterval changed: {changes.RefreshInterval.IsChanged}");
// Output:
// Theme changed: True (light -> dark)
// RefreshInterval changed: False
```
<!-- /sample -->

Each property on `UserSettings.ChangeSet` exposes an `IsChanged` flag along with typed `Before` and `After` values. Because `RefreshInterval` was not modified, `changes.RefreshInterval.IsChanged` is `false`.

## Step 3: Restore the original value or revert changes

`EditSession` tracks semantic differences against its baseline rather than counting how many times a setter was called. If you set a property back to its baseline value, `session.HasChanges` returns to `false`. You can also call `session.RevertChanges()` to restore all modified properties on `settings` back to the session baseline at once:

<!-- sample: tutorial-revert-and-restore -->
```csharp
// 3. Setting the property back to its baseline value clears HasChanges
session.Observable.Theme = "light";
Console.WriteLine($"After restoring 'light', HasChanges: {session.HasChanges}");
// Output: After restoring 'light', HasChanges: False

// Edit two properties and revert them back to the baseline at once
session.Observable.Theme = "solarized";
session.Observable.RefreshInterval = 120;
session.RevertChanges();
Console.WriteLine(
    $"After RevertChanges -> Theme: {settings.Theme}, RefreshInterval: {settings.RefreshInterval}, HasChanges: {session.HasChanges}"
);
// Output: After RevertChanges -> Theme: light, RefreshInterval: 60, HasChanges: False
```
<!-- /sample -->

When you want to keep the current edits and treat them as the new starting point for future comparisons, call `session.AcceptChanges()`. `AcceptChanges()` updates only the in-memory baseline; it does not send data over the network or save to a database.

## Complete runnable Program.cs

Replace `SparseTutorial/Program.cs` with the complete program below and run `dotnet run --project SparseTutorial`:

<!-- sample: tutorial-program -->
```csharp
using SparseTypes;

// 1. Start an edit session (retains the initial baseline)
var settings = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};
var session = settings.CreateEditSession();

session.Observable.Theme = "dark";
Console.WriteLine($"Live Theme: {settings.Theme}, HasChanges: {session.HasChanges}");

// 2. Inspect what changed
var changes = session.CreateChangeSet();
Console.WriteLine(
    $"Theme changed: {changes.Theme.IsChanged} ({changes.Theme.Before.Value} -> {changes.Theme.After.Value})"
);
Console.WriteLine($"RefreshInterval changed: {changes.RefreshInterval.IsChanged}");

// 3. Setting the property back to its baseline value clears HasChanges
session.Observable.Theme = "light";
Console.WriteLine($"After restoring 'light', HasChanges: {session.HasChanges}");

// Edit two properties and revert them back to the baseline at once
session.Observable.Theme = "solarized";
session.Observable.RefreshInterval = 120;
session.RevertChanges();
Console.WriteLine(
    $"After RevertChanges -> Theme: {settings.Theme}, RefreshInterval: {settings.RefreshInterval}, HasChanges: {session.HasChanges}"
);

[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public int RefreshInterval { get; set; }
}
```
<!-- /sample -->

Running the program prints:

```text
Live Theme: dark, HasChanges: True
Theme changed: True (light -> dark)
RefreshInterval changed: False
After restoring 'light', HasChanges: False
After RevertChanges -> Theme: light, RefreshInterval: 60, HasChanges: False
```

## Where to go next

* **Next tutorial**: Continue to [Tutorial 2: Send changes and check conflicts](send-and-rebase-changes.md) to serialize a `ChangeSet` as JSON and reconcile concurrent server edits.
* **Configuration tutorial**: If you only need layered configuration and overrides, jump to [Tutorial 3: Layer settings and remove overrides](layer-and-reset-settings.md).
* **UI editing and Undo/Redo**: Learn how to enable opt-in Undo/Redo (`EnableHistory()`), capture save snapshots, and use `Reload` or `Fork` in [Work with an edit session](../how-to/edit-session.md), or bind forms in [Blazor edit form](../how-to/blazor-edit-form.md).
* **Collections**: When your model includes lists of items, start with fixed keys (`[SparseKey]`) in [Keyed collections](../explanation/keyed-collections.md) before introducing database-assigned temporary keys (`[SparseTemporaryKey]`).

