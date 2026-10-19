using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SparseTypes.Tests;

[SparseModel]
public partial class JsonHelperScalar
{
    public string? Label { get; set; }

    public int Count { get; set; }
}

[JsonSerializable(typeof(KeyedServer))]
internal partial class KeyedServerLeafContext : JsonSerializerContext { }

/// <summary>Certifies the top-level ModelJson entry point at runtime.</summary>
public sealed class PayloadJsonHelperRoundTripTests
{
    private static Optional<JsonHelperScalar.Fragment?> ScalarBefore() =>
        Optional<JsonHelperScalar.Fragment?>.Present(
            JsonHelperScalar.Fragment.From(new JsonHelperScalar { Label = "before", Count = 1 })
        );

    private static Optional<JsonHelperScalar.Fragment?> ScalarAfter() =>
        Optional<JsonHelperScalar.Fragment?>.Present(
            JsonHelperScalar.Fragment.From(new JsonHelperScalar { Label = "after", Count = 1 })
        );

    private static Optional<KeyedServerHolder.Fragment?> KeyedBefore() =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(
                new KeyedServerHolder
                {
                    Items =
                    [
                        new KeyedServer
                        {
                            Id = "a",
                            Name = "A",
                            Count = 1,
                        },
                    ],
                }
            )
        );

    private static Optional<KeyedServerHolder.Fragment?> KeyedAfter() =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(
                new KeyedServerHolder
                {
                    Items =
                    [
                        new KeyedServer
                        {
                            Id = "a",
                            Name = "A2",
                            Count = 1,
                        },
                        new KeyedServer
                        {
                            Id = "b",
                            Name = "B",
                            Count = 2,
                        },
                    ],
                }
            )
        );

    [Test]
    public void DefaultRoundTripsScalarTransition()
    {
        var before = ScalarBefore();
        var after = ScalarAfter();
        var changes = JsonHelperScalar.ChangeSet.Between(before, after);

        var json = JsonSerializer.Serialize(changes.ToPayload(), JsonHelperScalarJson.Default);
        var restored = JsonSerializer
            .Deserialize(json, JsonHelperScalarJson.Default)!
            .ToChangeSet();

        JsonHelperScalar
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DefaultExposesJsonTypeInfoAndResolver()
    {
        JsonHelperScalarJson.Default.ShouldBeOfType<JsonTypeInfo<JsonHelperScalar.ChangePayload>>();
        JsonHelperScalarJson.Resolver.ShouldBeAssignableTo<IJsonTypeInfoResolver>();
        JsonHelperScalarJson.Resolver.ShouldBe(JsonHelperScalar.PayloadJson.Default);
    }

    [Test]
    public void ResolverComposesWithCallerProvidedOptions()
    {
        var before = ScalarBefore();
        var after = ScalarAfter();
        var changes = JsonHelperScalar.ChangeSet.Between(before, after);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(JsonHelperScalarJson.Resolver),
            WriteIndented = true,
        };
        var info =
            (JsonTypeInfo<JsonHelperScalar.ChangePayload>)
                options.GetTypeInfo(typeof(JsonHelperScalar.ChangePayload));

        var json = JsonSerializer.Serialize(changes.ToPayload(), info);
        var restored = JsonSerializer.Deserialize(json, info)!.ToChangeSet();

        JsonHelperScalar
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ResolverComposesWithLeafContextForCustomValues()
    {
        // Keyed additions carry the element POCO itself, so custom value
        // shapes resolve through a source-generated context combined with
        // the generated resolver.
        var before = KeyedBefore();
        var after = KeyedAfter();
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                KeyedServerHolderJson.Resolver,
                KeyedServerLeafContext.Default
            ),
        };
        var info =
            (JsonTypeInfo<KeyedServerHolder.ChangePayload>)
                options.GetTypeInfo(typeof(KeyedServerHolder.ChangePayload));

        var json = JsonSerializer.Serialize(changes.ToPayload(), info);
        var restored = JsonSerializer.Deserialize(json, info)!.ToChangeSet();

        KeyedServerHolder
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void HelperIsDiscoverableWhilePlumbingStaysHidden()
    {
        typeof(JsonHelperScalarJson).GetCustomAttribute<EditorBrowsableAttribute>().ShouldBeNull();
        typeof(JsonHelperScalarJson)
            .GetProperty("Default")!
            .GetCustomAttribute<EditorBrowsableAttribute>()
            .ShouldBeNull();
        typeof(JsonHelperScalar.PayloadJson)
            .GetCustomAttribute<EditorBrowsableAttribute>()!
            .State.ShouldBe(EditorBrowsableState.Never);
        typeof(JsonHelperScalar.PayloadJsonConverter)
            .GetCustomAttribute<EditorBrowsableAttribute>()!
            .State.ShouldBe(EditorBrowsableState.Never);
    }
}
