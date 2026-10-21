# Available libraries

The following libraries are available.

## `SparseTypes`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes/)

Provides core editing and type-safe change-tracking functionality without depending on a specific framework.

```bash
dotnet add package SparseTypes
```

### Requirements

* An environment that supports `netstandard2.0`
  * For example, it can be used with `.NET Framework 4.8` and recent .NET environments.
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
dotnet add package SparseTypes.Fragments
```

> [!NOTE]
> Choose either `SparseTypes` or `SparseTypes.Fragments` for a consumer compilation. The products use the same model attribute, but mixed-product model graphs and interoperability between their generated types are unsupported.

`SparseTypes.Fragments` does not generate `ChangeSet`, patch `Rebase`, JSON payloads or converters, observable views, edit sessions, or history APIs. Its requirements are the same as for `SparseTypes`.

## `SparseTypes.Core`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.Core?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.Core/)

Shared runtime contracts and helpers used by `SparseTypes` and `SparseTypes.Fragments`. Both product packages depend on Core and provide their own generator. Most applications should install one of those products instead of referencing Core directly.

Core targets `netstandard2.0` and has no source generator.

### Requirements

The requirements are the same as for `SparseTypes`.


## `SparseTypes.Blazor`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.Blazor?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.Blazor/)

Provides features for Blazor, including convenient headless UI components.

* [Blazor headless components](../how-to/blazor-headless-components.md)
* [Blazor edit form](../how-to/blazor-edit-form.md)

```bash
dotnet add package SparseTypes.Blazor
```

### Requirements

* `.NET 8` or later
* There are no other specific requirements.
  * Server, WebAssembly, and Hybrid Blazor applications are all supported.
  * It does not depend on any UI framework (such as MudBlazor, FluentUI, etc.)


## `SparseTypes.EntityFrameworkCore`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.EntityFrameworkCore?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.EntityFrameworkCore/)

Extensions for using `SparseTypes` with Entity Framework Core.  
Provides functionality for applying, discarding, and mapping changes on entities.

* [EF Core Mapping](../how-to/efcore-mapping.md)

```bash
dotnet add package SparseTypes.EntityFrameworkCore
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
dotnet add package SparseTypes.OpenApi.AspNetCore
# dotnet add package SparseTypes.OpenApi.Swashbuckle
```

## For library developers

### `SparseTypes.Generator.Shared`

[![NuGet Version](https://img.shields.io/nuget/v/SparseTypes.Generator.Shared?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseTypes.Generator.Shared/)

Source-only generator infrastructure shared by `SparseTypes.Generator` and downstream generators.

This package ships C# sources only — there is no runtime assembly and no `lib/` assets.
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

More details here: [README.md](../../src/SparseTypes.Generator.Shared/README.md)
