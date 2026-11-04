using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace SparseTypes.OpenApi.AspNetCore.Tests;

public sealed class OpenApiRegistrationValidationTests
{
    [Test]
    public async Task ConflictingDefinitionsAcrossRegistrationsAreRejectedForTheRequestedDialect()
    {
        var provider31 = BuildProvider(OpenApiSpecVersion.OpenApi3_1, Bundle("{\"type\":\"string\"}"), Bundle("{\"type\":\"integer\"}"));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => provider31.GetOpenApiDocumentAsync());
        exception.Message.ShouldContain("Shared");

        var document30 = await BuildProvider(OpenApiSpecVersion.OpenApi3_0, Bundle("{\"type\":\"string\"}"), Bundle("{\"type\":\"integer\"}"))
            .GetOpenApiDocumentAsync();
        document30.Components!.Schemas!.Keys.ShouldContain("Shared");
    }

    [Test]
    public async Task ConflictingExternalDefinitionsAreRejected()
    {
        var services = WebApplication.CreateBuilder().Services;
        services.AddOpenApi(options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1);
        var root = Bundle("{\"type\":\"string\"}");
        services.AddSparseOpenApi(root, [new SparsePayloadSchema(typeof(DateOnly), "Ext", "{\"type\":\"string\"}")]);
        services.AddSparseOpenApi(root, [new SparsePayloadSchema(typeof(DateOnly), "Ext", "{\"type\":\"integer\"}")]);
        var provider = BuildProvider(services);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => provider.GetOpenApiDocumentAsync());
        exception.Message.ShouldContain("Ext");
    }

    [Test]
    public async Task IdenticalRepeatedRegistrationsAreAccepted()
    {
        var document = await BuildProvider(OpenApiSpecVersion.OpenApi3_1, Bundle("{\"type\":\"string\"}"), Bundle("{\"type\":\"string\"}"))
            .GetOpenApiDocumentAsync();

        document.Components!.Schemas!.Keys.ShouldContain("Shared");
    }

    [Test]
    public void FailedRegistrationLeavesServicesAndRegistryUntouched()
    {
        var services = new ServiceCollection();
        services.AddOpenApi();
        services.AddSparseOpenApi(Bundle("{\"type\":\"string\"}"));
        var before = services.Count;
        var colliding = new SparsePayloadSchemaBundle(
            typeof(int),
            [Schema(typeof(int), "Fresh"), Schema(typeof(long), "Shared")],
            [Schema(typeof(int), "Fresh"), Schema(typeof(long), "Shared")],
            []
        );

        Should.Throw<InvalidOperationException>(() => services.AddSparseOpenApi(colliding));

        services.Count.ShouldBe(before);
        Should.NotThrow(() => services.AddSparseOpenApi(new SparsePayloadSchemaBundle(typeof(short), [Schema(typeof(short), "Fresh")], [Schema(typeof(short), "Fresh")], [])));
    }

    private static IOpenApiDocumentProvider BuildProvider(OpenApiSpecVersion version, params SparsePayloadSchemaBundle[] bundles)
    {
        var services = WebApplication.CreateBuilder().Services;
        services.AddOpenApi(options => options.OpenApiVersion = version);
        foreach (var bundle in bundles)
        {
            services.AddSparseOpenApi(bundle);
        }

        return BuildProvider(services);
    }

    private static IOpenApiDocumentProvider BuildProvider(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

    private static SparsePayloadSchemaBundle Bundle(string schema31) =>
        new(
            typeof(PayloadModel.ChangePayload),
            [new SparsePayloadSchema(typeof(PayloadModel.ChangePayload), "Shared", "{\"type\":\"string\"}")],
            [new SparsePayloadSchema(typeof(PayloadModel.ChangePayload), "Shared", "{\"type\":\"string\"}", schema31)],
            []
        );

    private static SparsePayloadSchema Schema(Type type, string name) => new(type, name, "{\"type\":\"string\"}");
}
