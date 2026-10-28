<img src="./assets/hero2.png" />

# SparseTypes

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/SparseTypes/test.yaml?branch=main&label=Test&style=flat-square)

*A unified change model for .NET.*

SparseTypes generates change-tracking, diffing, patching, and merging companion types from C# models.

## Motivation
### Overview

Consider a case where you directly edit `settings.Label = "dark"`.
In that situation, information about the previous value and exactly where it changed is lost.
This makes it inconvenient to track edit history or update the UI based on the changed parts.

Now consider multiple people editing the same settings concurrently.
Unrelated changes should be merged automatically, and conflicts should be detected and reported clearly.

Implementing these features required a lot of redundant code. It's tedious, isn't it?

`SparseTypes` solves this by generating companion types for your C# models at compile time to track changes, create diffs, apply patches, and merge updates.

It's simple to use: just add the `[SparseModel]` attribute to your model.  
That's it. All the necessary companion types are generated automatically.

<!-- illustrative: model shape excerpt; attribute and partial declarations only, not a runnable program -->
```csharp
[SparseModel]                 // add this attribute
public partial class Settings // mark the class as partial
{
    public string? Label { get; set; }
    public DatabaseSettings? Database { get; set; }
}

// Reachable nested models are also marked as partial.
public partial class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
}
```

Behind the scenes, [four companion types](docs/explanation/fragments-and-patches.md) are generated automatically.  
You never edit the generated code directly — the `EditSession`/`ChangeSet` APIs below are all you touch.

### What can you build?

Try the demos in the [Playground](https://sparsetypes.eclairs.cc/). You can build complex master-data management web pages commonly found in enterprise applications.

* [Enterprise editing demo](https://sparsetypes.eclairs.cc/enterprise-editing)
* [EFCore mapping example](https://sparsetypes.eclairs.cc/ef-mapping)

You can implement functionality like the following features:

* Manage edit history separately for each tab and save each tab independently.
* Highlight changed sections and enable Undo/Redo.
* Reorder, add, and remove child items in keyed collections.
* Automatically merge changes made by others where possible.
* When conflicts occur, let users choose how to resolve them or fail the operation outright.
* Use these features with any framework through [headless Blazor components](docs/how-to/blazor-headless-components.md).

---

Like it? Let's take a look at how it can be used.

## Quick Start
### Simple example

With the .NET 10 SDK or later, you can run a complete single-file program.
Save the file as `quickstart.cs` and run it: `dotnet run quickstart.cs`.

<!-- sample: readme-quickstart -->
```csharp
#:package SparseTypes@*-*
using SparseTypes;
var settings = new Settings
{
    Label = "default",
    Database = new() { Host = "db.local", Port = 5432 },
};

// 1. Start an edit session (captures the initial baseline)
var session = settings.CreateEditSession();

// 2. Edit through the observable proxy (writes through to settings)
session.Observable.Label = "production";
session.Observable.Database!.Port = 7432;

// 3. Inspect dirty state and typed transitions
var changes = session.CreateChangeSet();
Console.WriteLine(settings.Label);      // production
Console.WriteLine(session.HasChanges);  // True
Console.WriteLine(changes.Label.IsChanged);    // True
Console.WriteLine(changes.Database.IsChanged); // True
Console.WriteLine(changes.Database.Port.Before.Value); // 5432
Console.WriteLine(changes.Database.Port.After.Value);  // 7432
// Enumerate all changed locations
foreach (var change in changes.EnumerateChanges()) {
    Console.WriteLine(
        $"* {change.Path,-14} changed from {change.Before.Value,10} to {change.After.Value,10}");
    // Output:
    // * Database.Port  changed from       5432 to       7432
    // * Label          changed from    default to production
}

// 4. Revert edits back to the session baseline
session.RevertChanges();
Console.WriteLine(session.HasChanges); // False
Console.WriteLine(settings.Label);     // default

// ---------
[SparseModel]
public partial class Settings
{
    public string? Label { get; set; }
    public DatabaseSettings? Database { get; set; }
}
public partial class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
}
```
<!-- /sample -->

### What happened?

Let's walk through it step by step.

1. Start an edit session with `CreateEditSession`.
    * The starting values are captured internally at this point.
1. Update values through `session.Observable`.
    * Editing through this object raises `INotifyPropertyChanged` notifications.
    * This makes it work as-is with UI frameworks such as WPF.
1. Collect the differences with `CreateChangeSet`.
    * For each value you can check whether it changed (`changes.X.IsChanged`).
    * For each changed value you can inspect the previous and current values (`changes.X.Before` and `changes.X.After`).
    * You can also collect all changed locations (`changes.EnumerateChanges`).
1. Revert changes with `RevertChanges`.
    * This rolls back to the values at the start (step 1).
    * You can also accept changes (`AcceptChanges`).

Of course, there is much more that didn't fit here! For example...

* [Undo/redo](docs/how-to/edit-session.md#record-undo-and-redo-history) user changes.
* [Create a fork](docs/how-to/edit-session.md#branch-speculative-edits-with-fork) for tentative changes. Once done, merge it or discard it.
* [Reload with the latest server state](docs/how-to/edit-session.md#reload-with-fresh-server-state) (without discarding unsaved local edits!)
* Automatically apply these changes to EF Core [entities](docs/how-to/efcore-mapping.md).
* By [declaring key properties in advance](docs/explanation/keyed-collections.md), even complex cases like arrays of arrays of classes work without issues.

## Wire transport and concurrent rebase

When changes cross process boundaries, serialize the `ChangeSet` to a `ChangePayload` and reconcile it against the latest server state:

<!-- illustrative: excerpt; crosses process boundaries with System.Text.Json -->
```csharp
// 1. Serialize the typed change set for wire transport
var json = JsonSerializer.Serialize(changes.ToPayload());
// 2. Deserialize on the server
var incoming = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!.ToChangeSet();
// 3. Rebase incoming changes onto the latest server state
var rebased = incoming.RebaseOnto(currentServerState);
```

`RebaseOnto` works like `git rebase`: it reports conflicts where they occur and merges everything else automatically.
With [wire transport](docs/how-to/partial-updates.md) and [`Rebase`](docs/explanation/rebase.md), partial changes fit naturally even in environments where the client and server are separated.

## Install

Install the `SparseTypes` package from [NuGet](https://www.nuget.org/packages/SparseTypes/):

```shell
dotnet add package SparseTypes --prerelease
```

We also provide several other libraries. For details, see [the library overview](docs/explanation/libraries.md).

## Learn more

New to SparseTypes? Start with the [tutorials](docs/README.md#tutorials).

See the [Documentation Index](docs/README.md) for the full map.

## License

Licensed under the Apache-2.0 License.
