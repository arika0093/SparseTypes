using System.Buffers;
using System.Text.Json;
using SparseTypes;

namespace SparseTypes.Tests;

[SparseModel]
public partial class NamingWidget
{
    [System.Text.Json.Serialization.JsonPropertyName("customName")]
    public string? Value { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("a/b")]
    public int Slash { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Plain { get; set; }
}

[SparseModel]
public partial class TransportPolicyWidget
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int JsonExcluded { get; set; }

    [SparseTransportIgnore]
    public int TransportExcluded { get; set; }

    public int Visible { get; set; }
}

[SparseModel]
public partial class TransportPolicyChild
{
    [SparseTransportIgnore]
    public int LocalOnly { get; set; }

    public int Visible { get; set; }
}

[SparseModel]
public partial class TransportPolicyParent
{
    public TransportPolicyChild Child { get; set; } = new();
}

[SparseModel]
public partial class TransportPolicyKeyedItem
{
    [SparseKey]
    public int Id { get; set; }

    [SparseTransportIgnore]
    public int LocalOnly { get; set; }
}

[SparseModel]
public partial class TransportPolicyKeyedHolder
{
    public global::System.Collections.Generic.List<TransportPolicyKeyedItem> Items { get; set; } = new();
}

[SparseModel]
public partial class JsonExcludedChild
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Hidden { get; set; }

    public string? Visible { get; set; }
}

[SparseModel]
public partial class JsonExcludedParent
{
    public JsonExcludedChild Child { get; set; } = new();
}

[SparseModel]
public partial class JsonExcludedKeyedItem
{
    [SparseKey]
    public int Id { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Hidden { get; set; }
}

[SparseModel]
public partial class JsonExcludedKeyedHolder
{
    public global::System.Collections.Generic.List<JsonExcludedKeyedItem> Items { get; set; } = new();
}

public sealed class FragmentAndChangeSetJsonTests
{
    [Test]
    public void ChangeSetRoundTripsWithExplicitWireNames()
    {
        Optional<NamingWidget.Fragment?> State(NamingWidget model) => Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(model));
        var before = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 2,
            }
        );
        var after = State(
            new NamingWidget
            {
                Value = "b",
                Slash = 3,
                Plain = 2,
            }
        );

        var payload = NamingWidget.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload, NamingWidget.PayloadJson.Default);
        json.ShouldContain("\"customName\"");
        json.ShouldContain("\"a/b\"");
        json.ShouldNotContain("\"member\":\"Value\"");

        var restored = JsonSerializer.Deserialize(json, NamingWidget.PayloadJson.Default)!.ToChangeSet();
        NamingWidget.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void JsonIgnorePreservesNeutralPayloadButFailsJsonSerialization()
    {
        Optional<NamingWidget.Fragment?> State(NamingWidget model) => Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(model));
        var before = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 2,
            }
        );
        var after = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 3,
            }
        );

        var ignoredOnly = NamingWidget.ChangeSet.Between(before, after);
        ignoredOnly.IsEmpty.ShouldBeFalse();
        var payload = ignoredOnly.ToPayload();
        payload.Change!.Members!.Plain.ShouldNotBeNull();
        payload.ToChangeSet().IsEmpty.ShouldBeFalse();
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(payload, NamingWidget.PayloadJson.Default));
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            Should.Throw<JsonException>(() => JsonSerializer.Serialize(writer, payload, NamingWidget.PayloadJson.Default));
            output.WrittenCount.ShouldBe(0);
        }
        const string ignoredMemberInput =
            """{"version":"0.1","change":{"kind":"update","members":{"plain":{"kind":"replace","before":{"state":"value","value":2},"after":{"state":"value","value":3}}}}}""";
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<NamingWidget.ChangePayload>(ignoredMemberInput, NamingWidget.PayloadJson.Default)
        );
        const string ignoredSnapshotInput =
            """{"version":"0.1","change":{"kind":"replace","before":{"state":"missing"},"after":{"state":"value","value":{"plain":{"state":"value","value":3}}}}}""";
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<NamingWidget.ChangePayload>(ignoredSnapshotInput, NamingWidget.PayloadJson.Default)
        );

        var mixed = NamingWidget.ChangeSet.Between(
            before,
            State(
                new NamingWidget
                {
                    Value = "b",
                    Slash = 1,
                    Plain = 3,
                }
            )
        );
        var mixedPayload = mixed.ToPayload();
        mixedPayload.Change!.Members!.Value.ShouldNotBeNull();
        mixedPayload.Change.Members.Plain.ShouldNotBeNull();
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(mixed.ToPayload(), NamingWidget.PayloadJson.Default));

        var patchPayload = NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Plain = 3 });
        patchPayload.Change!.Members!.Plain.ShouldNotBeNull();
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(patchPayload, NamingWidget.PayloadJson.Default));
        NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Value = "b" }).Change!.Members!.Value.ShouldNotBeNull();
    }

    [Test]
    public void JsonExcludedNestedAndKeyedChangesFailBeforeWritingBytes()
    {
        var nested = JsonExcludedParent
            .ChangeSet.Between(
                Optional<JsonExcludedParent.Fragment?>.Present(
                    JsonExcludedParent.Fragment.From(new JsonExcludedParent { Child = new JsonExcludedChild { Hidden = "before" } })
                ),
                Optional<JsonExcludedParent.Fragment?>.Present(
                    JsonExcludedParent.Fragment.From(new JsonExcludedParent { Child = new JsonExcludedChild { Hidden = "after" } })
                )
            )
            .ToPayload();
        var nestedOutput = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(nestedOutput))
        {
            Should.Throw<JsonException>(() => JsonSerializer.Serialize(writer, nested, JsonExcludedParent.PayloadJson.Default));
            nestedOutput.WrittenCount.ShouldBe(0);
        }

        var keyed = JsonExcludedKeyedHolder
            .ChangeSet.Between(
                Optional<JsonExcludedKeyedHolder.Fragment?>.Present(
                    JsonExcludedKeyedHolder.Fragment.From(
                        new JsonExcludedKeyedHolder { Items = [new JsonExcludedKeyedItem { Id = 1, Hidden = "before" }] }
                    )
                ),
                Optional<JsonExcludedKeyedHolder.Fragment?>.Present(
                    JsonExcludedKeyedHolder.Fragment.From(
                        new JsonExcludedKeyedHolder { Items = [new JsonExcludedKeyedItem { Id = 1, Hidden = "after" }] }
                    )
                )
            )
            .ToPayload();
        var keyedOutput = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(keyedOutput))
        {
            Should.Throw<JsonException>(() => JsonSerializer.Serialize(writer, keyed, JsonExcludedKeyedHolder.PayloadJson.Default));
            keyedOutput.WrittenCount.ShouldBe(0);
        }

        var whole = JsonExcludedChild
            .ChangeSet.Between(
                Optional<JsonExcludedChild.Fragment?>.Present(JsonExcludedChild.Fragment.From(new JsonExcludedChild { Hidden = "before" })),
                Optional<JsonExcludedChild.Fragment?>.Missing
            )
            .ToPayload();
        var wholeOutput = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(wholeOutput))
        {
            Should.Throw<JsonException>(() => JsonSerializer.Serialize(writer, whole, JsonExcludedChild.PayloadJson.Default));
            wholeOutput.WrittenCount.ShouldBe(0);
        }
    }

    [Test]
    public void SparseTransportIgnoreRejectsChangeSetAndPatchExport()
    {
        var before = TransportPolicyWidget.Fragment.From(new TransportPolicyWidget { TransportExcluded = 1 });
        var after = TransportPolicyWidget.Fragment.From(new TransportPolicyWidget { TransportExcluded = 2 });
        var changes = TransportPolicyWidget.ChangeSet.Between(
            Optional<TransportPolicyWidget.Fragment?>.Present(before),
            Optional<TransportPolicyWidget.Fragment?>.Present(after)
        );

        var changeSetError = Should.Throw<InvalidOperationException>(() => changes.ToPayload());
        changeSetError.Message.ShouldContain("SparseTransportIgnore");
        changeSetError.Message.ShouldContain("TransportExcluded");

        var patchError = Should.Throw<InvalidOperationException>(() =>
            TransportPolicyWidget.ChangePayload.FromPatch(new TransportPolicyWidget.Patch { TransportExcluded = 2 })
        );
        patchError.Message.ShouldContain("SparseTransportIgnore");
        patchError.Message.ShouldContain("TransportExcluded");

        var visiblePayload = TransportPolicyWidget.ChangePayload.FromPatch(new TransportPolicyWidget.Patch { Visible = 1 });
        visiblePayload.Change!.Members!.Visible.ShouldNotBeNull();
        const string transportExcludedInput =
            """{"version":"0.1","change":{"kind":"update","members":{"transportExcluded":{"kind":"replace","before":{"state":"value","value":1},"after":{"state":"value","value":2}}}}}""";
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<TransportPolicyWidget.ChangePayload>(transportExcludedInput, TransportPolicyWidget.PayloadJson.Default)
        );

        var rootTransition = TransportPolicyWidget.ChangeSet.Between(
            Optional<TransportPolicyWidget.Fragment?>.Present(TransportPolicyWidget.Fragment.From(new TransportPolicyWidget())),
            Optional<TransportPolicyWidget.Fragment?>.Missing
        );
        Should.Throw<InvalidOperationException>(() => rootTransition.ToPayload()).Message.ShouldContain("$root");

        var nestedChanges = TransportPolicyParent.ChangeSet.Between(
            Optional<TransportPolicyParent.Fragment?>.Present(
                TransportPolicyParent.Fragment.From(new TransportPolicyParent { Child = new TransportPolicyChild { LocalOnly = 1 } })
            ),
            Optional<TransportPolicyParent.Fragment?>.Present(
                TransportPolicyParent.Fragment.From(new TransportPolicyParent { Child = new TransportPolicyChild { LocalOnly = 2 } })
            )
        );
        Should.Throw<InvalidOperationException>(() => nestedChanges.ToPayload()).Message.ShouldContain("SparseTransportIgnore");

        var collectionChanges = TransportPolicyKeyedHolder.ChangeSet.Between(
            Optional<TransportPolicyKeyedHolder.Fragment?>.Present(
                TransportPolicyKeyedHolder.Fragment.From(
                    new TransportPolicyKeyedHolder { Items = [new TransportPolicyKeyedItem { Id = 1, LocalOnly = 1 }] }
                )
            ),
            Optional<TransportPolicyKeyedHolder.Fragment?>.Present(
                TransportPolicyKeyedHolder.Fragment.From(
                    new TransportPolicyKeyedHolder { Items = [new TransportPolicyKeyedItem { Id = 1, LocalOnly = 2 }] }
                )
            )
        );
        Should.Throw<InvalidOperationException>(() => collectionChanges.ToPayload()).Message.ShouldContain("SparseTransportIgnore");
    }
}
