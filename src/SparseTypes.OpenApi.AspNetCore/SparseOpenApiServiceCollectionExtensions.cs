using System;
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
        var configureOpenApi = !services.Any(static descriptor =>
            descriptor.ServiceType == typeof(SparsePayloadSchemaSet)
        );
        services.AddSingleton<SparsePayloadSchemaSet>(
            new SparsePayloadSchemaSet(new TPayload().GetSparsePayloadSchemas())
        );
        if (configureOpenApi)
        {
            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.CreateSchemaReferenceId = type =>
                    SparsePayloadSchemaNames.GetName(type.Type);
                options.AddDocumentTransformer<SparsePayloadDocumentTransformer>();
            });
        }
        return services;
    }
}
