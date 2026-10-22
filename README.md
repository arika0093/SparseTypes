<img src="./assets/hero2.png" />

# SparseTypes

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/SparseTypes/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-SparseTypes-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/SparseTypes)

*A unified change model for .NET.*

SparseTypes generates change-tracking, diffing, patching, and merging companion types from C# models.

## Motivation

Consider a case where you directly edit `settings.Theme = "dark"`.  
In that situation, information about the previous value and exactly where it changed is lost.  
This makes it inconvenient to track edit history or update the UI based on the changed parts.

Additionally, consider a scenario where multiple people are editing the same settings.  
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
    public string? Theme { get; set; }
    public DatabaseSettings? Database { get; set; }
}

// Reachable nested models are also marked as partial.
public partial class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
}
```

Behind the scenes, [many classes are generated](./docs/reference/generated-api.md), but users don't need to be aware of it.

Let's take a look at how it can be used.

## Quick Start
### Simple example

With the .NET 10 SDK or later, you can run a complete single-file program.  
Save the file as `quickstart.cs` and run it: `dotnet run quickstart.cs`. 

<!-- sample: readme-quickstart -->
```csharp
#:package SparseTypes@*

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

Console.WriteLine(settings.Label); // production
Console.WriteLine(session.HasChanges); // True
Console.WriteLine(changes.Label.IsChanged); // True
Console.WriteLine(changes.Database.Port.Before.Value); // 5432
Console.WriteLine(changes.Database.Port.After.Value); // 7432

// 4. Revert edits back to the session baseline
session.RevertChanges();
Console.WriteLine(session.HasChanges); // False
Console.WriteLine(settings.Label); // default

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
    * For each value you can check whether it changed (`changes.X.IsChanged`) and, if so, get the before/after values (`changes.X.Before|After.Value`).
    * You can also collect all changed locations as an array (`changes.EnumerateChanges`).
1. Revert changes with `RevertChanges`.
    * This rolls back to the values at the start (step 1).
    * You can also accept changes (`AcceptChanges`).

Of course, there is much more that didn't fit here! For example...

* [Undo/redo](docs/how-to/edit-session.md#record-undo-and-redo-history) user changes.
* [Create a fork](docs/how-to/edit-session.md#branch-speculative-edits-with-fork) for tentative changes. Once done, merge it or discard it.
* [Reload from the latest server state](docs/how-to/edit-session.md#reload-with-fresh-server-state) (without discarding changes!)
* Automatically apply these changes to EF Core [entities](docs/how-to/efcore-mapping.md).
* By [declaring key columns in advance](docs/explanation/keyed-collections.md), even complex cases like arrays of arrays of classes work without issues.

### Wire transport and concurrent rebase

When changes cross process boundaries, serialize the `ChangeSet` to a `ChangePayload` and reconcile it against the latest server state:

<!-- illustrative: excerpt; crosses process boundaries with System.Text.Json -->
```csharp
// 1. Serialize the typed change set for wire transport
var json = JsonSerializer.Serialize(changes.ToPayload());
// 2. Deserialize on the server
var incoming = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!.ToChangeSet();
// 3. Rebase incoming changes onto latest server state
var rebased = incoming.RebaseOnto(updated);
```

`RebaseOnto` works like `git rebase`: it reports conflicts where they occur and merges everything else automatically.
With [`ChangePayload`](docs/reference/change-payload.md) and [`Rebase`](docs/explanation/rebase.md), partial changes fit naturally even in environments where the client and server are separated.

## Install

Install the `SparseTypes` package from [NuGet](https://www.nuget.org/packages/SparseTypes/):

```shell
dotnet add package SparseTypes
```

We also provide several other libraries. For details, see [here](docs/explanation/libraries.md).

## Learn more

See the [Documentation Index](docs/README.md) for the full map.

## License

Licensed under the Apache-2.0 License.
