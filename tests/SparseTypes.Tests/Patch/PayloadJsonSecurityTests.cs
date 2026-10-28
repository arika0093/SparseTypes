using System.Text.Json;
using System.Text.Json.Nodes;

namespace SparseTypes.Tests.Patch;

[Flags]
public enum TransportFlags : ulong
{
    None = 0,
    Read = 1,
    Write = 2,
    High = 1UL << 63,
}

[Flags]
public enum SignedTransportFlags : long
{
    None = 0,
    Read = 1,
    High = long.MinValue,
}

[SparseModel]
public partial class EnumSecurityModel
{
    public TransportFlags Flags { get; set; }
    public SignedTransportFlags SignedFlags { get; set; }
}

public sealed class PayloadJsonSecurityTests
{
    private static JsonSerializerOptions KeyedOptions(bool ignoreCase) =>
        new() { PropertyNameCaseInsensitive = ignoreCase, TypeInfoResolver = KeyedServerHolder.PayloadJson.Resolver };

    private static string KeyedJson() =>
        JsonSerializer.Serialize(
            KeyedServerHolder
                .ChangeSet.Between(
                    Optional<KeyedServerHolder.Fragment?>.Present(
                        KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "before" }] })
                    ),
                    Optional<KeyedServerHolder.Fragment?>.Present(
                        KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "after" }] })
                    )
                )
                .ToPayload(),
            KeyedServerHolder.PayloadJson.Default
        );

    [Test]
    public void WebCaseMatchingAppliesToEveryPayloadScope()
    {
        var original = KeyedJson();
        var json = original;
        foreach (
            var property in new[] { "version", "schema", "change", "kind", "members", "items", "key", "before", "after", "state", "value", "name" }
        )
        {
            json = json.Replace("\"" + property + "\":", "\"" + property.ToUpperInvariant() + "\":", StringComparison.Ordinal);
        }
        var restored = JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(json, KeyedOptions(true))!;
        JsonSerializer.Serialize(restored, KeyedServerHolder.PayloadJson.Default).ShouldBe(original);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(json, KeyedOptions(false)));
    }

    [Test]
    [Arguments("key", "\"other\"")]
    [Arguments("kind", "\"update\"")]
    [Arguments("state", "\"missing\"")]
    [Arguments("value", "\"other\"")]
    [Arguments("members", "{}")]
    public void CaseVariantsCannotBypassDuplicateGuards(string property, string value)
    {
        var marker = "\"" + property + "\":";
        var json = KeyedJson().Replace(marker, "\"" + property.ToUpperInvariant() + "\":" + value + "," + marker, StringComparison.Ordinal);
        json.ShouldNotBe(KeyedJson());
        Should
            .Throw<JsonException>(() => JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(json, KeyedOptions(true)))
            .Message.ShouldContain("Duplicate JSON property");
    }

    [Test]
    [Arguments("beforeIndex", "2147483648")]
    [Arguments("afterIndex", "\"1\"")]
    [Arguments("beforeIndex", "null")]
    [Arguments("isReordered", "\"true\"")]
    [Arguments("isReordered", "{}")]
    public void InvalidItemMetadataThrowsJsonException(string property, string value)
    {
        var node = JsonNode.Parse(KeyedJson())!;
        node["change"]!["members"]!["items"]!["items"]![0]![property] = JsonNode.Parse(value);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(node.ToJsonString(), KeyedOptions(false)));
    }

    [Test]
    [Arguments("7")]
    [Arguments("\"not-a-guid\"")]
    [Arguments("{}")]
    public void InvalidTemporaryKeysThrowJsonException(string value)
    {
        var json =
            """{"version":"0.1","schema":"SCHEMA","change":{"kind":"update","members":{"lines":{"kind":"update","items":[{"key":0,"kind":"add","temporaryKey":VALUE}]}}}}"""
                .Replace("SCHEMA", new TempOrderHolder.ChangePayload().Schema, StringComparison.Ordinal)
                .Replace("VALUE", value, StringComparison.Ordinal);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, TempOrderHolder.PayloadJson.Default));
    }

    [Test]
    public void UnsupportedEditsAreRejectedInsteadOfDiscarded()
    {
        var json =
            """{"version":"0.1","schema":"SCHEMA","change":{"kind":"update","members":{"prices":{"kind":"update","items":[{"key":"a","kind":"replace","edit":{},"before":{"state":"value","value":"1"},"after":{"state":"value","value":"2"}}]}}}}""".Replace(
                "SCHEMA",
                new ScalarLedger.ChangePayload().Schema,
                StringComparison.Ordinal
            );
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, ScalarLedger.PayloadJson.Default)).Message.ShouldContain("edit payload");
    }

    [Test]
    [Arguments("kind", "99")]
    [Arguments("maybeKind", "-1")]
    [Arguments("wideKind", "\"999\"")]
    public void UndefinedEnumValuesAreRejected(string member, string value)
    {
        var json =
            """{"version":"0.1","schema":"SCHEMA","change":{"kind":"update","members":{"MEMBER":{"kind":"replace","before":{"state":"value","value":VALUE},"after":{"state":"value","value":VALUE}}}}}"""
                .Replace("SCHEMA", new ScalarLedger.ChangePayload().Schema, StringComparison.Ordinal)
                .Replace("MEMBER", member, StringComparison.Ordinal)
                .Replace("VALUE", value, StringComparison.Ordinal);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, ScalarLedger.PayloadJson.Default));
    }

    [Test]
    public void FlagsAcceptDeclaredBitsIncludingTheSignBit()
    {
        var flags = TransportFlags.Read | TransportFlags.Write | TransportFlags.High;
        var signed = SignedTransportFlags.Read | SignedTransportFlags.High;
        var before = Optional<EnumSecurityModel.Fragment?>.Present(EnumSecurityModel.Fragment.From(new EnumSecurityModel()));
        var after = Optional<EnumSecurityModel.Fragment?>.Present(
            EnumSecurityModel.Fragment.From(new EnumSecurityModel { Flags = flags, SignedFlags = signed })
        );
        var payload = EnumSecurityModel.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload, EnumSecurityModel.PayloadJson.Default);
        var restored = JsonSerializer.Deserialize(json, EnumSecurityModel.PayloadJson.Default)!.ToChangeSet();
        restored.Flags.After.Value.ShouldBe(flags);
        restored.SignedFlags.After.Value.ShouldBe(signed);
        var node = JsonNode.Parse(json)!;
        node["change"]!["members"]!["flags"]!["after"]!["value"] = "4";
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(node.ToJsonString(), EnumSecurityModel.PayloadJson.Default));
        node = JsonNode.Parse(json)!;
        node["change"]!["members"]!["signedFlags"]!["after"]!["value"] = "2";
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(node.ToJsonString(), EnumSecurityModel.PayloadJson.Default));
    }
}
