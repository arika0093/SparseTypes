using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace SparseTypes.OpenApi.AspNetCore.Tests;

public sealed class OpenApiRegistrationTests
{
    [Test]
    public async Task RegisteredPayloadSchemasAppearInTheDocument()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi<PayloadModel.ChangePayload>();
        builder.Services.AddSparseOpenApi<PayloadModel.ChangePayload>();
        await using var app = builder.Build();
        app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();

        var document = await app
            .Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync();

        document.Components!.Schemas!.Keys.ShouldContain(
            "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload"
        );
        document.Components.Schemas.Keys.ShouldContain(
            "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_Change"
        );
    }

    [Test]
    public async Task StartedAppDocumentKeepsSparsePayloadSchemas()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi<PayloadModel.ChangePayload>();
        await using var app = builder.Build();
        app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app
            .Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync();

        document.Paths!.Keys.ShouldContain("/changes");
        document.Components!.Schemas!.Keys.ShouldContain(
            "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload"
        );
        document.Components.Schemas.Keys.ShouldContain(
            "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_Change"
        );

        await app.StopAsync();
    }

    [Test]
    public async Task NestedPayloadSchemasUseCuratedNames()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi<Basket.ChangePayload>();
        builder.Services.AddSparseOpenApi<BasketItem.ChangePayload>();
        await using var app = builder.Build();
        app.MapPost("/baskets", (Basket.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app
            .Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync();

        document.Paths!.Keys.ShouldContain("/baskets");
        var names = document.Components!.Schemas!.Keys;
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_Basket_ChangePayload");
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_Basket_Change");
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_BasketItem_ChangePayload");
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_BasketItem_Change");
        names.ShouldNotContain(name => name.Contains("Generated_Change"));

        await app.StopAsync();
    }
}
