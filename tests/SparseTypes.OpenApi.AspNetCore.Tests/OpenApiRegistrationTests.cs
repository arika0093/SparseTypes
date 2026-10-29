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
    public async Task BundleSelectsTheSchemaMatchingEachDocumentVersionWithoutReplacingAppIds()
    {
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1 })
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddOpenApi(options =>
            {
                options.OpenApiVersion = version;
                options.CreateSchemaReferenceId = type => "App_" + type.Type.Name;
            });
            builder.Services.AddSparseOpenApi(CreateBundle());
            await using var app = builder.Build();
            app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.Ok(payload));
            app.MapOpenApi();
            await app.StartAsync();
            var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();
            var root = (OpenApiSchema)document.Components!.Schemas!["BundlePayload"];

            root.Enum!.Select(static value => value!.GetValue<string>())
                .ShouldBe(version == OpenApiSpecVersion.OpenApi3_0 ? ["openapi-30"] : ["openapi-31"]);
            document.Components.Schemas.Keys.ShouldContain("App_ChangePayload");
            await app.StopAsync();
        }
    }

    [Test]
    public async Task BundleRejectsAnUnregisteredExternalLeafSchema()
    {
        var bundle = new SparsePayloadSchemaBundle(
            typeof(PayloadModel.ChangePayload),
            [new SparsePayloadSchema(typeof(PayloadModel.ChangePayload), "BundlePayload", "{\"type\":\"object\"}")],
            [new SparsePayloadSchema(typeof(PayloadModel.ChangePayload), "BundlePayload", "{\"type\":\"object\"}")],
            [typeof(DateOnly)]
        );
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(bundle);
        await using var app = builder.Build();
        app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.Ok(payload));

        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => provider.GetOpenApiDocumentAsync());

        exception.Message.ShouldContain(typeof(DateOnly).ToString());
    }

    [Test]
    public async Task BundleRejectsAnUnregisteredComponentReference()
    {
        var rootType = typeof(PayloadModel.ChangePayload);
        var bundle = new SparsePayloadSchemaBundle(
            rootType,
            [new SparsePayloadSchema(rootType, "BundlePayload", "{\"$ref\":\"#/components/schemas/Missing\"}")],
            [new SparsePayloadSchema(rootType, "BundlePayload", "{\"$ref\":\"#/components/schemas/Missing\"}")],
            []
        );
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(bundle);
        await using var app = builder.Build();

        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => provider.GetOpenApiDocumentAsync());

        exception.Message.ShouldContain("unregistered component 'Missing'");
    }

    [Test]
    public async Task BundleAcceptsAnExplicitExternalSchemaForAReferencedLeaf()
    {
        var rootType = typeof(PayloadModel.ChangePayload);
        var schema = "{\"type\":\"object\",\"properties\":{\"date\":{\"$ref\":\"#/components/schemas/ExternalDate\"}}}";
        var bundle = new SparsePayloadSchemaBundle(
            rootType,
            [new SparsePayloadSchema(rootType, "BundlePayload", schema)],
            [new SparsePayloadSchema(rootType, "BundlePayload", schema)],
            [typeof(DateOnly)]
        );
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(
            bundle,
            [new SparsePayloadSchema(typeof(DateOnly), "ExternalDate", "{\"type\":\"string\",\"format\":\"date\"}")]
        );
        await using var app = builder.Build();

        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = await provider.GetOpenApiDocumentAsync();

        document.Components!.Schemas!.Keys.ShouldContain("ExternalDate");
    }

    [Test]
    public async Task BundleAcceptsAComponentReferenceAsTheRootSchema()
    {
        var rootType = typeof(PayloadModel.ChangePayload);
        var schema = new SparsePayloadSchemaBundle(
            rootType,
            [
                new SparsePayloadSchema(rootType, "BundlePayload", "{\"$ref\":\"#/components/schemas/BundleValue\"}"),
                new SparsePayloadSchema(typeof(string), "BundleValue", "{\"type\":\"string\"}"),
            ],
            [
                new SparsePayloadSchema(rootType, "BundlePayload", "{\"$ref\":\"#/components/schemas/BundleValue\"}"),
                new SparsePayloadSchema(typeof(string), "BundleValue", "{\"type\":\"string\"}"),
            ],
            []
        );
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(schema);
        await using var app = builder.Build();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        document.Components!.Schemas!.Keys.ShouldContain("BundleValue");
        document.Components.Schemas.Keys.ShouldContain("BundlePayload");
        (document.Components.Schemas["BundlePayload"] is OpenApiSchemaReference).ShouldBeTrue();
    }

    [Test]
    public async Task RegisteredPayloadSchemasAppearInTheDocument()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
        builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        document.Components!.Schemas!.Keys.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload");
        document.Components.Schemas.Keys.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ModelChange");
        document.Components.Schemas["SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload"]
            .Extensions!.Keys.ShouldContain("x-sparsetypes");
    }

    [Test]
    public async Task StartedAppDocumentKeepsSparsePayloadSchemas()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        document.Paths!.Keys.ShouldContain("/changes");
        document.Components!.Schemas!.Keys.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload");
        document.Components.Schemas.Keys.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ModelChange");

        await app.StopAsync();
    }

    [Test]
    public async Task NestedPayloadSchemasUseCuratedNames()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(Basket.PayloadJson.Schema, [BasketItemDtoSchema()]);
        await using var app = builder.Build();
        app.MapPost("/baskets", (Basket.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        document.Paths!.Keys.ShouldContain("/baskets");
        var names = document.Components!.Schemas!.Keys;
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_Basket_ChangePayload");
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_Basket_ModelChange");
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_BasketItem_ChangePayload");
        names.ShouldContain("SparseTypes_OpenApi_AspNetCore_Tests_BasketItem_ModelChange");
        names.ShouldNotContain(name => name.Contains("Generated_Change"));

        await app.StopAsync();
    }

    [Test]
    public async Task StateAndKindUseWireStringEnums()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(
            Shipment.PayloadJson.Schema,
            [new SparsePayloadSchema(typeof(ShipmentState), SparsePayloadSchemaNames.GetName(typeof(ShipmentState)), "{\"type\":\"integer\"}")]
        );
        builder.Services.AddSparseOpenApi(Basket.PayloadJson.Schema, [BasketItemDtoSchema()]);
        await using var app = builder.Build();
        app.MapPost("/shipments", (Shipment.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        var states = new List<OpenApiSchema>();
        var kinds = new List<OpenApiSchema>();
        var integers = 0;
        var visited = new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance);
        foreach (var schema in document.Components!.Schemas!.Values)
        {
            CollectWireEnums(schema, states, kinds, ref integers, visited);
        }

        (states.Count > 0).ShouldBeTrue();
        (kinds.Count > 0).ShouldBeTrue();
        string.Join(",", states.SelectMany(GetStringValues).Distinct().Order(StringComparer.Ordinal)).ShouldBe("missing,null,redacted,value");
        string.Join(",", kinds.SelectMany(GetStringValues).Distinct().Order(StringComparer.Ordinal)).ShouldBe("add,remove,replace,update");
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
        {
            return;
        }

        if (schema is OpenApiSchemaReference reference)
        {
            CollectWireEnums(reference.Target, states, kinds, ref integers, visited);
            return;
        }
        if (schema is not OpenApiSchema concrete)
        {
            return;
        }

        if (concrete.Type == JsonSchemaType.Integer)
        {
            integers++;
        }

        if (concrete.Properties is not null)
        {
            foreach (var entry in concrete.Properties)
            {
                var target = entry.Value;
                var guard = 0;
                while (target is OpenApiSchemaReference inner && guard++ < 8)
                {
                    target = inner.Target;
                }

                if (string.Equals(entry.Key, "state", StringComparison.Ordinal) && target is OpenApiSchema stateSchema)
                {
                    states.Add(stateSchema);
                }
                else if (string.Equals(entry.Key, "kind", StringComparison.Ordinal) && target is OpenApiSchema kindSchema)
                {
                    kinds.Add(kindSchema);
                }

                CollectWireEnums(entry.Value, states, kinds, ref integers, visited);
            }
        }
        if (concrete.Items is not null)
        {
            CollectWireEnums(concrete.Items, states, kinds, ref integers, visited);
        }

        if (concrete.OneOf is not null)
        {
            foreach (var variant in concrete.OneOf)
            {
                CollectWireEnums(variant, states, kinds, ref integers, visited);
            }
        }
        if (concrete.AnyOf is not null)
        {
            foreach (var variant in concrete.AnyOf)
            {
                CollectWireEnums(variant, states, kinds, ref integers, visited);
            }
        }
        if (concrete.AllOf is not null)
        {
            foreach (var variant in concrete.AllOf)
            {
                CollectWireEnums(variant, states, kinds, ref integers, visited);
            }
        }
    }

    private static IEnumerable<string> GetStringValues(OpenApiSchema schema) =>
        GetStringValues(schema, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance));

    private static IEnumerable<string> GetStringValues(IOpenApiSchema? schema, HashSet<IOpenApiSchema> visited)
    {
        if (schema is null || !visited.Add(schema))
        {
            yield break;
        }

        if (schema is OpenApiSchemaReference reference)
        {
            foreach (var value in GetStringValues(reference.Target, visited))
            {
                yield return value;
            }
            yield break;
        }
        if (schema is not OpenApiSchema concrete)
        {
            yield break;
        }

        if (concrete.Const is { } constant)
        {
            yield return constant;
        }
        if (concrete.Enum is not null)
        {
            foreach (var value in concrete.Enum)
            {
                yield return value!.GetValue<string>();
            }
        }
        if (concrete.OneOf is not null)
        {
            foreach (var variant in concrete.OneOf)
            {
                foreach (var value in GetStringValues(variant, visited))
                {
                    yield return value;
                }
            }
        }
        if (concrete.AnyOf is not null)
        {
            foreach (var variant in concrete.AnyOf)
            {
                foreach (var value in GetStringValues(variant, visited))
                {
                    yield return value;
                }
            }
        }
        if (concrete.AllOf is not null)
        {
            foreach (var variant in concrete.AllOf)
            {
                foreach (var value in GetStringValues(variant, visited))
                {
                    yield return value;
                }
            }
        }
    }

    private static SparsePayloadSchema BasketItemDtoSchema() =>
        new(
            typeof(BasketItem),
            SparsePayloadSchemaNames.GetName(typeof(BasketItem)),
            """{"type":"object","properties":{"id":{"type":"integer","format":"int32"},"key":{"type":"integer","format":"int32"},"quantity":{"type":"integer","format":"int32"}}}"""
        );

    private static SparsePayloadSchemaBundle CreateBundle()
    {
        var rootType = typeof(PayloadModel.ChangePayload);
        return new SparsePayloadSchemaBundle(
            rootType,
            [
                new SparsePayloadSchema(rootType, "BundlePayload", "{\"type\":\"string\",\"enum\":[\"openapi-30\"]}"),
                new SparsePayloadSchema(typeof(string), "BundleString", "{\"type\":\"string\"}"),
            ],
            [
                new SparsePayloadSchema(rootType, "BundlePayload", "{\"type\":\"string\",\"enum\":[\"openapi-31\"]}"),
                new SparsePayloadSchema(typeof(string), "BundleString", "{\"type\":\"string\"}"),
            ],
            []
        );
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<IOpenApiSchema>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(IOpenApiSchema? x, IOpenApiSchema? y) => ReferenceEquals(x, y);

        public int GetHashCode(IOpenApiSchema obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
