using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using SparseTypes.OpenApi.Swashbuckle;
using Swashbuckle.AspNetCore.Swagger;

namespace SparseTypes.OpenApi.Swashbuckle.Tests;

public sealed class SparsePayloadSchemaTests
{
    [Test]
    public async Task BundleSelectsTheConfiguredOpenApiDialect()
    {
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1 })
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
                options.CustomSchemaIds(type =>
                    type == typeof(OpenApiTestModel.ChangePayload)
                        ? "App_ChangePayload"
                        : "App_"
                            + type.FullName!.Replace('.', '_')
                                .Replace('+', '_')
                                .Replace('`', '_')
                                .Replace('[', '_')
                                .Replace(']', '_')
                                .Replace(',', '_')
                                .Replace(' ', '_')
                )
            );
            builder.Services.AddSparseOpenApi(CreateBundle(), version);
            await using var app = builder.Build();
            app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.Ok(payload));
            await app.StartAsync();
            var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
            var root = (OpenApiSchema)document.Components!.Schemas!["BundlePayload"];
            root.Enum!.Select(static value => value!.GetValue<string>())
                .ShouldBe(version == OpenApiSpecVersion.OpenApi3_0 ? ["openapi-30"] : ["openapi-31"]);
            document.Components.Schemas.Keys.ShouldContain("App_ChangePayload");
            await app.StopAsync();
        }
    }

    [Test]
    public void BundleRejectsAnUnregisteredExternalLeafSchema()
    {
        var rootType = typeof(OpenApiTestModel.ChangePayload);
        var bundle = new SparsePayloadSchemaBundle(
            rootType,
            [new SparsePayloadSchema(rootType, "BundlePayload", "{\"type\":\"object\"}")],
            [new SparsePayloadSchema(rootType, "BundlePayload", "{\"type\":\"object\"}")],
            [typeof(DateOnly)]
        );
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(bundle, OpenApiSpecVersion.OpenApi3_0);
        using var app = builder.Build();

        var provider = app.Services.GetRequiredService<ISwaggerProvider>();
        var exception = Should.Throw<InvalidOperationException>(() => provider.GetSwagger("v1"));

        exception.Message.ShouldContain(typeof(DateOnly).ToString());
    }

    [Test]
    public void BundleRejectsAnUnregisteredComponentReference()
    {
        var rootType = typeof(OpenApiTestModel.ChangePayload);
        var bundle = new SparsePayloadSchemaBundle(
            rootType,
            [new SparsePayloadSchema(rootType, "BundlePayload", "{\"$ref\":\"#/components/schemas/Missing\"}")],
            [new SparsePayloadSchema(rootType, "BundlePayload", "{\"$ref\":\"#/components/schemas/Missing\"}")],
            []
        );
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(bundle, OpenApiSpecVersion.OpenApi3_0);
        using var app = builder.Build();

        var provider = app.Services.GetRequiredService<ISwaggerProvider>();
        var exception = Should.Throw<InvalidOperationException>(() => provider.GetSwagger("v1"));

        exception.Message.ShouldContain("unregistered component 'Missing'");
    }

    [Test]
    public void BundleAcceptsAnExplicitExternalSchemaForAReferencedLeaf()
    {
        var rootType = typeof(OpenApiTestModel.ChangePayload);
        var schema = "{\"type\":\"object\",\"properties\":{\"date\":{\"$ref\":\"#/components/schemas/ExternalDate\"}}}";
        var bundle = new SparsePayloadSchemaBundle(
            rootType,
            [new SparsePayloadSchema(rootType, "BundlePayload", schema)],
            [new SparsePayloadSchema(rootType, "BundlePayload", schema)],
            [typeof(DateOnly)]
        );
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(
            bundle,
            OpenApiSpecVersion.OpenApi3_0,
            [new SparsePayloadSchema(typeof(DateOnly), "ExternalDate", "{\"type\":\"string\",\"format\":\"date\"}")]
        );
        using var app = builder.Build();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Components!.Schemas!.Keys.ShouldContain("ExternalDate");
    }

    [Test]
    public void BundleAcceptsAComponentReferenceAsTheRootSchema()
    {
        var rootType = typeof(OpenApiTestModel.ChangePayload);
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
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(schema, OpenApiSpecVersion.OpenApi3_1);
        using var app = builder.Build();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Components!.Schemas!.Keys.ShouldContain("BundleValue");
        document.Components.Schemas.Keys.ShouldContain("BundlePayload");
        (document.Components.Schemas["BundlePayload"] is OpenApiSchemaReference).ShouldBeTrue();
    }

    [Test]
    public void BundleRegistrationsRejectMixedOpenApiVersions()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(CreateBundle(), OpenApiSpecVersion.OpenApi3_0);

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.Services.AddSparseOpenApi(CreateBundle(), OpenApiSpecVersion.OpenApi3_1)
        );

        exception.Message.ShouldContain("cannot mix OpenAPI 3.0 and 3.1");
    }

    [Test]
    public void GeneratedPayloadProvidesStableSchemasAndDiscriminator()
    {
        var schemas = OpenApiTestModel.PayloadJson.Schema.OpenApi31Schemas;

        schemas.Select(static schema => schema.Name).ShouldContain("SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload");
        schemas.Select(static schema => schema.Name).ShouldContain("SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ModelChange");
        schemas
            .Single(static schema => schema.Name.EndsWith("_ModelChange", StringComparison.Ordinal))
            .OpenApi31JsonSchema.ShouldContain("\"oneOf\"");
        var envelope = schemas.Single(static schema => schema.Name.EndsWith("_ChangePayload", StringComparison.Ordinal));
        envelope.OpenApi31JsonSchema.ShouldContain("\"recordedAt\"");
        envelope.OpenApi31JsonSchema.ShouldContain("\"format\":\"date-time\"");
        envelope.OpenApi31JsonSchema.ShouldContain("\"null\"");
        var envelope30 = OpenApiTestModel.PayloadJson.Schema.OpenApi30Schemas.Single(static schema =>
            schema.Name.EndsWith("_ChangePayload", StringComparison.Ordinal)
        );
        using var openApi30 = System.Text.Json.JsonDocument.Parse(envelope30.OpenApi30JsonSchema);
        var recordedAt30 = openApi30.RootElement.GetProperty("properties").GetProperty("recordedAt");
        recordedAt30.GetProperty("format").GetString().ShouldBe("date-time");
        recordedAt30.GetProperty("nullable").GetBoolean().ShouldBeTrue();
        schemas.Select(static schema => schema.Name).Distinct().Count().ShouldBe(schemas.Count);
        schemas
            .Select(static schema => schema.Name)
            .Any(static name => System.Text.RegularExpressions.Regex.IsMatch(name, "_[0-9A-F]{8}(?:_|$)"))
            .ShouldBeFalse();
    }

    [Test]
    public void SwashbuckleAddsRegisteredSchemasToComponents()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema);
        builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema);
        using var app = builder.Build();
        app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.Ok(payload));

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Components!.Schemas!.Keys.ShouldContain("SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload");
        document.Components!.Schemas!.Keys.ShouldContain("SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ModelChange");
        document.Components.Schemas["SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload"]
            .Extensions!.Keys.ShouldContain("x-sparsetypes");
    }

    [Test]
    public async Task StartedAppDocumentKeepsSparsePayloadSchemas()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.Ok(payload));
        await app.StartAsync();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Paths!.Keys.ShouldContain("/changes");
        document.Components!.Schemas!.Keys.ShouldContain("SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload");
        document.Components.Schemas.Keys.ShouldContain("SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ModelChange");

        await app.StopAsync();
    }

    [Test]
    public void StateAndKindUseWireStringEnums()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(
            OpenApiShipment.PayloadJson.Schema,
            OpenApiSpecVersion.OpenApi3_0,
            [
                new SparsePayloadSchema(
                    typeof(OpenApiShipmentState),
                    SparseTypes.SparsePayloadSchemaNames.GetName(typeof(OpenApiShipmentState)),
                    "{\"type\":\"integer\"}"
                ),
            ]
        );
        builder.Services.AddSparseOpenApi(OpenApiBasket.PayloadJson.Schema, OpenApiSpecVersion.OpenApi3_0, [OpenApiBasketItemDtoSchema()]);
        using var app = builder.Build();
        app.MapPost("/shipments", (OpenApiShipment.ChangePayload payload) => Results.Ok(payload));

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

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

        // User domain enums stay numeric.
        var userEnum = ResolveComponent(document, SparseTypes.SparsePayloadSchemaNames.GetName(typeof(OpenApiShipmentState)));
        userEnum.Type.ShouldBe(JsonSchemaType.Integer);
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
    public async Task ComponentCollisionWithAnUnrelatedSchemaIsRejected()
    {
        const string curated = "SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
            options.CustomSchemaIds(type => type == typeof(SwashbuckleCollisionDto) ? curated : TestSchemaId(type)));
        builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapGet("/dto", () => new SwashbuckleCollisionDto { Value = 1 });
        await app.StartAsync();

        var provider = app.Services.GetRequiredService<ISwaggerProvider>();
        var exception = Should.Throw<InvalidOperationException>(() => provider.GetSwagger("v1"));

        exception.Message.ShouldContain(curated);
        exception.Message.ShouldContain("'v1'");
        await app.StopAsync();
    }

    [Test]
    public async Task EndpointSchemaForTheSamePayloadTypeIsStillReplaced()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
            options.CustomSchemaIds(type => type == typeof(OpenApiTestModel.ChangePayload) ? "RenamedPayload" : TestSchemaId(type)));
        builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.Ok(payload));
        await app.StartAsync();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Components!.Schemas!.Keys.ShouldContain("RenamedPayload");
        document.Components.Schemas["RenamedPayload"].Extensions!.Keys.ShouldContain("x-sparsetypes");
        await app.StopAsync();
    }

    private sealed class SwashbuckleCollisionDto
    {
        public int Value { get; set; }
    }

    [Test]
    public async Task PayloadSchemasPublishExplicitDtoAssociations()
    {
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1 })
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema, version);
            builder.Services.AddSparseOpenApi(OpenApiBasket.PayloadJson.Schema, version, [OpenApiBasketItemDtoSchema()]);
            await using var app = builder.Build();
            app.MapGet("/model", () => new OpenApiTestModel { DisplayName = "x", Count = 1 });
            app.MapGet("/basket", () => new OpenApiBasket());
            app.MapGet("/item", () => new OpenApiBasketItem { Id = 1, Quantity = 2 });
            app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.NoContent());
            await app.StartAsync();

            var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

            var payloadDto = DtoAssociation(document, "SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload");
            var basketDto = DtoAssociation(document, "SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiBasket_ChangePayload");
            var itemDto = DtoAssociation(document, "SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiBasketItem_ChangePayload");
            payloadDto.ShouldBe("OpenApiTestModel");
            basketDto.ShouldBe("OpenApiBasket");
            itemDto.ShouldBe(OpenApiBasketItemDtoSchema().Name);
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
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapPost("/changes", (OpenApiTestModel.ChangePayload payload) => Results.NoContent());
        await app.StartAsync();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        DtoAssociation(document, "SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload").ShouldBeNull();

        await app.StopAsync();
    }

    [Test]
    public async Task CustomSchemaIdIsUsedForDtoAssociation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
            options.CustomSchemaIds(type => type == typeof(OpenApiTestModel) ? "CustomDto" : TestSchemaId(type)));
        builder.Services.AddSparseOpenApi(OpenApiTestModel.PayloadJson.Schema);
        await using var app = builder.Build();
        app.MapGet("/model", () => new OpenApiTestModel { DisplayName = "x", Count = 1 });
        await app.StartAsync();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var dto = DtoAssociation(document, "SparseTypes_OpenApi_Swashbuckle_Tests_OpenApiTestModel_ChangePayload");
        dto.ShouldBe("CustomDto");
        document.Components!.Schemas!.Keys.ShouldContain("CustomDto");

        await app.StopAsync();
    }

    [Test]
    public async Task SameShortModelNamesResolveToTheirOwnDtoComponents()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSparseOpenApi(SameName.V1.Note.PayloadJson.Schema, OpenApiSpecVersion.OpenApi3_1, [NoteDtoSchema(typeof(SameName.V1.Note))]);
        builder.Services.AddSparseOpenApi(SameName.V2.Note.PayloadJson.Schema, OpenApiSpecVersion.OpenApi3_1, [NoteDtoSchema(typeof(SameName.V2.Note))]);
        await using var app = builder.Build();
        await app.StartAsync();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        DtoAssociation(document, SparseTypes.SparsePayloadSchemaNames.GetName(typeof(SameName.V1.Note.ChangePayload))).ShouldBe(NoteDtoSchema(typeof(SameName.V1.Note)).Name);
        DtoAssociation(document, SparseTypes.SparsePayloadSchemaNames.GetName(typeof(SameName.V2.Note.ChangePayload))).ShouldBe(NoteDtoSchema(typeof(SameName.V2.Note)).Name);

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
        new(modelType, SparseTypes.SparsePayloadSchemaNames.GetName(modelType), """{"type":"object","properties":{"text":{"type":"string"}}}""");

    private static string TestSchemaId(Type type) =>
        "Test_"
            + (type.FullName ?? type.Name).Replace('.', '_')
                .Replace('+', '_')
                .Replace('`', '_')
                .Replace('[', '_')
                .Replace(']', '_')
                .Replace(',', '_')
                .Replace(' ', '_');

    private static OpenApiSchema ResolveComponent(OpenApiDocument document, string name)
    {
        IOpenApiSchema schema = document.Components!.Schemas![name];
        var guard = 0;
        while (schema is OpenApiSchemaReference reference && guard++ < 8)
        {
            if (reference.Target is not IOpenApiSchema target)
            {
                break;
            }

            schema = target;
        }
        return (OpenApiSchema)schema;
    }

    private static SparsePayloadSchema OpenApiBasketItemDtoSchema() =>
        new(
            typeof(OpenApiBasketItem),
            SparseTypes.SparsePayloadSchemaNames.GetName(typeof(OpenApiBasketItem)),
            """{"type":"object","properties":{"id":{"type":"integer","format":"int32"},"key":{"type":"integer","format":"int32"},"quantity":{"type":"integer","format":"int32"}}}"""
        );

    private static SparsePayloadSchemaBundle CreateBundle()
    {
        var rootType = typeof(OpenApiTestModel.ChangePayload);
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
