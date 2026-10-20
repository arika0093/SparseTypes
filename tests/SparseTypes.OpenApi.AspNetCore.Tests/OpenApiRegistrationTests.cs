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
}
