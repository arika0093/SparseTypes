using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SparseTypes.OpenApi.Swashbuckle;

/// <summary>Registers SparseTypes schemas with Swashbuckle.</summary>
public static class SparseOpenApiServiceCollectionExtensions
{
    /// <summary>Registers an instance-free generated schema bundle.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="bundle">The generated OpenAPI 3.0 and 3.1 schemas.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSparseOpenApi(this IServiceCollection services, SparsePayloadSchemaBundle bundle) =>
        AddSparseOpenApi(services, bundle, OpenApiSpecVersion.OpenApi3_0, Array.Empty<SparsePayloadSchema>());

    /// <summary>Registers an instance-free generated schema bundle for one Swashbuckle output version.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="bundle">The generated OpenAPI 3.0 and 3.1 schemas.</param>
    /// <param name="version">The OpenAPI version configured for Swagger output.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSparseOpenApi(
        this IServiceCollection services,
        SparsePayloadSchemaBundle bundle,
        OpenApiSpecVersion version
    ) => AddSparseOpenApi(services, bundle, version, Array.Empty<SparsePayloadSchema>());

    /// <summary>Registers an instance-free schema bundle and explicit external leaf schemas.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="bundle">The generated OpenAPI 3.0 and 3.1 schemas.</param>
    /// <param name="version">The OpenAPI version configured for Swagger output.</param>
    /// <param name="externalSchemas">Schemas for external leaf types required by the bundle.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSparseOpenApi(
        this IServiceCollection services,
        SparsePayloadSchemaBundle bundle,
        OpenApiSpecVersion version,
        IReadOnlyList<SparsePayloadSchema> externalSchemas
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(externalSchemas);
        if (version is not (OpenApiSpecVersion.OpenApi3_0 or OpenApiSpecVersion.OpenApi3_1))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "SparseTypes schemas support OpenAPI 3.0 and 3.1.");
        }

        Register(services, new SparsePayloadSchemaSet(bundle, version, externalSchemas));
        return services;
    }

    private static void Register(IServiceCollection services, SparsePayloadSchemaSet schemaSet)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(schemaSet);
        var registry = services.Select(static descriptor => descriptor.ImplementationInstance).OfType<SparseSchemaNameRegistry>().LastOrDefault();
        if (registry is null)
        {
            registry = new SparseSchemaNameRegistry();
            services.AddSingleton(registry);
            services.Configure<SwaggerGenOptions>(options => options.DocumentFilter<SparsePayloadDocumentFilter>());
            services.PostConfigure<SwaggerGenOptions>(options =>
            {
                var existingSchemaId = options.SchemaGeneratorOptions.SchemaIdSelector;
                options.SchemaGeneratorOptions.SchemaIdSelector = type =>
                {
                    var name = existingSchemaId(type);
                    registry.RecordEffectiveName(type, name);
                    return name;
                };
            });
        }

        registry.Add(schemaSet.GetAllIdentities());
        registry.AddVersion(schemaSet.OpenApiVersion);
    }
}

/// <summary>
/// Maps generated payload types to their curated component names so endpoint
/// schemas use the same stable names as the registered metadata, including
/// inside generic arguments. Shared across every AddSparseOpenApi call.
/// </summary>
internal sealed class SparseSchemaNameRegistry
{
    private readonly ConcurrentDictionary<Type, string> _names = new();
    private readonly ConcurrentDictionary<Type, string> _effectiveNames = new();
    private OpenApiSpecVersion? _version;

    /// <summary>Adds curated names and rejects inconsistent type or name collisions.</summary>
    /// <param name="schemas">The generated payload schemas.</param>
    public void Add(IReadOnlyList<SparseTypes.SparsePayloadSchemaIdentity> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        foreach (var schema in schemas)
        {
            if (_names.TryGetValue(schema.Type, out var existingName) && !string.Equals(existingName, schema.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"SparseTypes schema type '{schema.Type}' has conflicting component names.");
            }

            if (_names.Any(entry => entry.Key != schema.Type && string.Equals(entry.Value, schema.Name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"SparseTypes schema name '{schema.Name}' is registered for multiple types.");
            }

            _names.TryAdd(schema.Type, schema.Name);
        }
    }

    public void AddVersion(OpenApiSpecVersion version)
    {
        if (_version is { } existingVersion && existingVersion != version)
        {
            throw new InvalidOperationException("A Swashbuckle registration set cannot mix OpenAPI 3.0 and 3.1 schema bundles.");
        }

        _version = version;
    }

    public void RecordEffectiveName(Type type, string name)
    {
        if (!_names.ContainsKey(type))
        {
            return;
        }

        if (_effectiveNames.TryGetValue(type, out var existingName) && !string.Equals(existingName, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The Swashbuckle schema ID policy returned inconsistent names for type '{type}'.");
        }

        if (_effectiveNames.Any(entry => entry.Key != type && string.Equals(entry.Value, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Swashbuckle component name '{name}' is registered for multiple SparseTypes payload types.");
        }

        _effectiveNames.TryAdd(type, name);
    }

    public string? GetEffectiveName(Type type) => _effectiveNames.TryGetValue(type, out var name) ? name : null;
}
