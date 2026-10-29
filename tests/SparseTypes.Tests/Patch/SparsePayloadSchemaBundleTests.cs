namespace SparseTypes.Tests.Patch;

public sealed class SparsePayloadSchemaBundleTests
{
    [Test]
    public void BundleCopiesBothDialectSchemaSetsAndExternalRequirements()
    {
        var root = typeof(SparsePayloadSchemaBundleTests);
        var schema30 = new List<SparsePayloadSchema>
        {
            new(root, "Payload", "{\"type\":\"object\",\"nullable\":true}", "{\"type\":[\"object\",\"null\"]}"),
            new(typeof(string), "String", "{\"type\":\"string\"}", "{\"type\":\"string\"}"),
        };
        var schema31 = new List<SparsePayloadSchema>
        {
            new(root, "Payload", "{\"type\":\"object\",\"nullable\":true}", "{\"type\":[\"object\",\"null\"]}"),
            new(typeof(string), "String", "{\"type\":\"string\"}", "{\"type\":\"string\"}"),
        };
        var external = new List<Type> { typeof(decimal) };

        var bundle = new SparsePayloadSchemaBundle(root, schema30, schema31, external);
        schema30.Clear();
        schema31.Clear();
        external.Clear();

        bundle.RootType.ShouldBe(root);
        bundle.RootSchemaName.ShouldBe("Payload");
        bundle.OpenApi30Schemas.Count.ShouldBe(2);
        bundle.OpenApi31Schemas.Count.ShouldBe(2);
        bundle.OpenApi30Schemas[0].OpenApi30JsonSchema.ShouldContain("\"nullable\":true");
        bundle.OpenApi31Schemas[0].OpenApi31JsonSchema.ShouldContain("\"null\"");
        bundle.RequiredExternalSchemaTypes.ShouldBe([typeof(decimal)]);
    }

    [Test]
    public void BundleAcceptsNamedOptionsForDialectSchemaSets()
    {
        var root = typeof(SparsePayloadSchemaBundleTests);
        var schema30 = new SparsePayloadSchema(root, "Payload", "{\"type\":\"object\"}");
        var schema31 = new SparsePayloadSchema(root, "Payload", "{\"type\":\"object\"}");

        var bundle = new SparsePayloadSchemaBundle(
            root,
            new SparsePayloadSchemaBundleOptions
            {
                Schema30 = () => [schema30],
                Schema31 = () => [schema31],
                SchemaIdentities = [new(root, "Payload")],
                RequiredExternalSchemaTypes = [typeof(decimal)],
            }
        );

        bundle.RootSchemaName.ShouldBe("Payload");
        bundle.OpenApi30Schemas.Select(static item => item.JsonSchema).ShouldBe([schema30.OpenApi30JsonSchema]);
        bundle.OpenApi31Schemas.Select(static item => item.JsonSchema).ShouldBe([schema31.OpenApi31JsonSchema]);
        bundle.RequiredExternalSchemaTypes.ShouldBe([typeof(decimal)]);
    }

    [Test]
    public void BundleMaterializesOnlyTheRequestedDialect()
    {
        var root = typeof(SparsePayloadSchemaBundleTests);
        var schema = new SparsePayloadSchema(root, "Payload", "{\"type\":\"object\"}");
        var schema30Calls = 0;
        var schema31Calls = 0;
        var bundle = new SparsePayloadSchemaBundle(
            root,
            new SparsePayloadSchemaBundleOptions
            {
                Schema30 = () =>
                {
                    schema30Calls++;
                    return [schema];
                },
                Schema31 = () =>
                {
                    schema31Calls++;
                    return [schema];
                },
                SchemaIdentities = [new(root, "Payload")],
            }
        );

        bundle.SchemaIdentities.Count.ShouldBe(1);
        schema30Calls.ShouldBe(0);
        schema31Calls.ShouldBe(0);

        bundle.GetSchemas(SparsePayloadSchemaDialect.OpenApi30).Count.ShouldBe(1);
        schema30Calls.ShouldBe(1);
        schema31Calls.ShouldBe(0);

        bundle.GetSchemas(SparsePayloadSchemaDialect.OpenApi30).Count.ShouldBe(1);
        schema30Calls.ShouldBe(1);
        bundle.GetSchemas(SparsePayloadSchemaDialect.OpenApi31).Count.ShouldBe(1);
        schema31Calls.ShouldBe(1);
    }

    [Test]
    public void BundleRejectsDuplicateNamesMissingRootsAndGeneratedExternalTypes()
    {
        var root = typeof(SparsePayloadSchemaBundleTests);
        var valid = new SparsePayloadSchema(root, "Payload", "{\"type\":\"object\"}");

        Should.Throw<ArgumentException>(() =>
            new SparsePayloadSchemaBundle(root, [valid, new SparsePayloadSchema(typeof(string), "Payload", "{\"type\":\"string\"}")], [valid], [])
        );
        Should.Throw<ArgumentException>(() =>
            new SparsePayloadSchemaBundle(root, [new SparsePayloadSchema(typeof(string), "String", "{\"type\":\"string\"}")], [valid], [])
        );
        Should.Throw<ArgumentException>(() => new SparsePayloadSchemaBundle(root, [valid], [valid], [root]));
    }

    [Test]
    public void BundleIncludesChildSchemasAndHandlesCyclicModelDependencies()
    {
        SparsePayloadSchemaBundle? first = null;
        SparsePayloadSchemaBundle? second = null;
        first = new SparsePayloadSchemaBundle(
            typeof(FirstPayload),
            [new(typeof(FirstPayload), "First", "{\"type\":\"object\"}")],
            [new(typeof(FirstPayload), "First", "{\"type\":\"object\"}")],
            [],
            [() => second ?? throw new InvalidOperationException("The second schema bundle is not initialized.")]
        );
        second = new SparsePayloadSchemaBundle(
            typeof(SecondPayload),
            [new(typeof(SecondPayload), "Second", "{\"type\":\"object\"}")],
            [new(typeof(SecondPayload), "Second", "{\"type\":\"object\"}")],
            [typeof(decimal)],
            [() => first ?? throw new InvalidOperationException("The first schema bundle is not initialized.")]
        );

        first.OpenApi30Schemas.Select(static schema => schema.Name).ShouldBe(["First", "Second"]);
        first.OpenApi31Schemas.Select(static schema => schema.Name).ShouldBe(["First", "Second"]);
        first.RequiredExternalSchemaTypes.ShouldBe([typeof(decimal)]);
    }

    [Test]
    public void GeneratedBundleContainsCompleteDialectSchemasAndChildComponents()
    {
        var bundle = PayloadSchemaProbe.PayloadJson.Schema;
        var schema30 = bundle.OpenApi30Schemas;
        var schema31 = bundle.OpenApi31Schemas;
        var componentNames = schema31.Select(static schema => schema.Name).ToHashSet(StringComparer.Ordinal);
        var references = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in schema30)
        {
            using var document = System.Text.Json.JsonDocument.Parse(descriptor.OpenApi30JsonSchema);
            CollectReferences(document.RootElement, references);
        }

        foreach (var descriptor in schema31)
        {
            using var document = System.Text.Json.JsonDocument.Parse(descriptor.OpenApi31JsonSchema);
            CollectReferences(document.RootElement, references);
        }

        using var root30 = System.Text.Json.JsonDocument.Parse(
            schema30.Single(static schema => schema.Type == typeof(PayloadSchemaProbe.ChangePayload)).OpenApi30JsonSchema
        );
        root30.RootElement.GetProperty("properties").GetProperty("version").GetProperty("enum")[0].GetString().ShouldBe("0.1");
        root30.RootElement.GetProperty("properties").GetProperty("schema").GetProperty("pattern").GetString().ShouldBe("^sha256:[0-9a-f]{64}$");
        root30.RootElement.GetProperty("required").EnumerateArray().ShouldContain(static value => value.GetString() == "schema");

        using var root31 = System.Text.Json.JsonDocument.Parse(
            schema31.Single(static schema => schema.Type == typeof(PayloadSchemaProbe.ChangePayload)).OpenApi31JsonSchema
        );
        root31.RootElement.GetProperty("properties").GetProperty("version").GetProperty("const").GetString().ShouldBe("0.1");
        root31.RootElement.GetProperty("properties").GetProperty("schema").GetProperty("pattern").GetString().ShouldBe("^sha256:[0-9a-f]{64}$");
        root31.RootElement.GetProperty("required").EnumerateArray().ShouldContain(static value => value.GetString() == "schema");

        var allSchemas = string.Join("\n", schema31.Select(static schema => schema.OpenApi31JsonSchema));
        allSchemas.ShouldContain("\"x-sparse-type\":\"int64\"");
        allSchemas.ShouldContain("\"x-sparse-type\":\"uint64\"");
        allSchemas.ShouldContain("\"x-sparse-type\":\"decimal\"");
        allSchemas.ShouldContain("\"x-sparse-type\":\"date-time-utc-or-unspecified\"");
        allSchemas.ShouldContain("\"x-sparse-type\":\"date-time-offset\"");
        allSchemas.ShouldContain("\"pattern\":\"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$\"");

        schema31.ShouldContain(static schema => schema.Type == typeof(PayloadSchemaChild.ChangePayload));
        foreach (var reference in references)
        {
            componentNames.ShouldContain(reference);
        }

        static void CollectReferences(System.Text.Json.JsonElement element, HashSet<string> destination)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("$ref"))
                    {
                        var reference = property.Value.GetString() ?? string.Empty;
                        destination.Add(reference["#/components/schemas/".Length..]);
                    }
                    else
                    {
                        CollectReferences(property.Value, destination);
                    }
                }
            }
            else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    CollectReferences(item, destination);
                }
            }
        }
    }

    [Test]
    public void KeyedModelItemSchemaMatchesItsJsonSnapshotAndReorderFlag()
    {
        var schemas = PayloadSchemaOrder.PayloadJson.Schema;
        var item30 = schemas.OpenApi30Schemas.Single(static schema => schema.Name.Contains("_Item_lines", StringComparison.Ordinal));
        var item31 = schemas.OpenApi31Schemas.Single(static schema => schema.Name.Contains("_Item_lines", StringComparison.Ordinal));

        using var document30 = System.Text.Json.JsonDocument.Parse(item30.OpenApi30JsonSchema);
        using var document31 = System.Text.Json.JsonDocument.Parse(item31.OpenApi31JsonSchema);
        var add30 = document30.RootElement.GetProperty("oneOf")[0];
        var add31 = document31.RootElement.GetProperty("oneOf")[0];
        var valueRef30 = GetValueReference(add30.GetProperty("properties").GetProperty("after"));
        var valueRef31 = GetValueReference(add31.GetProperty("properties").GetProperty("after"));
        valueRef30.ShouldEndWith("PayloadSchemaOrderLine");
        valueRef31.ShouldEndWith("PayloadSchemaOrderLine");
        valueRef30.ShouldNotEndWith("_Root");
        valueRef31.ShouldNotEndWith("_Root");

        var reorder30 = add30.GetProperty("properties").GetProperty("isReordered");
        reorder30.GetProperty("type").GetString().ShouldBe("boolean");
        reorder30.TryGetProperty("enum", out _).ShouldBeFalse();
        var reorder31 = add31.GetProperty("properties").GetProperty("isReordered");
        reorder31.GetProperty("type").GetString().ShouldBe("boolean");
        reorder31.TryGetProperty("const", out _).ShouldBeFalse();

        static string GetValueReference(System.Text.Json.JsonElement endpoint) =>
            endpoint.GetProperty("oneOf")
                .EnumerateArray()
                .Single(variant => GetStringValue(variant.GetProperty("properties").GetProperty("state")) == "value")
                .GetProperty("properties")
                .GetProperty("value")
                .GetProperty("$ref")
                .GetString()!
                .Split('/')
                .Last();

        static string? GetStringValue(System.Text.Json.JsonElement schema) =>
            schema.TryGetProperty("const", out var constant)
                ? constant.GetString()
                : schema.GetProperty("enum")[0].GetString();
    }

    [Test]
    public void SchemaIdentityTracksNestedWireShapesInsteadOfNestedClrNames()
    {
        var first = new PayloadSchemaNestedProbeA.ChangePayload().Schema;
        var equivalent = new PayloadSchemaNestedProbeB.ChangePayload().Schema;
        var changed = new PayloadSchemaNestedProbeChanged.ChangePayload().Schema;

        equivalent.ShouldBe(first);
        changed.ShouldNotBe(first);
    }

    [Test]
    public void CompatibleModelsExchangeAddressedMembersAcrossSchemaMismatch()
    {
        var before = Optional<PayloadSchemaNestedProbeA.Fragment?>.Present(
            PayloadSchemaNestedProbeA.Fragment.From(new PayloadSchemaNestedProbeA { Child = new PayloadSchemaNestedChildA { Count = 1 } })
        );
        var after = Optional<PayloadSchemaNestedProbeA.Fragment?>.Present(
            PayloadSchemaNestedProbeA.Fragment.From(new PayloadSchemaNestedProbeA { Child = new PayloadSchemaNestedChildA { Count = 2 } })
        );
        var json = System.Text.Json.JsonSerializer.Serialize(
            PayloadSchemaNestedProbeA.ChangeSet.Between(before, after).ToPayload(),
            PayloadSchemaNestedProbeA.PayloadJson.Default
        );

        var received = System.Text.Json.JsonSerializer.Deserialize<PayloadSchemaNestedProbeCompatible.ChangePayload>(
            json,
            PayloadSchemaNestedProbeCompatible.PayloadJson.Default
        )!;

        received.Schema.ShouldNotBe(new PayloadSchemaNestedProbeCompatible.ChangePayload().Schema);
        received.Validate().IsValid.ShouldBeTrue();
        received.ToChangeSet().Child.Count.After.Value.ShouldBe(2);
    }

    private sealed class FirstPayload { }

    private sealed class SecondPayload { }
}

[SparseModel]
public partial class PayloadSchemaChild
{
    public int Count { get; set; }
}

[SparseModel]
public partial class PayloadSchemaProbe
{
    public string? Label { get; set; }

    public PayloadSchemaChild? Child { get; set; }

    public long WideInteger { get; set; }

    public ulong UnsignedWideInteger { get; set; }

    public decimal ExactDecimal { get; set; }

    public PayloadSchemaWideEnum WideEnum { get; set; }

    public Guid Identifier { get; set; }

    public DateTime Moment { get; set; }

    public DateTimeOffset OffsetMoment { get; set; }
}

public enum PayloadSchemaWideEnum : long
{
    Large = 9007199254740993,
}

[SparseModel]
public partial class PayloadSchemaNestedChildA
{
    public int Count { get; set; }
}

[SparseModel]
public partial class PayloadSchemaNestedChildB
{
    public int Count { get; set; }
}

[SparseModel]
public partial class PayloadSchemaNestedChildChanged
{
    public long Count { get; set; }
}

[SparseModel]
public partial class PayloadSchemaNestedProbeA
{
    public PayloadSchemaNestedChildA? Child { get; set; }
}

[SparseModel]
public partial class PayloadSchemaNestedProbeB
{
    public PayloadSchemaNestedChildB? Child { get; set; }
}

[SparseModel]
public partial class PayloadSchemaNestedProbeChanged
{
    public PayloadSchemaNestedChildChanged? Child { get; set; }
}

[SparseModel]
public partial class PayloadSchemaNestedProbeCompatible
{
    public PayloadSchemaNestedChildA? Child { get; set; }

    public string? Note { get; set; }
}

[SparseModel]
public partial class PayloadSchemaOrderLine
{
    public int Id { get; set; }

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class PayloadSchemaOrder
{
    public List<PayloadSchemaOrderLine> Lines { get; set; } = [];
}
