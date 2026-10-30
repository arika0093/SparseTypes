# Available libraries

SparseTypes separates its core source generator from framework integration packages so that domain models stay independent of UI and persistence libraries. You can reference the core package in shared model projects and add framework integrations only in the application layers that use them.

## Choosing packages

Start with `SparseTypes` alone. It generates fragments, patches, `ChangeSet`, `ChangePayload`, observable views, and edit sessions from your `[SparseModel]` classes without adding framework dependencies. It supports `netstandard2.0` (including .NET Framework 4.8) as well as `net6.0` and `net10.0`, and requires C# 9 and Roslyn 4.3.1 or later at build time. If a project only needs in-memory `Fragment` and `Patch` types without edit sessions or wire payloads, use `SparseTypes.Fragments` as the core package. See the [reference overview](../reference/README.md) for the generated types and API surface.

Each optional package connects those generated types to a specific boundary in your application architecture. Add an integration package only in the project that hosts that boundary:

* `SparseTypes.Blazor` targets .NET 10 and provides headless components and form state integration for Blazor applications on .NET 10 or later. See [Blazor edit form](../how-to/blazor-edit-form.md) and [Blazor headless components](../how-to/blazor-headless-components.md).
* `SparseTypes.EntityFrameworkCore` maps `ChangeSet` values onto tracked entities when persisting changes with EF Core 10 on .NET 10 or later. See [EF Core mapping](../how-to/efcore-mapping.md).
* `SparseTypes.OpenApi.AspNetCore` and `SparseTypes.OpenApi.Swashbuckle` register `ChangePayload` schemas when exposing partial-update endpoints in OpenAPI documents on .NET 10 or later. Both packages require `SparseTypes`. See [OpenAPI integration](../how-to/openapi.md).
* Trimmed and NativeAOT deployments require no extra package because `SparseTypes` emits AOT-compatible code and `System.Text.Json` metadata (`{ModelName}.PayloadJson.Default`) directly. See [Use SparseTypes with NativeAOT](../how-to/nativeaot.md) for publishing notes.

## `SparseTypes`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes/)

Provides core editing and type-safe change-tracking functionality without depending on a specific framework.

```bash
dotnet add package SparseTypes --prerelease
```

### Requirements

* A runtime target of `netstandard2.0`, `net6.0`, or `net10.0`
  * NuGet selects the most specific compatible asset.
  * The `netstandard2.0` asset supports `.NET Framework 4.8` and older .NET environments.
* `langVersion >= 9` (C# 9 or later)
  * It can be used in most recent environments.
  * For `.NET Framework 4.8`, add `<LangVersion>9</LangVersion>` (or later) to the csproj.
* A build environment with `Roslyn 4.3.1` or later
  * The source generator requires Roslyn 4.3.1 or later.
  * Examples:
    * Visual Studio 2022 17.3 or later
    * JetBrains Rider 2023.1 or later
    * .NET 6.0.401 SDK or later
    * Unity 6.0 or later
  * This requirement applies to the build environment, not the runtime environment.

## `SparseTypes.Fragments`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.Fragments?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.Fragments/)

Generates `Fragment` and `Patch` types and their in-memory operations. It includes fragment conversion, merge, diff, change application, deep cloning, and patch application, `Between`, `Compose`, and baseline-relative `Invert`.

```bash
dotnet add package SparseTypes.Fragments --prerelease
```

> [!NOTE]
> Choose either `SparseTypes` or `SparseTypes.Fragments` for a consumer compilation. The products use the same model attribute, but mixed-product model graphs and interoperability between their generated types are unsupported.

`SparseTypes.Fragments` does not generate `ChangeSet`, patch `Rebase`, JSON payloads or converters, observable views, edit sessions, or history APIs. Its requirements are the same as for `SparseTypes`.

## `SparseTypes.Blazor`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.Blazor?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.Blazor/)

Provides Blazor features, including headless UI components.

* [Blazor headless components](../how-to/blazor-headless-components.md)
* [Blazor edit form](../how-to/blazor-edit-form.md)

```bash
dotnet add package SparseTypes.Blazor --prerelease
```

### Requirements

* Target framework: `net10.0` (applications targeting .NET 10 or later can reference the package)
* There are no other specific requirements.
  * Server, WebAssembly, and Hybrid Blazor applications are all supported.
  * It does not depend on any UI framework (such as MudBlazor, FluentUI, etc.)


## `SparseTypes.EntityFrameworkCore`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.EntityFrameworkCore?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.EntityFrameworkCore/)

Extensions for using `SparseTypes` with Entity Framework Core.  
Provides functionality for applying, discarding, and mapping changes on entities.

* [EF Core Mapping](../how-to/efcore-mapping.md)

```bash
dotnet add package SparseTypes.EntityFrameworkCore --prerelease
```

### Requirements

* `.NET 10` or later
* `EntityFrameworkCore 10` or later


## `SparseTypes.OpenApi.AspNetCore` / `SparseTypes.OpenApi.Swashbuckle`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.OpenApi.AspNetCore?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.OpenApi.AspNetCore/) [![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.OpenApi.Swashbuckle?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.OpenApi.Swashbuckle/)

These extensions expose `ChangePayload` correctly in OpenAPI documents.  
`.AspNetCore` is for `Microsoft.AspNetCore.OpenApi`, and `.Swashbuckle` is for `Swashbuckle.AspNetCore`.

* [OpenAPI integration](../how-to/openapi.md)

```bash
dotnet add package SparseTypes.OpenApi.AspNetCore --prerelease
# dotnet add package SparseTypes.OpenApi.Swashbuckle --prerelease
```

### Requirements

* `.NET 10` or later
* `SparseTypes` (generates the `ChangePayload` schema metadata)
* `Microsoft.AspNetCore.OpenApi` when using `SparseTypes.OpenApi.AspNetCore`
* `Swashbuckle.AspNetCore` when using `SparseTypes.OpenApi.Swashbuckle`

## For library developers
### `SparseTypes.Core`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.Core?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.Core/)

Shared runtime contracts and helpers used by `SparseTypes` and `SparseTypes.Fragments`. Both product packages depend on Core and provide their own generator. Most applications should install one of those products instead of referencing Core directly.

### `SparseTypes.Generator.Shared`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.Generator.Shared?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.Generator.Shared/)

Source-only generator infrastructure shared by `SparseTypes.Generator` and downstream generators.

This package ships C# sources only; there is no runtime assembly and no `lib/` assets.
Referencing it compiles the shared sources into the consuming generator via `build/SparseTypes.Generator.Shared.props`
(direct consumer only; no `buildTransitive/` propagation):

```xml
<PackageReference Include="SparseTypes.Generator.Shared" Version="0.1.0" PrivateAssets="all" />
```

Pin an explicit compatible version. Shared source changes alter the compiled generator binary,
so a floating version range is not recommended for downstream generators.

`PrivateAssets="all"` is required: the sources are compiled into the generator itself and must not flow transitively to generator consumers.

Consuming generators additionally need the same compile surface the sources were written against:

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <LangVersion>preview</LangVersion>
  <PolyUseEmbeddedAttribute>true</PolyUseEmbeddedAttribute>
  <PolyArgumentExceptions>true</PolyArgumentExceptions>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.1" PrivateAssets="all" />
  <PackageReference Include="Polyfill" Version="11.4.1" PrivateAssets="all" />
</ItemGroup>
```

See [README.md](../../src/SparseTypes.Generator.Shared/README.md).
