using System;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

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

    [Test]
    public async Task StateAndKindUseWireStringEnums()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi<Shipment.ChangePayload>();
        builder.Services.AddSparseOpenApi<Basket.ChangePayload>();
        builder.Services.AddSparseOpenApi<BasketItem.ChangePayload>();
        await using var app = builder.Build();
        app.MapPost("/shipments", (Shipment.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();

        var document = await app
            .Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync();

        var states = new List<OpenApiSchema>();
        var kinds = new List<OpenApiSchema>();
        var integers = 0;
        var visited = new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance);
        foreach (var schema in document.Components!.Schemas!.Values)
            CollectWireEnums(schema, states, kinds, ref integers, visited);

        (states.Count > 0).ShouldBeTrue();
        (kinds.Count > 0).ShouldBeTrue();
        foreach (var schema in states)
        {
            schema.Type.ShouldBe(JsonSchemaType.String);
            string.Join(",", schema.Enum!.Select(static node => node.GetValue<string>()))
                .ShouldBe("missing,null,value,redacted");
        }
        foreach (var schema in kinds)
        {
            schema.Type.ShouldBe(JsonSchemaType.String);
            string.Join(",", schema.Enum!.Select(static node => node.GetValue<string>()))
                .ShouldBe("add,remove,edit,reorder");
        }
        // User domain values stay numeric: integer schemas must remain.
        (integers > 0).ShouldBeTrue();
    }

    private static void CollectWireEnums(
        IOpenApiSchema? schema,
        List<OpenApiSchema> states,
        List<OpenApiSchema> kinds,
        ref int integers,
        HashSet<IOpenApiSchema> visited
    )
    {
        if (schema is null || !visited.Add(schema))
            return;
        if (schema is OpenApiSchemaReference reference)
        {
            CollectWireEnums(reference.Target, states, kinds, ref integers, visited);
            return;
        }
        if (schema is not OpenApiSchema concrete)
            return;
        if (concrete.Type == JsonSchemaType.Integer)
            integers++;
        if (concrete.Properties is not null)
        {
            foreach (var entry in concrete.Properties)
            {
                var target = entry.Value;
                var guard = 0;
                while (target is OpenApiSchemaReference inner && guard++ < 8)
                    target = inner.Target;
                if (
                    string.Equals(entry.Key, "state", StringComparison.Ordinal)
                    && target is OpenApiSchema stateSchema
                )
                    states.Add(stateSchema);
                else if (
                    string.Equals(entry.Key, "kind", StringComparison.Ordinal)
                    && target is OpenApiSchema kindSchema
                )
                    kinds.Add(kindSchema);
                CollectWireEnums(entry.Value, states, kinds, ref integers, visited);
            }
        }
        if (concrete.Items is not null)
            CollectWireEnums(concrete.Items, states, kinds, ref integers, visited);
        if (concrete.OneOf is not null)
        {
            foreach (var variant in concrete.OneOf)
                CollectWireEnums(variant, states, kinds, ref integers, visited);
        }
        if (concrete.AnyOf is not null)
        {
            foreach (var variant in concrete.AnyOf)
                CollectWireEnums(variant, states, kinds, ref integers, visited);
        }
        if (concrete.AllOf is not null)
        {
            foreach (var variant in concrete.AllOf)
                CollectWireEnums(variant, states, kinds, ref integers, visited);
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<IOpenApiSchema>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(IOpenApiSchema? x, IOpenApiSchema? y) => ReferenceEquals(x, y);

        public int GetHashCode(IOpenApiSchema obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
