<img src="./assets/hero2.png" />

# SparseTypes

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/SparseTypes/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-SparseTypes-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/SparseTypes)

*A unified change model for .NET.*

SparseTypes generates change-tracking, diffing, patching, and merging companion types from your C# models at compile time.

## Overview

When you assign `settings.Theme = "dark"` on an ordinary C# object, the object holds only the latest value. You cannot tell which properties were modified, what the previous value was, whether a `null` value was set intentionally or left out of a request, or how to merge that edit when another user updates `settings.DisplayName` at the same time.

Adding `[SparseModel]` to a `partial` class generates companion types that record and apply those changes without reflection or manual DTO copies:

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

You can track edits on a live object, inspect the before-and-after values of each modified property, and revert to the initial baseline:

<!-- illustrative: edit session usage -->
```csharp
var settings = new Settings { Theme = "light" };
var session = settings.CreateEditSession();

session.Observable.Theme = "dark";
Console.WriteLine(settings.Theme);             // dark (writes through to the model)
Console.WriteLine(session.HasChanges);         // True

var changes = session.CreateChangeSet();
Console.WriteLine(changes.Theme.Before.Value); // light
Console.WriteLine(changes.Theme.After.Value);  // dark
```

You can adopt only the parts you need. Use `EditSession` in a desktop or Blazor form without changing your API, use `ChangePayload` and `RebaseOnto` in a Web API without UI sessions, or use `Fragment` and `Patch` for layered configuration in a console service.

### Generated types and roles

Each `[SparseModel]` type `T` gets companion types tailored to a specific role in the change lifecycle:

| Type | Role | Typical use case |
| --- | --- | --- |
| `T` | Ordinary C# model holding current values | Application domain logic, UI display, and database entities |
| `EditSession` (`T.CreateEditSession()`) | Tracks live mutations against an in-memory baseline | Form dirty state, `RevertChanges()`, and opt-in Undo/Redo (`EnableHistory()`) |
| `T.ChangeSet` | Immutable before-to-after transition (`Before` and `After`) | Diff inspection, `Invert()`, three-way `RebaseOnto()`, and audit logs |
| `T.ChangePayload` | Serializable wire envelope for `System.Text.Json` | Sending a `ChangeSet` or `Patch` across HTTP or message queues |
| `T.Fragment` | Partial snapshot where each property is `Present(value)`, `Present(null)`, or `Missing` | Layered configuration (`lower.Merge(higher)`) and partial state snapshots |
| `T.Patch` | Baseline-free update command (`Set(value)`, `Set(null)`, `Remove()`, or untouched) | Applying blind updates (`ApplyTo`) or clearing overrides without prior state |

## Install

```shell
dotnet add package SparseTypes
```

The package bundles the Roslyn source generator, so building the project generates all companion types automatically.

## Quick Start

With the .NET 10 SDK or later, you can run a complete single-file program with `dotnet run --file`. On the .NET 8 SDK, place the same code in `Program.cs` inside a standard console project (`dotnet new console`).

`CreateEditSession()` retains a snapshot of the initial values as its baseline while `session.Observable` writes directly to the `settings` instance.

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

Save the file as `quickstart.cs` and run it:

```shell
dotnet run --file quickstart.cs
```

Calling `session.RevertChanges()` restores the baseline values into `settings`. Calling `session.AcceptChanges()` advances the in-memory baseline to the current values without performing network or database I/O. If you need an isolated copy before editing, call `settings.DeepClone()` or `session.Fork()` (see [Work with an edit session](docs/how-to/edit-session.md)).

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

`RebaseOnto` compares the client's `Before` values against the server's current state. Edits to different properties merge automatically, while competing edits to the same property return structured conflict descriptions instead of silently overwriting data.

## Choose Your Path

Start with the step-by-step tutorials or jump directly to the guide that matches your task. See the [Documentation Index](docs/README.md) for the full map.

### Step-by-step tutorials
1. [**Tutorial 1: First edit and diff**](docs/tutorial/first-sparse-edit.md): Track live edits with `EditSession`, inspect `ChangeSet` transitions, and revert changes.
2. [**Tutorial 2: Send changes and check conflicts**](docs/tutorial/send-and-rebase-changes.md): Serialize `ChangePayload` over JSON, merge concurrent edits, and detect conflicts with `RebaseOnto`.
3. [**Tutorial 3: Layer settings and remove overrides**](docs/tutorial/layer-and-reset-settings.md): Merge configuration layers with `Fragment` and clear overrides with `Patch`.

### By use case
* **UI forms and draft tracking**:
  * [Work with an edit session](docs/how-to/edit-session.md): Capture snapshots before saving, acknowledge persisted changes, and use `Reload`, `Fork`, or child sessions.
  * [Blazor edit form](docs/how-to/blazor-edit-form.md) and [Blazor headless components](docs/how-to/blazor-headless-components.md): Bind inputs, validate fields, and display conflicts in Blazor.
  * [Coordinate multi-session submissions](docs/how-to/edit-session-groups.md): Submit and acknowledge changes across multiple models together.
* **Web APIs, partial updates, and persistence**:
  * [Accept partial updates](docs/how-to/partial-updates.md): Handle baseline-free `Patch` commands and baseline-aware `ChangeSet` payloads in HTTP endpoints.
  * [EF Core mapping](docs/how-to/efcore-mapping.md): Apply `ChangeSet` transitions to tracked EF Core entities and configure concurrency tokens.
  * [ChangeSet rebase](docs/explanation/rebase.md): Understand three-way comparison outcomes and persistence concurrency control.
* **Layered configuration**:
  * [Layer settings](docs/how-to/layered-settings.md): Combine default, environment, and user fragments and materialize the effective model.
  * [Merge strategies](docs/explanation/merge-strategies.md): Configure deep merge, collection append, set union, and custom equality comparers.

## Try It in the Browser

The [**SparseTypes Playground**](https://arika0093.github.io/SparseTypes/) runs interactive examples in WebAssembly.

* **Start here (Core concepts)**:
  * [Case 1: Layer merge](https://arika0093.github.io/SparseTypes/case-1): Verify how missing properties fall through lower configuration layers while explicit `null` overrides them.
  * [Case 2: Typed patch](https://arika0093.github.io/SparseTypes/case-2): Verify how `Set`, `Set(null)`, and `Remove()` update individual fields without a baseline.
  * [Case 4: Edit session and Undo/Redo](https://arika0093.github.io/SparseTypes/case-4): Verify live dirty tracking, revert, and opt-in Undo/Redo history on a form.
* **Advanced scenarios (Collections, concurrency, and multi-section forms)**:
  * [Case 3: Keyed list](https://arika0093.github.io/SparseTypes/case-3): Verify identity-based list reordering, insertion, and item editing with `[SparseKey]`.
  * [Case 5: EF Core mapping and Rebase](https://arika0093.github.io/SparseTypes/case-5): Verify three-way rebase and conflict reporting when another user saves while your edit is in flight.
  * [Case 6: Enterprise master editing](https://arika0093.github.io/SparseTypes/case-6): Verify independent section save scopes and sortable work steps on a complex record.

## Reference and Architecture

* **Explanation**: [Fragments and patches](docs/explanation/fragments-and-patches.md) | [Edit sessions and state boundaries](docs/explanation/edit-sessions.md) | [Keyed collections](docs/explanation/keyed-collections.md) | [Cloning and ownership](docs/explanation/cloning-and-ownership.md) | [UI frameworks](docs/explanation/ui-frameworks.md) | [Edit-session groups](docs/explanation/edit-session-groups.md)
* **API reference**: [Index](docs/reference/README.md) | [Package APIs](docs/reference/README.md#package-apis) | [Generated APIs](docs/reference/README.md#generated-apis)
* **Specifications**: [Model shapes](docs/reference/model-shapes.md) | [ChangePayload wire format](docs/reference/change-payload.md) | [Edit session state & observers](docs/reference/edit-session-state.md) | [Descriptors](docs/reference/descriptors.md) | [EF Core mapping reference](docs/reference/efcore-mapping.md) | [Diagnostics (`SPF001`-`SPF035`)](docs/reference/analyzer.md)

## Packages and Compatibility

| Package | Target frameworks | Description |
| --- | --- | --- |
| `SparseTypes` | `netstandard2.0` (.NET 6+, .NET 8+, .NET 10+, .NET Framework 4.7.2+) | Core runtime and Roslyn source generator (requires Roslyn 4.3.1+ and C# 9.0+). Reflection-free and Native AOT verified. Emits `System.Text.Json` payload support when `System.Text.Json` is referenced. |
| `SparseTypes.Blazor` | `net8.0`, `net10.0` | `EditContext` integration, validation stores, and headless field/collection components for Blazor. |
| `SparseTypes.EntityFrameworkCore` | `net10.0` | `ChangeSet` to EF Core `DbContext` entity mapping and navigation synchronization. |

## License

Licensed under the Apache-2.0 License.
