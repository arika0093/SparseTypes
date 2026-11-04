using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SparseTypes.Tests;

public sealed class PocoJsonRegressionTests
{
    [Test]
    public void OmittedMembersKeepConstructorAndPropertyDefaults()
    {
        var after = new PocoJsonRegressionHolder
        {
            Defaults = new PocoJsonRegressionDefaultsLeaf { Name = "required" },
            Bound = new PocoJsonRegressionBoundLeaf(),
        };
        var options = CreateOptions();
        var payload = JsonNode.Parse(Serialize(Payload(after), options))!.AsObject();
        PocoValue(payload, "defaults").Clear();
        PocoValue(payload, "defaults")["Name"] = "required";
        PocoValue(payload, "bound").Clear();

        var restored = JsonSerializer.Deserialize(Serialize(payload), GetInfo(options))!.ToChangeSet();

        restored.Defaults.After.Value!.Count.ShouldBe(42);
        restored.Defaults.After.Value.Label.ShouldBe("unset");
        restored.Defaults.After.Value.Fixed.ShouldBe(7);
        restored.Bound.After.Value!.Size.ShouldBe(5);
        restored.Bound.After.Value.Extra.ShouldBe(9);
    }

    [Test]
    public void ExplicitDefaultIgnorePolicyAlsoOverridesTheGlobalNullPolicy()
    {
        var options = CreateOptions(defaultIgnoreCondition: JsonIgnoreCondition.WhenWritingNull);
        var payload = Payload(new PocoJsonRegressionHolder { Ignore = new PocoJsonRegressionIgnoreLeaf() });
        using var document = JsonDocument.Parse(Serialize(payload, options));
        var value = document.RootElement.GetProperty("change").GetProperty("members").GetProperty("ignore").GetProperty("after").GetProperty("value");

        value.TryGetProperty("ForcedZero", out _).ShouldBeTrue();
        value.TryGetProperty("NullOnlyZero", out var nullOnlyZero).ShouldBeTrue();
        nullOnlyZero.GetInt32().ShouldBe(0);
        value.TryGetProperty("DefaultOnly", out _).ShouldBeFalse();
        value.TryGetProperty("Plain", out var plain).ShouldBeTrue();
        plain.GetInt32().ShouldBe(0);
        value.TryGetProperty("PlainText", out _).ShouldBeFalse();
    }

    [Test]
    public void PropertyIgnorePoliciesOverrideTheGlobalDefaultPolicy()
    {
        var options = CreateOptions(defaultIgnoreCondition: JsonIgnoreCondition.WhenWritingDefault);
        var payload = Payload(new PocoJsonRegressionHolder { Ignore = new PocoJsonRegressionIgnoreLeaf() });
        var json = Serialize(payload, options);
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("change").GetProperty("members").GetProperty("ignore").GetProperty("after").GetProperty("value");

        value.TryGetProperty("ForcedZero", out _).ShouldBeTrue();
        value.TryGetProperty("NullOnlyZero", out var nullOnlyZero).ShouldBeTrue();
        nullOnlyZero.GetInt32().ShouldBe(0);
        value.TryGetProperty("DefaultOnly", out _).ShouldBeFalse();
        value.TryGetProperty("Plain", out _).ShouldBeFalse();
        value.TryGetProperty("PlainText", out _).ShouldBeFalse();
        value.TryGetProperty("NullOnlyText", out _).ShouldBeFalse();
        value.TryGetProperty("ForcedNull", out var forcedNull).ShouldBeTrue();
        forcedNull.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public void NestedPocoUsesRegisteredCustomConverter()
    {
        var options = CreateOptions(converter: new PocoJsonRegressionInnerConverter("direct"));
        var payload = Payload(new PocoJsonRegressionHolder { Outer = new PocoJsonRegressionOuter { Reference = new() { Value = 23 } } });

        var json = Serialize(payload, options);
        using var document = JsonDocument.Parse(json);
        document
            .RootElement.GetProperty("change")
            .GetProperty("members")
            .GetProperty("outer")
            .GetProperty("after")
            .GetProperty("value")
            .GetProperty("Reference")
            .GetString()
            .ShouldBe("direct:23");

        var restored = JsonSerializer.Deserialize(json, GetInfo(options))!.ToChangeSet();
        restored.Outer.After.Value!.Reference!.Value.ShouldBe(23);
    }

    [Test]
    public void NestedPocoUsesRegisteredConverterFactory()
    {
        var options = CreateOptions(converter: new PocoJsonRegressionInnerConverterFactory());
        var payload = Payload(new PocoJsonRegressionHolder { Outer = new PocoJsonRegressionOuter { Reference = new() { Value = 47 } } });

        var json = Serialize(payload, options);
        using var document = JsonDocument.Parse(json);
        document
            .RootElement.GetProperty("change")
            .GetProperty("members")
            .GetProperty("outer")
            .GetProperty("after")
            .GetProperty("value")
            .GetProperty("Reference")
            .GetString()
            .ShouldBe("factory:47");

        var restored = JsonSerializer.Deserialize(json, GetInfo(options))!.ToChangeSet();
        restored.Outer.After.Value!.Reference!.Value.ShouldBe(47);
    }

    [Test]
    public void ConstructorBoundRequiredPocoBuildsAndEnforcesItsRequiredMember()
    {
        var options = CreateOptions();
        var payload = Payload(
            new PocoJsonRegressionHolder
            {
                RequiredBound = new PocoJsonRegressionRequiredBound("bound") { Name = "bound", Other = 8 },
            }
        );
        var json = Serialize(payload, options);
        var restored = JsonSerializer.Deserialize(json, GetInfo(options))!.ToChangeSet();
        restored.RequiredBound.After.Value!.Name.ShouldBe("bound");
        restored.RequiredBound.After.Value.Other.ShouldBe(8);

        var incomplete = JsonNode.Parse(json)!.AsObject();
        PocoValue(incomplete, "requiredBound").Remove("Name");
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(Serialize(incomplete), GetInfo(options)));
    }

    private static PocoJsonRegressionHolder.ChangePayload Payload(PocoJsonRegressionHolder after) =>
        PocoJsonRegressionHolder
            .ChangeSet.Between(
                Optional<PocoJsonRegressionHolder.Fragment?>.Present(PocoJsonRegressionHolder.Fragment.From(new PocoJsonRegressionHolder())),
                Optional<PocoJsonRegressionHolder.Fragment?>.Present(PocoJsonRegressionHolder.Fragment.From(after))
            )
            .ToPayload();

    private static JsonSerializerOptions CreateOptions(
        JsonConverter? converter = null,
        JsonIgnoreCondition defaultIgnoreCondition = JsonIgnoreCondition.Never
    )
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = defaultIgnoreCondition,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(PocoJsonRegressionHolder.PayloadJson.Resolver),
        };
        if (converter is not null)
        {
            options.Converters.Add(converter);
        }

        return options;
    }

    private static JsonTypeInfo<PocoJsonRegressionHolder.ChangePayload> GetInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<PocoJsonRegressionHolder.ChangePayload>)options.GetTypeInfo(typeof(PocoJsonRegressionHolder.ChangePayload));

    private static string Serialize(PocoJsonRegressionHolder.ChangePayload payload, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(payload, GetInfo(options));

    private static JsonObject PocoValue(JsonObject payload, string memberName) =>
        payload["change"]!["members"]![memberName]!["after"]!["value"]!.AsObject();

    private static string Serialize(JsonObject value) => value.ToJsonString();
}

public sealed class PocoJsonRegressionInnerConverter(string prefix) : JsonConverter<PocoJsonRegressionInner>
{
    public override PocoJsonRegressionInner Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException();
        if (!text.StartsWith(prefix + ":", StringComparison.Ordinal))
        {
            throw new JsonException();
        }

        return new PocoJsonRegressionInner { Value = int.Parse(text[(prefix.Length + 1)..], CultureInfo.InvariantCulture) };
    }

    public override void Write(Utf8JsonWriter writer, PocoJsonRegressionInner value, JsonSerializerOptions options) =>
        writer.WriteStringValue(prefix + ":" + value.Value.ToString(CultureInfo.InvariantCulture));
}

public sealed class PocoJsonRegressionInnerConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(PocoJsonRegressionInner);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        new PocoJsonRegressionInnerConverter("factory");
}
