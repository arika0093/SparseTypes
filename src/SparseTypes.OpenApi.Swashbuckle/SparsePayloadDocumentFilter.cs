using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi;
using SparseTypes.OpenApi.Internal;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SparseTypes.OpenApi.Swashbuckle;

internal sealed class SparsePayloadDocumentFilter(
    System.Collections.Generic.IEnumerable<SparsePayloadSchemaSet> schemaSets,
    SparseSchemaNameRegistry registry
) : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var sets = schemaSets.ToArray();
        var versions = sets.Select(static set => set.OpenApiVersion).Distinct().ToArray();
        if (versions.Length != 1)
        {
            throw new InvalidOperationException("Swashbuckle payload registrations must use one OpenAPI schema version per document.");
        }

        var version = versions[0];
        var schemas = sets.SelectMany(static set => set.GetSchemas()).ToArray();
        SparseOpenApiSchemaParser.ValidateReferences(schemas, version);
        var availableTypes = schemas.Select(static schema => schema.Type).ToHashSet();
        var missing = sets.SelectMany(static set => set.Bundle.RequiredExternalSchemaTypes)
            .Distinct()
            .FirstOrDefault(missing => !availableTypes.Contains(missing));
        if (missing is not null)
        {
            throw new InvalidOperationException($"SparseTypes payload schemas require an explicit external schema for leaf type '{missing}'.");
        }

        var names = new Dictionary<string, Type>(StringComparer.Ordinal);
        var installed = new HashSet<string>(StringComparer.Ordinal);
        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        foreach (var set in sets)
        {
            foreach (var descriptor in set.GetSchemas())
            {
                var type = descriptor.Type;
                var jsonSchema = version == OpenApiSpecVersion.OpenApi3_0 ? descriptor.OpenApi30JsonSchema : descriptor.OpenApi31JsonSchema;
                var generated = SparseOpenApiSchemaParser.Parse(jsonSchema, version, swaggerDoc);
                var namesForSchema = new HashSet<string>(StringComparer.Ordinal) { descriptor.Name };
                var foundSchemaKey = context.SchemaRepository.TryLookupByType(type, out var schemaKey);
                var repositoryName = foundSchemaKey ? schemaKey?.Id : null;
                var recordedName = registry.GetEffectiveName(type);
                var effectiveName = recordedName ?? repositoryName;

                if (!string.IsNullOrWhiteSpace(effectiveName))
                {
                    namesForSchema.Add(effectiveName);
                }

                foreach (var name in namesForSchema)
                {
                    if (names.TryGetValue(name, out var previousType))
                    {
                        if (previousType != type)
                        {
                            throw new InvalidOperationException($"SparseTypes schema name collision '{name}': '{previousType}' and '{type}'.");
                        }
                    }
                    else
                    {
                        names.Add(name, type);
                    }

                    if (
                        installed.Add(name)
                        && swaggerDoc.Components.Schemas.TryGetValue(name, out _)
                        && !IsOwnedBy(context, recordedName, name, type)
                    )
                    {
                        throw new InvalidOperationException(
                            $"SparseTypes cannot register component '{name}' in OpenAPI document '{context.DocumentName}' "
                                + $"for type '{type}' because a non-SparseTypes schema already uses that component name. "
                                + "Rename the conflicting schema or customize the schema ID policy so the names no longer collide."
                        );
                    }

                    // Keep the generated identity for internal refs while
                    // replacing endpoint-generated IDs only for payload types.
                    swaggerDoc.Components.Schemas[name] = generated;
                }
            }
        }
    }

    private static bool IsOwnedBy(DocumentFilterContext context, string? recordedName, string name, Type type)
    {
        // The schema repository knows every endpoint-generated schema: a
        // lookup hit for this exact type proves same-type ownership.
        if (context.SchemaRepository.TryLookupByType(type, out var schemaKey) && string.Equals(schemaKey?.Id, name, StringComparison.Ordinal))
        {
            return true;
        }

        // Otherwise only the application's schema ID policy can demonstrate
        // ownership: it must have assigned this exact name to this exact type.
        return string.Equals(recordedName, name, StringComparison.Ordinal);
    }
}

internal sealed class SparsePayloadSchemaSet
{
    public SparsePayloadSchemaSet(
        SparseTypes.SparsePayloadSchemaBundle bundle,
        OpenApiSpecVersion openApiVersion,
        IReadOnlyList<SparseTypes.SparsePayloadSchema> externalSchemas
    )
    {
        Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        ExternalSchemas = CopySchemas(externalSchemas);
        ValidateExternalSchemas(Bundle, ExternalSchemas);
        OpenApiVersion = openApiVersion;
    }

    public SparseTypes.SparsePayloadSchemaBundle Bundle { get; }

    public IReadOnlyList<SparseTypes.SparsePayloadSchema> ExternalSchemas { get; }

    public OpenApiSpecVersion OpenApiVersion { get; }

    public IEnumerable<SparseTypes.SparsePayloadSchema> GetSchemas()
    {
        var dialect =
            OpenApiVersion == OpenApiSpecVersion.OpenApi3_0
                ? SparseTypes.SparsePayloadSchemaDialect.OpenApi30
                : SparseTypes.SparsePayloadSchemaDialect.OpenApi31;
        return Bundle.GetSchemas(dialect).Concat(ExternalSchemas);
    }

    public IReadOnlyList<SparseTypes.SparsePayloadSchemaIdentity> GetAllIdentities() =>
        Bundle
            .SchemaIdentities.Concat(ExternalSchemas.Select(static schema => new SparseTypes.SparsePayloadSchemaIdentity(schema.Type, schema.Name)))
            .ToArray();

    private static IReadOnlyList<SparseTypes.SparsePayloadSchema> CopySchemas(IReadOnlyList<SparseTypes.SparsePayloadSchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        var copy = new SparseTypes.SparsePayloadSchema[schemas.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<Type>();
        for (var index = 0; index < schemas.Count; index++)
        {
            var schema = schemas[index] ?? throw new ArgumentException("Schema lists must not contain null entries.", nameof(schemas));
            if (!names.Add(schema.Name) || !types.Add(schema.Type))
            {
                throw new ArgumentException("Schema lists must not contain duplicate names or CLR types.", nameof(schemas));
            }

            copy[index] = schema;
        }

        return Array.AsReadOnly(copy);
    }

    private static void ValidateExternalSchemas(
        SparseTypes.SparsePayloadSchemaBundle bundle,
        IReadOnlyList<SparseTypes.SparsePayloadSchema> externalSchemas
    )
    {
        var generatedNames = new HashSet<string>(bundle.SchemaIdentities.Select(static schema => schema.Name), StringComparer.Ordinal);
        var generatedTypes = new HashSet<Type>(bundle.SchemaIdentities.Select(static schema => schema.Type));
        var conflict = externalSchemas.FirstOrDefault(schema => generatedNames.Contains(schema.Name) || generatedTypes.Contains(schema.Type));
        if (conflict is not null)
        {
            throw new ArgumentException($"External schema '{conflict.Name}' conflicts with a generated payload schema.", nameof(externalSchemas));
        }
    }
}
