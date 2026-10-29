<img src="./assets/hero2.png" />

# SparseTypes

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/SparseTypes/test.yaml?branch=main&label=Test&style=flat-square)

*Build complex .NET editors with less change-management code.*

SparseTypes generates typed edit sessions and change sets from C# models.  
Track edits, undo and redo, and reconcile concurrent changes.  
Use the same change model to apply accepted changes to EF Core entities.

Try the [Enterprise editing demo](https://sparsetypes.eclairs.cc/enterprise-editing) or the [EF Core mapping demo](https://sparsetypes.eclairs.cc/ef-mapping).

## Introduction
### Motivation

Business applications often need more than a form that loads and saves a record.
Tabs hold independent drafts. Detail rows can be added, removed, and reordered before they have database IDs.  
Users need to review changes, undo mistakes, and handle updates made by someone else.

The application must track changed values, their original values, new rows, and saved edits.  
A dirty flag alone cannot describe those changes.  
Copying an entire edited DTO onto an entity can overwrite values the user never touched.

Why should we have to implement these features ourselves?  
The DTO type information already exists, so it should be possible to generate the change-management code automatically from that member metadata.
If the mapping information to EF Core entities is available, the subsequent integration should largely be boilerplate.

*SparseTypes does exactly this.*

### Features

SparseTypes generates change-management infrastructure from models annotated with `[SparseModel]`.  
A typed `ChangeSet` describes pending UI edits and can be sent as a JSON payload.  
On the server, rebase it against current state. Map accepted changes onto tracked EF Core entities.

Use it to:

* Keep per-tab drafts and Undo/Redo histories, highlight edits, and review changes before saving.
* Edit keyed child collections, including unsaved rows whose database keys are assigned later.
* Merge non-conflicting edits and expose overlapping changes for explicit conflict resolution.
* Apply accepted changes to EF Core entities.  
  Successful reconciliation adopts the saved state while preserving later edits.

## Quick Start

Use the .NET 10 SDK or later.  
Save this complete program as `quickstart.cs`. Run it with `dotnet run quickstart.cs`.

<!-- sample: readme-quickstart -->
```csharp
#:package SparseTypes@*-*
using SparseTypes;

var order = new Order { CustomerName = "Alice", Quantity = 1 };
var session = order.CreateEditSession();

// Edit through the observable view; the original model is updated too.
session.Observable.CustomerName = "Bob";
Console.WriteLine(order.CustomerName); // Bob
Console.WriteLine(session.HasChanges); // True

// Inspect the difference from the start of the session.
var changes = session.CreateChangeSet();
Console.WriteLine(changes.CustomerName.Before.Value); // Alice
Console.WriteLine(changes.CustomerName.After.Value);  // Bob
Console.WriteLine(changes.Quantity.IsChanged);        // False

// Discard the edits and restore the starting values.
session.RevertChanges();
Console.WriteLine(order.CustomerName); // Alice
Console.WriteLine(session.HasChanges); // False

[SparseModel]
public partial class Order
{
    public string CustomerName { get; set; } = "";
    public int Quantity { get; set; }
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


## Build your editing workflow

| Goal | Start here |
|---|---|
| Track, inspect, and revert edits | [First edit session tutorial](docs/tutorial/first-sparse-edit.md) |
| Add Undo/Redo, child drafts, or refresh without losing pending edits | [Edit-session guide](docs/how-to/edit-session.md) |
| Bind an existing Blazor UI | [Headless Blazor components](docs/how-to/blazor-headless-components.md) |
| Bind WPF or another .NET UI | [UI framework integration](docs/explanation/ui-frameworks.md) |
| Add, remove, and reorder child rows | [Keyed collection guide](docs/how-to/edit-collections.md) |
| Send changes as JSON and merge concurrent edits | [Transport and rebase tutorial](docs/tutorial/send-and-rebase-changes.md) |
| Let users resolve conflicting edits | [Conflict resolution tutorial](docs/tutorial/resolve-a-conflict.md) |
| Persist DTO changes with EF Core | [EF Core mapping guide](docs/how-to/efcore-mapping.md) |

For a client/server example with PostgreSQL persistence, see [EfCoreSample](samples/EfCoreSample/README.md).

## Install

For an existing project:

```shell
dotnet add package SparseTypes --prerelease
```

Start with `SparseTypes` for framework-independent editing and change tracking.  
Add `SparseTypes.Blazor` for Blazor integration.  
Add `SparseTypes.EntityFrameworkCore` for persistence mapping.

See [packages and requirements](docs/explanation/libraries.md).

## Documentation

See the [Documentation Index](docs/README.md) for tutorials, guides, explanations, and API reference.

## License

Licensed under the Apache-2.0 License.
