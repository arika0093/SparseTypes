using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SparseTypes.OpenApi.Swashbuckle;

internal sealed class SparsePayloadDocumentFilter(
    System.Collections.Generic.IEnumerable<SparsePayloadSchemaSet> schemaSets
) : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var names = new Dictionary<string, Type>(StringComparer.Ordinal);
        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        foreach (var descriptor in schemaSets.SelectMany(static set => set.Schemas))
        {
            var type = descriptor.Type;
            var generated =
                descriptor.JsonSchema == "{}"
                    ? context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository)
                    : ParseSchema(descriptor.JsonSchema, swaggerDoc);
            var name = descriptor.Name;
            if (names.TryGetValue(name, out var previousType))
            {
                if (previousType == type)
                    continue;
                throw new InvalidOperationException(
                    $"SparseTypes schema name collision for '{type}'."
                );
            }
            names.Add(name, type);
            // Reserve the curated name so later generator passes resolve the
            // same component instead of registering a duplicate entry.
            if (!context.SchemaRepository.TryLookupByType(type, out _))
            {
                context.SchemaRepository.RegisterType(type, name);
            }

            // Endpoints that take a ChangePayload register the same stable
            // component during document generation. The curated schema wins
            // so the document stays consistent with the registered metadata.
            swaggerDoc.Components.Schemas[name] = generated;
        }
        // Reflection reports the wire string enums (state/kind) as integers.
        // Rewrite them to string enums so the document matches the JSON bytes.
        SparsePayloadEnumSchemas.FixDocument(swaggerDoc);
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
        if (source.TryGetProperty("properties", out var properties))
            foreach (var property in properties.EnumerateObject())
                (schema.Properties ??= new Dictionary<string, IOpenApiSchema>())[property.Name] =
                    ConvertSchema(property.Value, document);
        if (source.TryGetProperty("items", out var items))
            schema.Items = ConvertSchema(items, document);
        if (source.TryGetProperty("oneOf", out var oneOf))
            foreach (var variant in oneOf.EnumerateArray())
                (schema.OneOf ??= new List<IOpenApiSchema>()).Add(ConvertSchema(variant, document));
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
                            return new OpenApiSchemaReference(
                                path[(path.LastIndexOf('/') + 1)..],
                                document
                            );
                        },
                        StringComparer.Ordinal
                    ),
            };
        }
        return schema;
    }
}
