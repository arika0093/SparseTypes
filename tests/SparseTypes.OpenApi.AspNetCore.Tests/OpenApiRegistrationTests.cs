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

    [Test]
    public async Task PayloadSchemasPublishExplicitDtoAssociations()
    {
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1 })
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddOpenApi(options => options.OpenApiVersion = version);
            builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
            builder.Services.AddSparseOpenApi(Basket.PayloadJson.Schema, [BasketItemDtoSchema()]);
            await using var app = builder.Build();
            app.MapGet("/model", () => new PayloadModel { Name = "x" });
            app.MapGet("/basket", () => new Basket());
            app.MapGet("/item", () => new BasketItem { Id = 1, Quantity = 2 });
            app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.NoContent());
            app.MapOpenApi();
            await app.StartAsync();

            var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

            var payloadDto = DtoAssociation(document, "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload");
            var basketDto = DtoAssociation(document, "SparseTypes_OpenApi_AspNetCore_Tests_Basket_ChangePayload");
            var itemDto = DtoAssociation(document, "SparseTypes_OpenApi_AspNetCore_Tests_BasketItem_ChangePayload");
            payloadDto.ShouldBe("PayloadModel");
            basketDto.ShouldBe("Basket");
            itemDto.ShouldBe(BasketItemDtoSchema().Name);
            document.Components!.Schemas!.Keys.ShouldContain(payloadDto!);
            document.Components.Schemas.Keys.ShouldContain(basketDto!);
            document.Components.Schemas.Keys.ShouldContain(itemDto!);

            await app.StopAsync();
        }
    }

    [Test]
    public async Task PayloadSchemaOmitsDtoAssociationWhenModelIsAbsent()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.NoContent());
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        DtoAssociation(document, "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload").ShouldBeNull();

        await app.StopAsync();
    }

    [Test]
    public async Task CustomSchemaIdIsUsedForDtoAssociation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi(options =>
        {
            var existing = options.CreateSchemaReferenceId;
            options.CreateSchemaReferenceId = type => type.Type == typeof(PayloadModel) ? "CustomDto" : existing(type);
        });
        builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapGet("/model", () => new PayloadModel { Name = "x" });
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        var dto = DtoAssociation(document, "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload");
        dto.ShouldBe("CustomDto");
        document.Components!.Schemas!.Keys.ShouldContain("CustomDto");

        await app.StopAsync();
    }

    [Test]
    public async Task SameShortModelNamesResolveToTheirOwnDtoComponents()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi();
        builder.Services.AddSparseOpenApi(SameName.V1.Note.PayloadJson.Schema, [NoteDtoSchema(typeof(SameName.V1.Note))]);
        builder.Services.AddSparseOpenApi(SameName.V2.Note.PayloadJson.Schema, [NoteDtoSchema(typeof(SameName.V2.Note))]);
        await using var app = builder.Build();
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        DtoAssociation(document, SparsePayloadSchemaNames.GetName(typeof(SameName.V1.Note.ChangePayload))).ShouldBe(NoteDtoSchema(typeof(SameName.V1.Note)).Name);
        DtoAssociation(document, SparsePayloadSchemaNames.GetName(typeof(SameName.V2.Note.ChangePayload))).ShouldBe(NoteDtoSchema(typeof(SameName.V2.Note)).Name);

        await app.StopAsync();
    }

    private static string? DtoAssociation(OpenApiDocument document, string componentName)
    {
        var schema = document.Components!.Schemas![componentName];
        if (schema.Extensions is null || !schema.Extensions.TryGetValue("x-sparsetypes", out var metadata))
        {
            return null;
        }

        if (metadata is not JsonNodeExtension node || node.Node is not JsonObject metadataObject)
        {
            return null;
        }

        return metadataObject.TryGetPropertyValue("dto", out var dto) ? dto?.GetValue<string>() : null;
    }

    private static SparsePayloadSchema NoteDtoSchema(Type modelType) =>
        new(modelType, SparsePayloadSchemaNames.GetName(modelType), """{"type":"object","properties":{"text":{"type":"string"}}}""");

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

    [Test]
    public async Task ComponentCollisionWithAnUnrelatedSchemaIsRejected()
    {
        const string curated = "SparseTypes_OpenApi_AspNetCore_Tests_PayloadModel_ChangePayload";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi(options =>
        {
            var existing = options.CreateSchemaReferenceId;
            options.CreateSchemaReferenceId = type =>
                type.Type == typeof(CollisionDto) ? curated : existing(type);
        });
        builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapGet("/dto", () => new CollisionDto { Value = 1 });
        app.MapOpenApi();
        await app.StartAsync();

        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => provider.GetOpenApiDocumentAsync());

        exception.Message.ShouldContain(curated);
        exception.Message.ShouldContain("'v1'");
        await app.StopAsync();
    }

    [Test]
    public async Task EndpointSchemaForTheSamePayloadTypeIsStillReplaced()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi(options =>
        {
            options.CreateSchemaReferenceId = type =>
                type.Type == typeof(PayloadModel.ChangePayload) ? "RenamedPayload" : type.Type.Name;
        });
        builder.Services.AddSparseOpenApi(PayloadModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapPost("/changes", (PayloadModel.ChangePayload payload) => Results.Ok(payload));
        app.MapOpenApi();
        await app.StartAsync();

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();

        document.Components!.Schemas!.Keys.ShouldContain("RenamedPayload");
        document.Components.Schemas["RenamedPayload"].Extensions!.Keys.ShouldContain("x-sparsetypes");
        await app.StopAsync();
    }

    private sealed class CollisionDto
    {
        public int Value { get; set; }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<IOpenApiSchema>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(IOpenApiSchema? x, IOpenApiSchema? y) => ReferenceEquals(x, y);

        public int GetHashCode(IOpenApiSchema obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
