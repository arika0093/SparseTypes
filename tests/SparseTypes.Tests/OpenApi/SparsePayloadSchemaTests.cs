using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SparseTypes.OpenApi.Swashbuckle;
using Swashbuckle.AspNetCore.Swagger;

namespace SparseTypes.Tests.OpenApi;

public sealed class SparsePayloadSchemaTests
{
    [Test]
    public void GeneratedPayloadProvidesStableSchemasAndDiscriminator()
    {
        var provider = (ISparsePayloadSchemaProvider)new OpenApiTestModel.ChangePayload();
        var schemas = provider.GetSparsePayloadSchemas();

        schemas
            .Select(static schema => schema.Name)
            .ShouldContain("SparseTypes_Tests_OpenApi_OpenApiTestModel_ChangePayload");
        schemas
            .Select(static schema => schema.Name)
            .ShouldContain("SparseTypes_Tests_OpenApi_OpenApiTestModel_Change");
        schemas
            .Single(static schema => schema.Name.EndsWith("_Change", StringComparison.Ordinal))
            .JsonSchema.ShouldContain("\"oneOf\"");
        schemas.Select(static schema => schema.Name).Distinct().Count().ShouldBe(schemas.Count);
        schemas
            .Select(static schema => schema.Name)
            .Any(static name =>
                System.Text.RegularExpressions.Regex.IsMatch(name, "_[0-9A-F]{8}(?:_|$)")
            )
            .ShouldBeFalse();
    }

    [Test]
    public void SwashbuckleAddsRegisteredSchemasToComponents()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi<OpenApiTestModel.ChangePayload>();
        builder.Services.AddSparseOpenApi<OpenApiTestModel.ChangePayload>();
        using var app = builder.Build();
        app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.Ok(payload));

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Components!.Schemas!.Keys.ShouldContain(
            "SparseTypes_Tests_OpenApi_OpenApiTestModel_ChangePayload"
        );
        document.Components!.Schemas!.Keys.ShouldContain(
            "SparseTypes_Tests_OpenApi_OpenApiTestModel_Change"
        );
    }
}
