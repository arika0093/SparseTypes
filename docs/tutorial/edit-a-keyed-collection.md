# Tutorial 4: Edit a keyed collection

In this tutorial, you will declare a collection model whose elements are matched by `[SparseKey(Unassigned = 0)]`, diff a roster after adding an item, inspect an in-place property edit combined with a reorder, remove an item, and track a pending row with `[SparseTemporaryKey]` before its permanent key is assigned.

## Prerequisites and setup

You need the .NET 8 SDK or later and a console project referencing `SparseTypes`:

```shell
dotnet new console -o SparseTutorial --framework net8.0
dotnet add SparseTutorial package SparseTypes
```

Open `SparseTutorial/Program.cs`. Place the `using` directives and executable statements at the top of `Program.cs` and the model classes at the bottom.

## Define the model

Add `TutorialQuest` and `TutorialRoster` at the bottom of `Program.cs`. Marking `TutorialQuest.Id` with `[SparseKey(Unassigned = 0)]` tells the source generator to match `Quests` elements by `Id` across diffs and patches and treat `0` as an unassigned key for newly created rows. Marking `TemporaryId` with `[SparseTemporaryKey]` gives unassigned rows a client-side `Guid` identity until the server assigns their permanent `Id`:

<!-- sample: tutorial-roster-models -->
```csharp
public partial class TutorialQuest
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int Points { get; set; }
}

[SparseModel]
public partial class TutorialRoster
{
    public List<TutorialQuest> Quests { get; set; } = new();
}
```
<!-- /sample -->

`TutorialRoster` carries `[SparseModel]`, and `TutorialQuest` is a `partial` class that the generator promotes into a nested sparse model because it appears in `List<TutorialQuest>`.

## Step 1: Add an item and inspect the addition

Create a `before` roster with one quest (`Id = 1`) and an `after` roster that appends a second quest (`Id = 2`). Call `before.CreateChangeSet(after)` to capture the collection transition:

<!-- sample: tutorial-roster-add -->
```csharp
using System.Linq;
using SparseTypes;

var before = new TutorialRoster
{
    Quests = new List<TutorialQuest>
    {
        new() { Id = 1, Title = "Gather herbs", Points = 10 },
    },
};

var after = new TutorialRoster
{
    Quests = new List<TutorialQuest>
    {
        new() { Id = 1, Title = "Gather herbs", Points = 10 },
        new() { Id = 2, Title = "Repair bridge", Points = 25 },
    },
};

// 1. Diff the two rosters to inspect the added quest
var arrived = before.CreateChangeSet(after);
var q2Change = arrived.Quests.GetChange(2);
Console.WriteLine(
    $"Quest 2 IsAdded: {q2Change.IsAdded}, Added Id: {arrived.Quests.Added.Single().Id}"
);
// Output:
// Quest 2 IsAdded: True, Added Id: 2
```
<!-- /sample -->

`arrived.Quests.GetChange(2)` looks up the transition for key `2` and reports `IsAdded == true`. `arrived.Quests.Added` lists the fragments for newly added elements.

## Step 2: Edit an item and reorder the list

Because elements are matched by `Id`, changing an element's position and editing its properties in the same step produces an in-place edit plus an order update. Create a `reordered` roster that places quest `2` first and updates the title of quest `1`:

<!-- sample: tutorial-roster-edit -->
```csharp
// 2. Retitle quest 1 and swap the quest order
var reordered = new TutorialRoster
{
    Quests = new List<TutorialQuest>
    {
        new() { Id = 2, Title = "Repair bridge", Points = 25 },
        new() { Id = 1, Title = "Gather healing herbs", Points = 10 },
    },
};

var edited = after.CreateChangeSet(reordered);
var q1Change = edited.Quests.GetChange(1);
Console.WriteLine(
    $"Quest 1 IsEdited: {q1Change.IsEdited}, Title: {q1Change.Edit.Title.After.Value}, OrderChanged: {edited.Quests.OrderChanged}, AfterOrder: [{string.Join(", ", edited.Quests.AfterOrder)}]"
);
// Output:
// Quest 1 IsEdited: True, Title: Gather healing herbs, OrderChanged: True, AfterOrder: [2, 1]
```
<!-- /sample -->

`q1Change.IsEdited` is `true`, and `q1Change.Edit` is a nested `TutorialQuest.ChangeSet` whose `Title.After.Value` is `"Gather healing herbs"` while `Points` remains unchanged. `edited.Quests.OrderChanged` is `true` and `AfterOrder` records the new key sequence `[2, 1]`.

## Step 3: Remove an item

Create a `trimmed` roster that drops quest `2` and diff against `reordered`:

<!-- sample: tutorial-roster-remove -->
```csharp
// 3. Remove quest 2 from the roster
var trimmed = new TutorialRoster
{
    Quests = new List<TutorialQuest>
    {
        new() { Id = 1, Title = "Gather healing herbs", Points = 10 },
    },
};

var removed = reordered.CreateChangeSet(trimmed);
Console.WriteLine($"Quest 2 IsRemoved: {removed.Quests.GetChange(2).IsRemoved}");
// Output:
// Quest 2 IsRemoved: True
```
<!-- /sample -->

`removed.Quests.GetChange(2).IsRemoved` is `true`, and `removed.Quests.GetChange(1).IsEmpty` is `true` because quest `1` had no property changes in this step.

## Step 4: Add a pending item with a temporary key

When the client creates a new quest before the database assigns its permanent `Id`, leave `Id` at its unassigned default (`0`) and assign `TemporaryId = Guid.NewGuid()`. Diff `trimmed` against a roster that adds the pending quest:

<!-- sample: tutorial-roster-temporary -->
```csharp
// 4. Add a pending quest with an unassigned Id and a temporary key
var tempId = Guid.NewGuid();
var withPending = new TutorialRoster
{
    Quests = new List<TutorialQuest>
    {
        new() { Id = 1, Title = "Gather healing herbs", Points = 10 },
        new() { TemporaryId = tempId, Title = "Scout watchtower", Points = 15 },
    },
};

var pending = trimmed.CreateChangeSet(withPending);
// pending.Quests.GetTemporaryChange(tempId).IsAdded == true
var pendingTitlePath = TutorialRoster.SparsePath.Quests.TemporaryKey(tempId).Title;
Console.WriteLine(
    $"Pending IsAdded: {pending.Quests.GetTemporaryChange(tempId).IsAdded}, Path: {pendingTitlePath}"
);
// Output:
// Pending IsAdded: True, Path: Quests[temp:"<tempId>"].Title
```
<!-- /sample -->

`pending.Quests.GetTemporaryChange(tempId).IsAdded == true` confirms the unassigned row is tracked by its temporary `Guid`, and `TutorialRoster.SparsePath.Quests.TemporaryKey(tempId).Title` addresses the pending row's `Title` regardless of list position. After saving, the server echoes `TemporaryId` alongside the newly assigned `Id`, and the client session correlates them with `TryReconcile`; see [Edit keyed collections](../how-to/edit-collections.md#save-pending-additions) and [Keyed collections](../explanation/keyed-collections.md#saving-pending-additions-with-reconcile).

## Where to go next

* Continue to [Tutorial 5: Resolve a rebase conflict](resolve-a-conflict.md) to choose per-property winners when concurrent edits collide.
* See [Edit keyed collections](../how-to/edit-collections.md#save-pending-additions) to mutate rows through `session.EditCollection` and reconcile database-assigned `[SparseTemporaryKey]` rows after save.
* Read [Keyed collections](../explanation/keyed-collections.md#saving-pending-additions-with-reconcile) for composite keys, unassigned key defaults, and collection transition rules.
