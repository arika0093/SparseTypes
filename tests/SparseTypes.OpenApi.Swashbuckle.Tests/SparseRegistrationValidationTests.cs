using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace SparseTypes.OpenApi.Swashbuckle.Tests;

public sealed class SparseRegistrationValidationTests
{
    [Test]
    public void ConflictingDefinitionsAcrossRegistrationsAreRejected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(Bundle("{\"type\":\"string\"}"), OpenApiSpecVersion.OpenApi3_1);
        builder.Services.AddSparseOpenApi(Bundle("{\"type\":\"integer\"}"), OpenApiSpecVersion.OpenApi3_1);
        using var app = builder.Build();

        var exception = Should.Throw<InvalidOperationException>(() => app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1"));

        exception.ToString().ShouldContain("Shared");
    }

    [Test]
    public void ConflictingExternalDefinitionsAreRejected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        var root = Bundle("{\"type\":\"string\"}");
        builder.Services.AddSparseOpenApi(root, OpenApiSpecVersion.OpenApi3_1, [new SparsePayloadSchema(typeof(DateOnly), "Ext", "{\"type\":\"string\"}")]);
        builder.Services.AddSparseOpenApi(root, OpenApiSpecVersion.OpenApi3_1, [new SparsePayloadSchema(typeof(DateOnly), "Ext", "{\"type\":\"integer\"}")]);
        using var app = builder.Build();

        var exception = Should.Throw<InvalidOperationException>(() => app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1"));

        exception.ToString().ShouldContain("Ext");
    }

    [Test]
    public void IdenticalRepeatedRegistrationsAreAccepted()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(Bundle("{\"type\":\"string\"}"), OpenApiSpecVersion.OpenApi3_1);
        builder.Services.AddSparseOpenApi(Bundle("{\"type\":\"string\"}"), OpenApiSpecVersion.OpenApi3_1);
        using var app = builder.Build();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Components!.Schemas!.Keys.ShouldContain("Shared");
    }

    [Test]
    public void FailedVersionMixLeavesServicesAndRegistryUntouched()
    {
        var services = new ServiceCollection();
        services.AddSwaggerGen();
        services.AddSparseOpenApi(Bundle("{\"type\":\"string\"}"), OpenApiSpecVersion.OpenApi3_0);
        var before = services.Count;
        var mixed = new SparsePayloadSchemaBundle(typeof(int), [Schema(typeof(int), "Fresh")], [Schema(typeof(int), "Fresh")], []);

        Should.Throw<InvalidOperationException>(() => services.AddSparseOpenApi(mixed, OpenApiSpecVersion.OpenApi3_1));

        services.Count.ShouldBe(before);
        var recovered = new SparsePayloadSchemaBundle(typeof(short), [Schema(typeof(short), "Fresh")], [Schema(typeof(short), "Fresh")], []);
        Should.NotThrow(() => services.AddSparseOpenApi(recovered, OpenApiSpecVersion.OpenApi3_0));
    }

    [Test]
    public void FailedIdentityCollisionLeavesServicesAndRegistryUntouched()
    {
        var services = new ServiceCollection();
        services.AddSwaggerGen();
        services.AddSparseOpenApi(Bundle("{\"type\":\"string\"}"), OpenApiSpecVersion.OpenApi3_0);
        var before = services.Count;
        var colliding = new SparsePayloadSchemaBundle(
            typeof(int),
            [Schema(typeof(int), "Fresh"), Schema(typeof(long), "Shared")],
            [Schema(typeof(int), "Fresh"), Schema(typeof(long), "Shared")],
            []
        );

        Should.Throw<InvalidOperationException>(() => services.AddSparseOpenApi(colliding, OpenApiSpecVersion.OpenApi3_0));

        services.Count.ShouldBe(before);
        var recovered = new SparsePayloadSchemaBundle(typeof(short), [Schema(typeof(short), "Fresh")], [Schema(typeof(short), "Fresh")], []);
        Should.NotThrow(() => services.AddSparseOpenApi(recovered, OpenApiSpecVersion.OpenApi3_0));
    }

    private static SparsePayloadSchemaBundle Bundle(string schema) =>
        new(
            typeof(OpenApiTestModel.ChangePayload),
            [new SparsePayloadSchema(typeof(OpenApiTestModel.ChangePayload), "Shared", schema)],
            [new SparsePayloadSchema(typeof(OpenApiTestModel.ChangePayload), "Shared", schema)],
            []
        );

    private static SparsePayloadSchema Schema(Type type, string name) => new(type, name, "{\"type\":\"string\"}");
}
