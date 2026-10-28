using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SparseTypes.Tests.Patch;

public sealed class PayloadJsonResourceLimitTests
{
    private static string PayloadWithIgnoredArrays(int depth) =>
        "{\"version\":\"0.1\",\"schema\":\""
        + new KeyedServerHolder.ChangePayload().Schema
        + "\",\"change\":{\"kind\":\"update\",\"members\":{}},\"ignored\":"
        + new string('[', depth)
        + "0"
        + new string(']', depth)
        + "}";

    [Test]
    [Arguments(0)]
    [Arguments(8)]
    [Arguments(128)]
    public void GeneratedPayloadReaderEnforcesConfiguredJsonDepth(int maximumDepth)
    {
        var options = new JsonSerializerOptions { MaxDepth = maximumDepth, TypeInfoResolver = KeyedServerHolder.PayloadJson.Resolver };
        JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(PayloadWithIgnoredArrays(2), options).ShouldNotBeNull();
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(PayloadWithIgnoredArrays(200_000), options));
    }

    [Test]
    public void GeneratedPayloadReaderHonorsLargerExplicitDepth()
    {
        var json = PayloadWithIgnoredArrays(80);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, KeyedServerHolder.PayloadJson.Default));
        var options = new JsonSerializerOptions { MaxDepth = 128, TypeInfoResolver = KeyedServerHolder.PayloadJson.Resolver };
        JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(json, options).ShouldNotBeNull();
    }

    [Test]
    public void DirectConverterDispatchStillEnforcesReaderDefaultDepth()
    {
        ReadDirectly(PayloadWithIgnoredArrays(2)).ShouldNotBeNull();
        Should.Throw<JsonException>(() => ReadDirectly(PayloadWithIgnoredArrays(200_000)));
    }

    private static KeyedServerHolder.ChangePayload? ReadDirectly(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read().ShouldBeTrue();
        var options = KeyedServerHolder.PayloadJson.Default.Options;
        var converter = (JsonConverter<KeyedServerHolder.ChangePayload>)options.GetConverter(typeof(KeyedServerHolder.ChangePayload));
        return converter.Read(ref reader, typeof(KeyedServerHolder.ChangePayload), options);
    }
}
