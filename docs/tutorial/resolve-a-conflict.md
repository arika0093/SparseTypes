# Tutorial 5: Resolve a rebase conflict

In this tutorial, you will detect a conflicting concurrent edit with `RebaseOnto`, choose a winner for the colliding property path with `ChangeSet.BeginResolution`, and build a resolved `ChangeSet` to apply onto the server's current state.

## Prerequisites and setup

This tutorial uses the `UserSettings` model from [Tutorial 2: Send changes and check conflicts](send-and-rebase-changes.md). You need the .NET 8 SDK or later and a console project referencing `SparseTypes`:

```shell
dotnet new console -o SparseTutorial --framework net8.0
dotnet add SparseTutorial package SparseTypes
```

Keep the `UserSettings` model declaration at the bottom of `Program.cs`:

<!-- illustrative: same UserSettings model from Tutorial 1 -->
```csharp
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public int RefreshInterval { get; set; }
}
```

## Step 1: Detect the conflict during rebase

Suppose a client started from a baseline where `Theme` was `"light"` and changed `Theme` to `"dark"`. Meanwhile, another write updated `serverCurrent.Theme` to `"high-contrast"` and `RefreshInterval` to `120`. Call `incoming.RebaseOnto(serverCurrent)` to inspect the collision:

<!-- sample: tutorial-resolve-detect -->
```csharp
using SparseTypes;

var baseline = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};
var incoming = baseline.CreateChangeSet(
    new UserSettings
    {
        DisplayName = "Alice",
        Theme = "dark",
        RefreshInterval = 60,
    }
);

var serverCurrent = new UserSettings
{
    DisplayName = "Alice",
    Theme = "high-contrast",
    RefreshInterval = 120,
};

// 1. Rebase the incoming ChangeSet onto the server's current state
var conflicted = incoming.RebaseOnto(serverCurrent);
var conflict = conflicted.Conflicts[0];
Console.WriteLine(
    $"HasConflicts: {conflicted.HasConflicts}, Path: {conflict.PathText}, base={conflict.BaseValue.Value}, local={conflict.LocalValue.Value}, current={conflict.CurrentValue.Value}"
);
// Output:
// HasConflicts: True, Path: Theme, base=light, local=dark, current=high-contrast
```
<!-- /sample -->

Because `serverCurrent.Theme` (`"high-contrast"`) no longer matches `incoming.Theme.Before` (`"light"`), `conflicted.HasConflicts` is `true` and `conflict` reports `PathText == "Theme"` along with the three endpoints (`BaseValue`, `LocalValue`, and `CurrentValue`).

## Step 2: Decide the winner for the conflicting path

Open a resolution builder by calling `UserSettings.ChangeSet.BeginResolution(conflicted, serverCurrent)`. Check whether `UserSettings.SparsePath.Theme` has a conflict with `HasConflict`, and call `UseIncoming` to select the client's `"dark"` value:

<!-- sample: tutorial-resolve-decide -->
```csharp
// 2. Open a resolution builder and choose the incoming value for Theme
var resolution = UserSettings.ChangeSet.BeginResolution(conflicted, serverCurrent);
var themePath = UserSettings.SparsePath.Theme;

if (resolution.HasConflict(themePath))
{
    resolution.UseIncoming(themePath);
}

Console.WriteLine(
    $"HasConflict(Theme): {resolution.HasConflict(themePath)}, Unresolved count: {resolution.EnumerateUnresolvedConflicts().Count}"
);
// Output:
// HasConflict(Theme): True, Unresolved count: 0
```
<!-- /sample -->

`resolution.UseIncoming(themePath)` records a decision to overwrite the server's `"high-contrast"` value with the incoming `"dark"` value, reducing `EnumerateUnresolvedConflicts().Count` to `0`.

## Step 3: Build and apply the resolved ChangeSet

Once every conflict has a decision, call `resolution.TryBuild(out var resolved, out var failure)` to produce a conflict-free `ChangeSet` based on `serverCurrent`, then apply it with `ToPatch().ApplyTo(serverCurrent)`:

<!-- sample: tutorial-resolve-apply -->
```csharp
// 3. Build the resolved ChangeSet and apply it to the server state
var built = resolution.TryBuild(out var resolved, out var failure);
var updated = resolved.ToPatch().ApplyTo(serverCurrent);
Console.WriteLine(
    $"TryBuild: {built}, Theme: {updated.Theme}, RefreshInterval: {updated.RefreshInterval}"
);
// Output:
// TryBuild: True, Theme: dark, RefreshInterval: 120
```
<!-- /sample -->

`TryBuild` returns `true` and sets `failure` to `null` because all conflicts were decided. Applying `resolved` updates `updated.Theme` to `"dark"` while preserving `serverCurrent.RefreshInterval` (`120`).

## Where to go next

* See [Resolve rebase conflicts](../how-to/resolve-conflicts.md) for `TryApplyTo` save branches, `session.Reload`, fork merges, and conditional-write retry loops.
* Read [ChangeSet rebase](../explanation/rebase.md) for three-way comparison rules, conflict kinds, and custom rebase policies.
