using System;
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
        var configureOpenApi = !services.Any(static descriptor =>
            descriptor.ServiceType == typeof(SparsePayloadSchemaSet)
        );
        services.AddSingleton<SparsePayloadSchemaSet>(
            new SparsePayloadSchemaSet(new TPayload().GetSparsePayloadSchemas())
        );
        if (configureOpenApi)
        {
            services.Configure<SwaggerGenOptions>(options =>
            {
                options.DocumentFilter<SparsePayloadDocumentFilter>();
                options.CustomSchemaIds(SparsePayloadSchemaNames.GetName);
            });
        }
        return services;
    }
}

internal sealed class SparsePayloadSchemaSet(
    System.Collections.Generic.IReadOnlyList<SparseTypes.SparsePayloadSchema> schemas
)
{
    public System.Collections.Generic.IReadOnlyList<SparseTypes.SparsePayloadSchema> Schemas { get; } =
        schemas;
}
