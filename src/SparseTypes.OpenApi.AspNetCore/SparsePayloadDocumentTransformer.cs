using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SparseTypes.OpenApi.AspNetCore;

internal sealed class SparsePayloadDocumentTransformer(System.Collections.Generic.IEnumerable<SparsePayloadSchemaSet> schemaSets)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        var names = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var descriptor in schemaSets.SelectMany(static set => set.Schemas))
        {
            var type = descriptor.Type;
            var openApiSchema =
                descriptor.JsonSchema == "{}"
                    ? await context.GetOrCreateSchemaAsync(type, null, cancellationToken)
                    : ParseSchema(descriptor.JsonSchema, document);
            var name = descriptor.Name;
            if (names.TryGetValue(name, out var previousType))
            {
                if (previousType == type)
                {
                    continue;
                }

                throw new InvalidOperationException($"SparseTypes schema name collision for '{type}'.");
            }
            names.Add(name, type);
            // Endpoints that take a ChangePayload register the same stable
            // component during document generation. The curated schema wins
            // so the document stays consistent with the registered metadata.
            document.Components.Schemas[name] = openApiSchema;
        }
        // Reflection reports the wire string enums (state/kind) as integers.
        // Rewrite them to string enums so the document matches the JSON bytes.
        SparsePayloadEnumSchemas.FixDocument(document);
    }

    private static OpenApiSchema ParseSchema(string jsonSchema, OpenApiDocument document)
    {
        using var json = JsonDocument.Parse(jsonSchema);
        return (OpenApiSchema)ConvertSchema(json.RootElement, document);
    }

    private static IOpenApiSchema ConvertSchema(JsonElement source, OpenApiDocument document)
    {
        if (source.TryGetProperty("$ref", out var reference))
        {
            var path = reference.GetString()!;
            return new OpenApiSchemaReference(path[(path.LastIndexOf('/') + 1)..], document);
        }

        var schema = new OpenApiSchema();
        if (source.TryGetProperty("type", out var type))
        {
            schema.Type = type.GetString() switch
            {
                "object" => JsonSchemaType.Object,
                "array" => JsonSchemaType.Array,
                "string" => JsonSchemaType.String,
                "integer" => JsonSchemaType.Integer,
                "number" => JsonSchemaType.Number,
                "boolean" => JsonSchemaType.Boolean,
                _ => null,
            };
        }

        if (source.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                (schema.Properties ??= new Dictionary<string, IOpenApiSchema>())[property.Name] = ConvertSchema(property.Value, document);
            }
        }

        if (source.TryGetProperty("items", out var items))
        {
            schema.Items = ConvertSchema(items, document);
        }

        if (source.TryGetProperty("oneOf", out var oneOf))
        {
            foreach (var variant in oneOf.EnumerateArray())
            {
                (schema.OneOf ??= new List<IOpenApiSchema>()).Add(ConvertSchema(variant, document));
            }
        }

        if (source.TryGetProperty("discriminator", out var discriminator))
        {
            schema.Discriminator = new OpenApiDiscriminator
            {
                PropertyName = discriminator.GetProperty("propertyName").GetString()!,
                Mapping = discriminator
                    .GetProperty("mapping")
                    .EnumerateObject()
                    .ToDictionary(
                        static item => item.Name,
                        item =>
                        {
                            var path = item.Value.GetString()!;
                            return new OpenApiSchemaReference(path[(path.LastIndexOf('/') + 1)..], document);
                        },
                        StringComparer.Ordinal
                    ),
            };
        }
        return schema;
    }
}

internal sealed class SparsePayloadSchemaSet(System.Collections.Generic.IReadOnlyList<SparseTypes.SparsePayloadSchema> schemas)
{
    public System.Collections.Generic.IReadOnlyList<SparseTypes.SparsePayloadSchema> Schemas { get; } = schemas;
}
