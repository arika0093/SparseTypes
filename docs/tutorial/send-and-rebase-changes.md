# Tutorial 2: Send changes and check conflicts

In this tutorial, you will serialize a `ChangeSet` as a JSON `ChangePayload`, deserialize it on the receiving side, merge disjoint concurrent edits onto a newer server model, and detect conflicting edits when two writers modify the same property.

## Prerequisites and setup

This tutorial builds on the `UserSettings` model from [Tutorial 1: First edit and diff](first-sparse-edit.md) and runs in a single console process with no external server or database. You need the .NET 8 SDK or later and a console project referencing `SparseTypes`:

```shell
dotnet new console -o SparseTutorial --framework net8.0
dotnet add SparseTutorial package SparseTypes
```

Use the same `UserSettings` model at the bottom of `Program.cs`:

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

## Step 1: Serialize and deserialize a ChangePayload

Start from a client baseline where `DisplayName` is `"Alice"` and `Theme` is `"light"`. Edit `Theme` to `"dark"`, capture the `ChangeSet`, and convert it to `UserSettings.ChangePayload` for `System.Text.Json`:

<!-- sample: tutorial-payload -->
```csharp
using System.Text.Json;
using SparseTypes;

var clientSettings = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};
var clientSession = clientSettings.CreateEditSession();
clientSession.Observable.Theme = "dark";

// 1. Serialize the transition to a JSON wire payload
var outgoingChanges = clientSession.CreateChangeSet();
var payload = outgoingChanges.ToPayload();
var json = JsonSerializer.Serialize(payload, UserSettings.PayloadJson.Default);
Console.WriteLine($"Wire JSON: {json}");

// 2. Deserialize on the server into a typed ChangeSet
var receivedPayload = JsonSerializer.Deserialize<UserSettings.ChangePayload>(json, UserSettings.PayloadJson.Default)!;
var incomingChanges = receivedPayload.ToChangeSet();
Console.WriteLine(
    $"Received Theme change: {incomingChanges.Theme.Before.Value} -> {incomingChanges.Theme.After.Value}"
);
// Output:
// Wire JSON: {"version":"0.1","change":{"kind":"update","members":{"theme":{"kind":"replace","before":{"state":"value","value":"light"},"after":{"state":"value","value":"dark"}}}}}
// Received Theme change: light -> dark
```
<!-- /sample -->

The serialized JSON contains only the modified `Theme` member along with its `before` (`"light"`) and `after` (`"dark"`) states. Untouched properties (`DisplayName` and `RefreshInterval`) are omitted from the wire payload.

## Step 2: Merge disjoint concurrent edits

Suppose that while the client was editing `Theme`, another user updated `DisplayName` on the server from `"Alice"` to `"Alice Cooper"`. Call `TryApplyTo` (or `RebaseOnto`) to reconcile the incoming `ChangeSet` against the server's current state:

<!-- sample: tutorial-rebase -->
```csharp
// 3. Reconcile disjoint concurrent changes
var serverCurrent = new UserSettings
{
    DisplayName = "Alice Cooper",
    Theme = "light",
    RefreshInterval = 60,
};

var rebaseResult = incomingChanges.RebaseOnto(serverCurrent);
var merged = rebaseResult.Rebased.ToPatch().ApplyTo(serverCurrent);
Console.WriteLine(
    $"Disjoint rebase -> HasConflicts: {rebaseResult.HasConflicts}, DisplayName: {merged.DisplayName}, Theme: {merged.Theme}"
);
// Output:
// Disjoint rebase -> HasConflicts: False, DisplayName: Alice Cooper, Theme: dark
```
<!-- /sample -->

Because the server's `Theme` (`"light"`) still matches `incomingChanges.Theme.Before` (`"light"`), the client's `"dark"` edit replays cleanly while the server's `"Alice Cooper"` edit on `DisplayName` stays intact.

## Step 3: Detect overlapping edits and stop on conflict

Now suppose another writer changed `Theme` on the server to `"high-contrast"` before the client's `"light" -> "dark"` change arrived. Both writers modified `Theme` from the `"light"` baseline to different values:

<!-- sample: tutorial-rebase-conflict -->
```csharp
// 4. Detect an overlapping edit on the same property
var serverConflicting = new UserSettings
{
    DisplayName = "Alice Cooper",
    Theme = "high-contrast",
    RefreshInterval = 60,
};

var conflictResult = incomingChanges.RebaseOnto(serverConflicting);
if (conflictResult.HasConflicts)
{
    var conflict = conflictResult.Conflicts[0];
    Console.WriteLine(
        $"Conflict at {conflict.PathText}: base={conflict.BaseValue.Value}, local={conflict.LocalValue.Value}, current={conflict.CurrentValue.Value}"
    );
}
// Output:
// Conflict at Theme: base=light, local=dark, current=high-contrast
```
<!-- /sample -->

When `conflictResult.HasConflicts` is `true`, your endpoint should stop without writing to the database and return the structured conflicts (or a `409 Conflict` response) to the caller.

> [!IMPORTANT]
> Rebase reconciles property values in memory against the server row you loaded; it does not lock the database row. When you persist the merged result to a database, guard the `UPDATE` with your store's concurrency token (such as an EF Core `[Timestamp]` or version column) so a write that arrives between your read and save is caught. See [ChangeSet rebase](../explanation/rebase.md) and [EF Core mapping](../how-to/efcore-mapping.md).

## Where to go next

* **Next tutorial**: Continue to [Tutorial 3: Layer settings and remove overrides](layer-and-reset-settings.md) to merge configuration fragments and reset user overrides.
* **Web API partial updates**: See [Accept partial updates](../how-to/partial-updates.md) for full request-to-response handling, validation, and write-only secrets (`[SparseRedactBefore]`).
* **Database persistence**: See [EF Core mapping](../how-to/efcore-mapping.md) to apply rebased `ChangeSet` values to tracked EF Core entities on .NET 10.
* **Conflict resolution**: Learn how to resolve conflicts path by path or customize rebase policies in [ChangeSet rebase](../explanation/rebase.md).
