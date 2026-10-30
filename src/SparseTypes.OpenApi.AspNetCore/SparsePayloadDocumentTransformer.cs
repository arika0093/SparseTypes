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
        var installed = new HashSet<string>(StringComparer.Ordinal);
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

                    if (
                        installed.Add(name)
                        && document.Components.Schemas.TryGetValue(name, out _)
                        && !registry.IsOwnedBy(context.DocumentName, name, type)
                    )
                    {
                        throw new InvalidOperationException(
                            $"SparseTypes cannot register component '{name}' in OpenAPI document '{context.DocumentName}' "
                                + $"for type '{type}' because a non-SparseTypes schema already uses that component name. "
                                + "Rename the conflicting schema or customize the schema ID policy so the names no longer collide."
                        );
                    }

                    // Replace any endpoint-generated schema with the canonical
                    // bundle and retain the bundle identity for internal refs.
                    document.Components.Schemas[name] = openApiSchema;
                }
            }
        }
        PublishDtoAssociations(document, context.DocumentName, sets);
        return Task.CompletedTask;
    }

    private void PublishDtoAssociations(OpenApiDocument document, string documentName, SparsePayloadSchemaSet[] sets)
    {
        var components = document.Components?.Schemas;
        if (components is null)
        {
            return;
        }

        var externals = sets.SelectMany(static set => set.ExternalSchemas).ToArray();
        foreach (var set in sets)
        {
            foreach (var descriptor in set.GetSchemas(SparseTypes.SparsePayloadSchemaDialect.OpenApi31))
            {
                var modelName = ReadPayloadModel(descriptor);
                if (modelName is null)
                {
                    continue;
                }

                var modelType = ResolveModelType(modelName);
                if (modelType is null)
                {
                    continue;
                }

                var dto = externals.FirstOrDefault(schema => schema.Type == modelType)?.Name;
                if (dto is null && registry.TryGetObservedName(documentName, modelType, out var observed))
                {
                    dto = observed;
                }

                if (dto is null || !components.ContainsKey(dto))
                {
                    continue;
                }

                if (components.TryGetValue(descriptor.Name, out var installed))
                {
                    AddDtoAssociation(installed, dto);
                }
            }
        }
    }

    private static string? ReadPayloadModel(SparseTypes.SparsePayloadSchema descriptor)
    {
        using var json = System.Text.Json.JsonDocument.Parse(descriptor.OpenApi31JsonSchema);
        if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        if (!json.RootElement.TryGetProperty("x-sparsetypes", out var metadata) || metadata.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        if (!metadata.TryGetProperty("role", out var role) || role.GetString() != "payload")
        {
            return null;
        }

        return metadata.TryGetProperty("model", out var model) && model.ValueKind == System.Text.Json.JsonValueKind.String ? model.GetString() : null;
    }

    private static Type? ResolveModelType(string modelName)
    {
        const string globalPrefix = "global::";
        if (modelName.StartsWith(globalPrefix, StringComparison.Ordinal))
        {
            modelName = modelName.Substring(globalPrefix.Length);
        }

        var direct = Type.GetType(modelName, throwOnError: false);
        if (direct is not null)
        {
            return direct;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? resolved;
            try
            {
                resolved = assembly.GetType(modelName, throwOnError: false, ignoreCase: false);
            }
            catch (Exception)
            {
                // Best-effort model lookup: an unloadable assembly simply has no match.
                continue;
            }

            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static void AddDtoAssociation(IOpenApiSchema installed, string dtoComponent)
    {
        if (installed is not OpenApiSchema concrete || concrete.Extensions is null)
        {
            return;
        }

        if (!concrete.Extensions.TryGetValue("x-sparsetypes", out var metadata) || metadata is not JsonNodeExtension nodeExtension)
        {
            return;
        }

        if (nodeExtension.Node is System.Text.Json.Nodes.JsonObject metadataObject && !metadataObject.ContainsKey("dto"))
        {
            metadataObject["dto"] = dtoComponent;
        }
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
