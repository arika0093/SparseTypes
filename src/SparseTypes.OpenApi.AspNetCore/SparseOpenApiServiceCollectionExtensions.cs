using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace SparseTypes.OpenApi.AspNetCore;

/// <summary>Registers SparseTypes schemas with ASP.NET Core OpenAPI.</summary>
public static class SparseOpenApiServiceCollectionExtensions
{
    /// <summary>Registers an instance-free generated schema bundle.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="bundle">The generated OpenAPI 3.0 and 3.1 schemas.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSparseOpenApi(this IServiceCollection services, SparsePayloadSchemaBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return AddSparseOpenApi(services, bundle, Array.Empty<SparsePayloadSchema>());
    }

    /// <summary>Registers an instance-free generated schema bundle and its external leaf schemas.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="bundle">The generated OpenAPI 3.0 and 3.1 schemas.</param>
    /// <param name="externalSchemas">Explicit schemas for leaf types required by the bundle.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSparseOpenApi(
        this IServiceCollection services,
        SparsePayloadSchemaBundle bundle,
        IReadOnlyList<SparsePayloadSchema> externalSchemas
    )
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(externalSchemas);
        Register(services, new SparsePayloadSchemaSet(bundle, externalSchemas));
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
            services.PostConfigureAll<OpenApiOptions>(options =>
            {
                var existingReferenceId = options.CreateSchemaReferenceId;
                options.CreateSchemaReferenceId = type =>
                {
                    var name = existingReferenceId(type);
                    registry.RecordObservedName(options.DocumentName, type.Type, name);
                    if (registry.ContainsType(type.Type))
                    {
                        registry.RecordEffectiveName(options.DocumentName, type.Type, name);
                    }

                    return name;
                };
                options.AddDocumentTransformer<SparsePayloadDocumentTransformer>();
            });
        }

        registry.Add(schemaSet.GetAllIdentities());
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
    private readonly ConcurrentDictionary<string, Type> _typesByName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string DocumentName, Type Type), string> _effectiveNames = new();
    private readonly ConcurrentDictionary<(string DocumentName, string Name), Type> _typesByEffectiveName = new();
    private readonly ConcurrentDictionary<(string DocumentName, string Name), Type> _observedOwners = new();
    private readonly ConcurrentDictionary<(string DocumentName, string Name), byte> _conflictedNames = new();

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

            if (_typesByName.TryGetValue(schema.Name, out var existingType) && existingType != schema.Type)
            {
                throw new InvalidOperationException($"SparseTypes schema name '{schema.Name}' is registered for multiple types.");
            }

            _names.TryAdd(schema.Type, schema.Name);
            _typesByName.TryAdd(schema.Name, schema.Type);
        }
    }

    /// <summary>Records the component name selected by the application's schema ID policy.</summary>
    public void RecordEffectiveName(string documentName, Type type, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var key = (documentName, name);
        if (_typesByEffectiveName.TryGetValue(key, out var existingType) && existingType != type)
        {
            throw new InvalidOperationException($"OpenAPI component name '{name}' maps to multiple CLR types in document '{documentName}'.");
        }

        var typeKey = (documentName, type);
        if (_effectiveNames.TryGetValue(typeKey, out var existingName) && !string.Equals(existingName, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The schema ID policy returned inconsistent names for type '{type}' in document '{documentName}'.");
        }

        _typesByEffectiveName.TryAdd(key, type);
        _effectiveNames.TryAdd(typeKey, name);
    }

    /// <summary>Records the component name the application schema pipeline assigned to any CLR type.</summary>
    /// <remarks>Best-effort ownership map used to tell endpoint-generated schemas apart from unrelated collisions. Never throws.</remarks>
    public void RecordObservedName(string documentName, Type type, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var key = (documentName, name);
        var owner = _observedOwners.GetOrAdd(key, type);
        if (owner != type)
        {
            _conflictedNames.TryAdd(key, 0);
        }
    }

    /// <summary>Gets the component name selected by the application for one document.</summary>
    public string? GetEffectiveName(string documentName, Type type) => _effectiveNames.TryGetValue((documentName, type), out var name) ? name : null;

    /// <summary>Reports whether the application schema pipeline demonstrably assigned a component to one CLR type.</summary>
    public bool IsOwnedBy(string documentName, string name, Type type)
    {
        var key = (documentName, name);
        return !_conflictedNames.ContainsKey(key) && _observedOwners.TryGetValue(key, out var owner) && owner == type;
    }
    /// <summary>Gets the component name the application schema pipeline assigned to a CLR type in one document.</summary>
    public bool TryGetObservedName(string documentName, Type type, out string? name)
    {
        foreach (var entry in _observedOwners)
        {
            if (
                string.Equals(entry.Key.DocumentName, documentName, StringComparison.Ordinal)
                && entry.Value == type
                && !_conflictedNames.ContainsKey(entry.Key)
            )
            {
                name = entry.Key.Name;
                return true;
            }
        }

        name = null;
        return false;
    }

    /// <summary>Reports whether a type belongs to a registered payload schema set.</summary>
    public bool ContainsType(Type type) => _names.ContainsKey(type);
}
