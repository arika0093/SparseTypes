using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using SparseTypes.OpenApi.Internal;

namespace SparseTypes.OpenApi.AspNetCore;

internal sealed class SparsePayloadDocumentTransformer(
    System.Collections.Generic.IEnumerable<SparsePayloadSchemaSet> schemaSets,
    SparseSchemaNameRegistry registry
) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        var version = context.ApplicationServices.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(context.DocumentName).OpenApiVersion;
        if (version is not (OpenApiSpecVersion.OpenApi3_0 or OpenApiSpecVersion.OpenApi3_1))
        {
            throw new NotSupportedException($"SparseTypes schemas do not support OpenAPI version '{version}'.");
        }

        var sets = schemaSets.ToArray();
        var dialect =
            version == OpenApiSpecVersion.OpenApi3_0
                ? SparseTypes.SparsePayloadSchemaDialect.OpenApi30
                : SparseTypes.SparsePayloadSchemaDialect.OpenApi31;
        var schemas = sets.SelectMany(set => set.GetSchemas(dialect)).ToArray();
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
        foreach (var set in sets)
        {
            foreach (var descriptor in set.GetSchemas(version))
            {
                var type = descriptor.Type;
                var jsonSchema = version == OpenApiSpecVersion.OpenApi3_0 ? descriptor.OpenApi30JsonSchema : descriptor.OpenApi31JsonSchema;
                IOpenApiSchema openApiSchema;
                try
                {
                    openApiSchema = SparseOpenApiSchemaParser.Parse(jsonSchema, version, document);
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException($"Could not register generated SparseTypes schema '{descriptor.Name}'.", exception);
                }
                var namesForSchema = new HashSet<string>(StringComparer.Ordinal) { descriptor.Name };
                if (registry.GetEffectiveName(context.DocumentName, type) is { Length: > 0 } effectiveName)
                {
                    namesForSchema.Add(effectiveName);
                }

                foreach (var name in namesForSchema)
                {
                    if (names.TryGetValue(name, out var previousType))
                    {
                        if (previousType != type)
                        {
                            throw new InvalidOperationException($"SparseTypes schema name collision for '{type}'.");
                        }
                    }
                    else
                    {
                        names.Add(name, type);
                    }

                    // Replace any endpoint-generated schema with the canonical
                    // bundle and retain the bundle identity for internal refs.
                    document.Components.Schemas[name] = openApiSchema;
                }
            }
        }
        return Task.CompletedTask;
    }
}

internal sealed class SparsePayloadSchemaSet
{
    public SparsePayloadSchemaSet(SparseTypes.SparsePayloadSchemaBundle bundle, IReadOnlyList<SparseTypes.SparsePayloadSchema> externalSchemas)
    {
        Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        ExternalSchemas = CopySchemas(externalSchemas);
        ValidateExternalSchemas(Bundle, ExternalSchemas);
    }

    public SparseTypes.SparsePayloadSchemaBundle Bundle { get; }

    public IReadOnlyList<SparseTypes.SparsePayloadSchema> ExternalSchemas { get; }

    public IEnumerable<SparseTypes.SparsePayloadSchema> GetSchemas(OpenApiSpecVersion version)
    {
        var dialect =
            version == OpenApiSpecVersion.OpenApi3_0
                ? SparseTypes.SparsePayloadSchemaDialect.OpenApi30
                : SparseTypes.SparsePayloadSchemaDialect.OpenApi31;
        return GetSchemas(dialect);
    }

    public IEnumerable<SparseTypes.SparsePayloadSchema> GetSchemas(SparseTypes.SparsePayloadSchemaDialect dialect)
    {
        var bundleSchemas = Bundle.GetSchemas(dialect);
        return bundleSchemas.Concat(ExternalSchemas);
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
