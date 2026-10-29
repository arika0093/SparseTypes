# SparseTypes.Generator.Shared

Source-only generator infrastructure shared by `SparseTypes.Generator` and downstream generators.

Generated sequence descriptor resolvers return -1 for unassigned, missing, or ambiguous keys and for empty temporary identities. Generated descriptor mutations on nullable keyed sequences reject duplicate null slots before changing the collection. Descriptor hosts should preserve these resolver results instead of choosing a first match.

Generated payload readers honour `JsonSerializerOptions.PropertyNameCaseInsensitive` for envelope, member, item, and endpoint properties. Duplicate detection uses the same comparison. Non-model item values do not support an `edit` property; readers reject it rather than discarding it. Malformed index, reorder, and temporary-key fields throw `JsonException`.

Scalar enum codecs reject undefined values on read and write. Flags enums accept combinations of declared bits, including zero. These checks apply to the shared scalar codecs and payload codecs, including nullable and wide enum types.

Keyed item snapshot endpoints serialize model values as ordinary JSON values. OpenAPI hosts must register a `SparsePayloadSchema` for each type in `PayloadJson.Schema.RequiredExternalSchemaTypes`; use `SparsePayloadSchemaNames.GetName` for the component name referenced by the generated bundle.

Payload-schema metadata is opt-in through the consuming generator's
`PayloadSchemaMetadataExtensionName` dialect setting. When configured, the
shared emitter adds versioned metadata to payload and member schemas, including
member JSON names and shapes, scalar wire/comparison hints, redaction, nested
model links, and collection key/order semantics. The shared emitter uses only
the configured extension name and dialect; consumers own the metadata
vocabulary and can leave the setting unset to preserve existing OpenAPI output.
Downstream code generators can use this metadata to build product-specific
clients without inferring C# names or payload behavior from schema component
names.


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
