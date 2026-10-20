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
            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.CreateSchemaReferenceId = type => registry.GetName(type.Type);
                options.AddDocumentTransformer<SparsePayloadDocumentTransformer>();
                options.AddSchemaTransformer(SparsePayloadEnumSchemas.TransformAsync);
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
