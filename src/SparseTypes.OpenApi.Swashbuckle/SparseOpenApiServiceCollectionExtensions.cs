using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SparseTypes.OpenApi.Swashbuckle;

/// <summary>Registers SparseTypes schemas with Swashbuckle.</summary>
public static class SparseOpenApiServiceCollectionExtensions
{
    /// <summary>Registers the generated schemas for a ChangePayload.</summary>
    /// <typeparam name="TPayload">A generated ChangePayload type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSparseOpenApi<TPayload>(this IServiceCollection services)
        where TPayload : class, ISparsePayloadSchemaProvider, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        var schemas = new TPayload().GetSparsePayloadSchemas();
        services.AddSingleton<SparsePayloadSchemaSet>(new SparsePayloadSchemaSet(schemas));
        var registry = services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<SparseSchemaNameRegistry>()
            .LastOrDefault();
        if (registry is null)
        {
            registry = new SparseSchemaNameRegistry();
            services.AddSingleton(registry);
            services.Configure<SwaggerGenOptions>(options =>
            {
                options.DocumentFilter<SparsePayloadDocumentFilter>();
                options.CustomSchemaIds(registry.GetName);
            });
        }
        registry.Add(schemas);
        return services;
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

    /// <summary>Adds the curated names, keeping the first name per type.</summary>
    /// <param name="schemas">The generated payload schemas.</param>
    public void Add(IReadOnlyList<SparseTypes.SparsePayloadSchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        foreach (var schema in schemas)
        {
            _names.TryAdd(schema.Type, schema.Name);
        }
    }

    /// <summary>Gets the stable component name for a CLR type.</summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The curated name when registered; otherwise the default stable name.</returns>
    public string GetName(Type type) => SparseTypes.SparsePayloadSchemaNames.GetName(type, Resolve);

    private string? Resolve(Type type) => _names.TryGetValue(type, out var name) ? name : null;
}

internal sealed class SparsePayloadSchemaSet(
    System.Collections.Generic.IReadOnlyList<SparseTypes.SparsePayloadSchema> schemas
)
{
    public System.Collections.Generic.IReadOnlyList<SparseTypes.SparsePayloadSchema> Schemas { get; } =
        schemas;
}
