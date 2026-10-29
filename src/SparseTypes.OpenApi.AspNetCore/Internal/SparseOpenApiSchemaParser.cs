using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using SparseTypes;

namespace SparseTypes.OpenApi.Internal;

internal static class SparseOpenApiSchemaParser
{
    internal static void ValidateReferences(IEnumerable<SparsePayloadSchema> schemas, OpenApiSpecVersion version)
    {
        var descriptors = schemas.ToArray();
        var names = new HashSet<string>(descriptors.Select(static schema => schema.Name), StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            var jsonSchema = version == OpenApiSpecVersion.OpenApi3_0 ? descriptor.OpenApi30JsonSchema : descriptor.OpenApi31JsonSchema;
            using var json = JsonDocument.Parse(jsonSchema);
            ValidateReferences(json.RootElement, descriptor.Name, names);
        }
    }

    internal static IOpenApiSchema Parse(string jsonSchema, OpenApiSpecVersion version, OpenApiDocument document)
    {
        using var json = JsonDocument.Parse(jsonSchema);
        if (
            json.RootElement.ValueKind == JsonValueKind.Object
            && json.RootElement.EnumerateObject().Count() == 1
            && json.RootElement.TryGetProperty("$ref", out var referenceElement)
            && referenceElement.ValueKind == JsonValueKind.String
        )
        {
            const string componentPrefix = "#/components/schemas/";
            var reference = referenceElement.GetString();
            if (reference is not null && reference.StartsWith(componentPrefix, StringComparison.Ordinal))
            {
                var name = Uri.UnescapeDataString(reference[componentPrefix.Length..])
                    .Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                return new OpenApiSchemaReference(name, document);
            }
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonSchema), writable: false);
        var schema = new OpenApiJsonReader().ReadFragment<OpenApiSchema>(stream, version, document, out var diagnostic);
        if (diagnostic.Errors.Count > 0)
        {
            throw new InvalidOperationException("The generated payload schema is invalid: " + string.Join("; ", diagnostic.Errors));
        }

        return schema
            ?? throw new InvalidOperationException(
                "The generated payload schema did not contain a schema object."
                    + (diagnostic.Warnings.Count > 0 ? " " + string.Join("; ", diagnostic.Warnings) : string.Empty)
            );
    }

    private static void ValidateReferences(JsonElement schema, string sourceName, HashSet<string> names)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in schema.EnumerateObject())
            {
                if (property.NameEquals("$ref"))
                {
                    var reference = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    const string componentPrefix = "#/components/schemas/";
                    if (reference is null || !reference.StartsWith(componentPrefix, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Schema '{sourceName}' contains an unsupported reference '{reference}'.");
                    }

                    var componentName = Uri.UnescapeDataString(reference[componentPrefix.Length..])
                        .Replace("~1", "/", StringComparison.Ordinal)
                        .Replace("~0", "~", StringComparison.Ordinal);
                    if (componentName.Length == 0 || componentName.Contains('/') || !names.Contains(componentName))
                    {
                        throw new InvalidOperationException($"Schema '{sourceName}' references unregistered component '{componentName}'.");
                    }
                }
                else if (property.Name is "properties" or "patternProperties" or "$defs" or "definitions" or "dependentSchemas")
                {
                    foreach (var nestedSchema in property.Value.EnumerateObject())
                    {
                        ValidateReferences(nestedSchema.Value, sourceName, names);
                    }
                }
                else if (property.Name is "allOf" or "anyOf" or "oneOf" or "prefixItems")
                {
                    foreach (var nestedSchema in property.Value.EnumerateArray())
                    {
                        ValidateReferences(nestedSchema, sourceName, names);
                    }
                }
                else if (
                    property.Name
                    is "items"
                        or "additionalItems"
                        or "additionalProperties"
                        or "unevaluatedItems"
                        or "unevaluatedProperties"
                        or "contains"
                        or "propertyNames"
                        or "not"
                        or "if"
                        or "then"
                        or "else"
                        or "contentSchema"
                )
                {
                    ValidateReferences(property.Value, sourceName, names);
                }
            }
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in schema.EnumerateArray())
            {
                ValidateReferences(element, sourceName, names);
            }
        }
    }
}
