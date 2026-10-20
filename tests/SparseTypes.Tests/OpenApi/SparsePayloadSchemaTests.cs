using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
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

    [Test]
    public async Task StartedAppDocumentKeepsSparsePayloadSchemas()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi<OpenApiTestModel.ChangePayload>();
        await using var app = builder.Build();
        app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.Ok(payload));
        await app.StartAsync();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Paths!.Keys.ShouldContain("/changes");
        document.Components!.Schemas!.Keys.ShouldContain(
            "SparseTypes_Tests_OpenApi_OpenApiTestModel_ChangePayload"
        );
        document.Components.Schemas.Keys.ShouldContain(
            "SparseTypes_Tests_OpenApi_OpenApiTestModel_Change"
        );

        await app.StopAsync();
    }

    [Test]
    public void StateAndKindUseWireStringEnums()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi<OpenApiShipment.ChangePayload>();
        builder.Services.AddSparseOpenApi<OpenApiBasket.ChangePayload>();
        builder.Services.AddSparseOpenApi<OpenApiBasketItem.ChangePayload>();
        using var app = builder.Build();
        app.MapPost("/shipments", (OpenApiShipment.ChangePayload payload) => Results.Ok(payload));

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var state = ResolveComponent(
            document,
            SparseTypes.SparsePayloadSchemaNames.GetName(typeof(SparseTypes.ChangePayloadState))
        );
        state.Type.ShouldBe(JsonSchemaType.String);
        string.Join(",", state.Enum!.Select(static node => node.GetValue<string>()))
            .ShouldBe("missing,null,value,redacted");

        var kind = ResolveComponent(
            document,
            SparseTypes.SparsePayloadSchemaNames.GetName(typeof(SparseTypes.ChangePayloadItemKind))
        );
        kind.Type.ShouldBe(JsonSchemaType.String);
        string.Join(",", kind.Enum!.Select(static node => node.GetValue<string>()))
            .ShouldBe("add,remove,edit,reorder");

        // User domain enums stay numeric.
        var userEnum = ResolveComponent(
            document,
            SparseTypes.SparsePayloadSchemaNames.GetName(typeof(OpenApiShipmentState))
        );
        userEnum.Type.ShouldBe(JsonSchemaType.Integer);
    }

    private static OpenApiSchema ResolveComponent(OpenApiDocument document, string name)
    {
        IOpenApiSchema schema = document.Components!.Schemas![name];
        var guard = 0;
        while (schema is OpenApiSchemaReference reference && guard++ < 8)
        {
            if (reference.Target is not IOpenApiSchema target)
                break;
            schema = target;
        }
        return (OpenApiSchema)schema;
    }
}
