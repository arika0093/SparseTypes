# SparseTypes.Generator.Shared

Source-only generator infrastructure shared by `SparseTypes.Generator` and downstream generators.

This package ships C# sources only — there is no runtime assembly and no `lib/` assets.
Referencing it compiles the shared sources into the consuming generator via
`build/SparseTypes.Generator.Shared.props` (direct consumer only; no
`buildTransitive/` propagation):

```xml
<PackageReference
  Include="SparseTypes.Generator.Shared"
  Version="0.1.0"
  PrivateAssets="all" />
```

Pin an explicit compatible version. Shared source changes alter the compiled
generator binary, so a floating version range is not recommended for
downstream generators.

`PrivateAssets="all"` is required: the sources are compiled into the
generator itself and must not flow transitively to generator consumers.

Consuming generators additionally need the same compile surface the
sources were written against:

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
