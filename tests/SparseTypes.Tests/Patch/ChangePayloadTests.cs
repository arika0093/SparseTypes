extern alias Playground;

using System.Reflection;
using System.Text.Json;
using Playground::SparseTypes.Playground.Models;

namespace SparseTypes.Tests.Patch;

[SparseModel]
public partial struct PayloadValueItem
{
    [SparseKey]
    public int Id { get; set; }

    public string Name { get; set; }
}

[SparseModel]
public partial class PayloadValueHolder
{
    public List<PayloadValueItem> Items { get; set; } = [];

    public Dictionary<string, PayloadValueItem> Values { get; set; } = new();
}

public sealed class ChangePayloadTests
{
    private static KeyedServer Server(string id, string name, int count = 0) =>
        new()
        {
            Id = id,
            Name = name,
            Count = count,
        };

    private static Optional<KeyedServerHolder.Fragment?> KeyedState(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() }));

    private static T RoundTrip<T>(T payload)
        where T : notnull => PayloadJsonTestSupport.RoundTrip(payload);

    [Test]
    public void ToPayload_UsesTypedMemberVariantAndRoundTripsThroughJson()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var changes = Settings.ChangeSet.Between(Optional<Settings.Fragment?>.Present(before), Optional<Settings.Fragment?>.Present(after));

        var payload = changes.ToPayload();
        var json = PayloadJsonTestSupport.Serialize(payload);
        var restored = PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(json);

        json.ShouldContain("\"state\":\"value\"");
        json.ShouldContain("\"version\":\"0.1\"");
        restored.Version.ShouldBe("0.1");
        var memberChange = restored.Change!.Members!.Label!;
        memberChange.GetType().Name.ShouldStartWith("Change");
        // Stage 3: typed DTOs live in the per-model implementation
        // namespace; the envelope facade stays nested in the model.
        typeof(Settings)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static type => type.Name.EndsWith("SerializeObjects", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var payloadContainer = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        payloadContainer.Name.ShouldEndWith("SerializeObjects");
        memberChange.GetType().DeclaringType.ShouldBe(payloadContainer);
        var endpointProperty = memberChange.GetType().GetProperty("After")!;
        var endpoint = endpointProperty.GetValue(memberChange)!;
        var endpointType = endpoint.GetType();
        endpointType.GetProperty("State")!.GetValue(endpoint)!.ToString().ShouldBe("Value");
        endpointType.GetProperty("Value")!.GetValue(endpoint).ShouldBe("after");
    }

    [Test]
    public void Payload_RoundTripsWholeRootMissingNullAndValueStates()
    {
        var states = new[]
        {
            Optional<Settings.Fragment?>.Missing,
            Optional<Settings.Fragment?>.Present(null),
            Optional<Settings.Fragment?>.Present(Settings.Fragment.From(new Settings { Label = "present", RetryCount = 3 })),
        };

        foreach (var before in states)
        {
            foreach (var after in states)
            {
                var changes = Settings.ChangeSet.Between(before, after);
                var restored = RoundTrip(changes.ToPayload()).ToChangeSet();
                Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
            }
        }
    }

    [Test]
    public void Payload_EncodesWholeRootTransitionsAsTypedSnapshots()
    {
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("present"), RetryCount = Optional<int>.Present(3) }
        );

        var payload = Settings.ChangeSet.Between(Optional<Settings.Fragment?>.Missing, after).ToPayload();
        var json = PayloadJsonTestSupport.Serialize(payload);

        payload.Change!.Kind.ToString().ShouldBe("Replace");
        json.ShouldContain("\"change\":{\"kind\":\"replace\"");
        json.ShouldContain("\"label\"");
        json.ShouldContain("\"state\":\"missing\"");
        json.ShouldNotContain("Member1");
        Settings.Patch.Between(payload.ToChangeSet().ToPatch().Apply(Optional<Settings.Fragment?>.Missing), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_UsesMemberArraysForNestedUnsetAndSetNull()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Nested = Optional<Nested.Fragment?>.Present(new Nested.Fragment { Host = Optional<string>.Present("old") }) }
        );
        var afterStates = new[] { Optional<Nested.Fragment?>.Missing, Optional<Nested.Fragment?>.Present(null) };

        foreach (var nestedAfter in afterStates)
        {
            var after = Optional<Settings.Fragment?>.Present(new Settings.Fragment { Nested = nestedAfter });
            var payload = Settings.ChangeSet.Between(before, after).ToPayload();
            var json = PayloadJsonTestSupport.Serialize(payload);
            var restored = RoundTrip(payload).ToChangeSet();

            json.ShouldContain("\"change\"");
            json.ShouldContain("\"kind\":\"replace\"");
            json.ShouldNotContain("Member1");
            Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void Payload_RoundTripsNestedMemberChanges()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Nested = Optional<Nested.Fragment?>.Present(new Nested.Fragment { Host = Optional<string>.Present("old") }) }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Nested = Optional<Nested.Fragment?>.Present(new Nested.Fragment { Host = Optional<string>.Present("new") }) }
        );

        var payload = Settings.ChangeSet.Between(before, after).ToPayload();
        var json = PayloadJsonTestSupport.Serialize(payload);
        using (var document = JsonDocument.Parse(json))
        {
            document.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["version", "change"]);
            var nested = document.RootElement.GetProperty("change").GetProperty("members").GetProperty("nested");
            nested.GetProperty("kind").GetString().ShouldBe("update");
            nested.GetProperty("change").GetProperty("kind").GetString().ShouldBe("update");
        }
        var restored = RoundTrip(payload).ToChangeSet();
        Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_RoundTripsScalarMissingNullAndValueEndpoints()
    {
        var states = new[] { Optional<string?>.Missing, Optional<string?>.Present(null), Optional<string?>.Present("value") };

        foreach (var beforeLabel in states)
        {
            foreach (var afterLabel in states)
            {
                var before = Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = beforeLabel });
                var after = Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = afterLabel });
                var restored = RoundTrip(Settings.ChangeSet.Between(before, after).ToPayload()).ToChangeSet();

                Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
            }
        }
    }

    [Test]
    public void Payload_RoundTripsSparseKeyedAddRemoveEditAndReorder()
    {
        var before = KeyedState(Server("a", "A"), Server("b", "B"));
        var after = KeyedState(Server("b", "B2", 2), Server("c", "C"));
        var payload = KeyedServerHolder.ChangeSet.Between(before, after).ToPayload();
        var json = PayloadJsonTestSupport.Serialize(payload);

        json.ShouldContain("\"kind\":\"update\"");
        json.ShouldNotContain("\"before\":null");
        json.ShouldNotContain("\"after\":null");
        json.ShouldNotContain("\"edit\":null");
        json.ShouldNotContain("\"isReordered\":false");
        using (var document = JsonDocument.Parse(json))
        {
            var itemChanges = document.RootElement.GetProperty("change").GetProperty("members").GetProperty("items");
            var items = itemChanges.GetProperty("items").EnumerateArray().ToArray();
            var edited = items.Single(item => item.GetProperty("key").GetString() == "b");
            edited.TryGetProperty("before", out _).ShouldBeFalse();
            edited.TryGetProperty("after", out _).ShouldBeFalse();
            edited.GetProperty("change").GetProperty("members").TryGetProperty("id", out _).ShouldBeFalse();
            items.Single(item => item.GetProperty("key").GetString() == "c").TryGetProperty("before", out _).ShouldBeFalse();
            items.Single(item => item.GetProperty("key").GetString() == "a").TryGetProperty("after", out _).ShouldBeFalse();
        }
        var restored = RoundTrip(payload).ToChangeSet();

        KeyedServerHolder.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        restored.Items.GetChange("b").IsEdited.ShouldBeTrue();
        restored.Items.GetChange("c").IsAdded.ShouldBeTrue();
        restored.Items.GetChange("a").IsRemoved.ShouldBeTrue();

        var orderBefore = KeyedState(Server("a", "A"), Server("b", "B"), Server("c", "C"));
        var orderAfter = KeyedState(Server("b", "B"), Server("a", "A"), Server("c", "C"));
        var orderPayload = KeyedServerHolder.ChangeSet.Between(orderBefore, orderAfter).ToPayload();
        var orderJson = PayloadJsonTestSupport.Serialize(orderPayload);
        orderJson.ShouldContain("\"kind\":\"update\"");
        orderJson.ShouldContain("\"isReordered\":true");
        orderJson.ShouldNotContain("\"before\":null");
        orderJson.ShouldNotContain("\"after\":null");
        var orderRestored = RoundTrip(orderPayload).ToChangeSet();
        KeyedServerHolder.Patch.Between(orderRestored.ToPatch().Apply(orderBefore), orderAfter).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_RestoresModelItemReplacementAsExistingEditSemantics()
    {
        var before = KeyedState(Server("a", "old", 1));
        var after = KeyedState(Server("a", "new", 2));
        var payload = PayloadJsonTestSupport.Deserialize<KeyedServerHolder.ChangePayload>(
            """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"replace","beforeIndex":0,"afterIndex":0,"before":{"state":"value","value":{"Id":"a","Name":"old","Count":1}},"after":{"state":"value","value":{"Id":"a","Name":"new","Count":2}}}],"beforeOrder":["a"],"afterOrder":["a"]}}}}"""
        )!;

        payload.Validate().IsValid.ShouldBeTrue();
        var restored = RoundTrip(payload);
        var json = PayloadJsonTestSupport.Serialize(restored);
        json.ShouldContain("\"kind\":\"replace\"");

        var changes = restored.ToChangeSet();
        changes.Items.GetChange("a").IsEdited.ShouldBeTrue();
        KeyedServerHolder.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        KeyedServerHolder.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_RoundTripsValueTypeKeyedAndDictionaryItems()
    {
        Optional<PayloadValueHolder.Fragment?> State(List<PayloadValueItem> items, Dictionary<string, PayloadValueItem> values) =>
            Optional<PayloadValueHolder.Fragment?>.Present(
                PayloadValueHolder.Fragment.From(new PayloadValueHolder { Items = items, Values = values })
            );

        var before = State(
            [new PayloadValueItem { Id = 1, Name = "before" }],
            new Dictionary<string, PayloadValueItem>
            {
                ["a"] = new() { Id = 2, Name = "before" },
            }
        );
        var after = State(
            [new PayloadValueItem { Id = 1, Name = "after" }, new PayloadValueItem { Id = 3, Name = "added" }],
            new Dictionary<string, PayloadValueItem>
            {
                ["a"] = new() { Id = 2, Name = "after" },
            }
        );

        var restored = RoundTrip(PayloadValueHolder.ChangeSet.Between(before, after).ToPayload()).ToChangeSet();

        restored.Items.GetChange(1).IsEdited.ShouldBeTrue();
        restored.Items.GetChange(3).IsAdded.ShouldBeTrue();
        restored.Values.GetChange("a").IsEdited.ShouldBeTrue();
        PayloadValueHolder.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_RoundTripsSparseScalarAndStructuralDictionaryChanges()
    {
        var scalarBefore = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(
                new ScalarDictHolder
                {
                    Scores = new() { ["a"] = 1, ["b"] = 2 },
                }
            )
        );
        var scalarAfter = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(
                new ScalarDictHolder
                {
                    Scores = new() { ["b"] = 3, ["c"] = 4 },
                }
            )
        );
        var scalarChanges = ScalarDictHolder.ChangeSet.Between(scalarBefore, scalarAfter);
        var scalarPayload = scalarChanges.ToPayload();
        var scalarJson = PayloadJsonTestSupport.Serialize(scalarPayload);
        using (var document = JsonDocument.Parse(scalarJson))
        {
            var items = document
                .RootElement.GetProperty("change")
                .GetProperty("members")
                .GetProperty("scores")
                .GetProperty("items")
                .EnumerateArray()
                .ToArray();
            items.Single(static item => item.GetProperty("key").GetString() == "b").GetProperty("kind").GetString().ShouldBe("replace");
            items.Single(static item => item.GetProperty("key").GetString() == "a").GetProperty("kind").GetString().ShouldBe("remove");
            items.Single(static item => item.GetProperty("key").GetString() == "c").GetProperty("kind").GetString().ShouldBe("add");
        }
        var scalarRestored = RoundTrip(scalarPayload).ToChangeSet();
        ScalarDictHolder.Patch.Between(scalarRestored.ToPatch().Apply(scalarBefore), scalarAfter).IsEmpty.ShouldBeTrue();

        var structuralBefore = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "old") } })
        );
        var structuralAfter = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new StructuralDictHolder
                {
                    Servers = new() { ["a"] = Server("a", "new"), ["b"] = Server("b", "added") },
                }
            )
        );
        var structuralChanges = StructuralDictHolder.ChangeSet.Between(structuralBefore, structuralAfter);
        var structuralPayload = structuralChanges.ToPayload();
        var structuralJson = PayloadJsonTestSupport.Serialize(structuralPayload);
        using (var document = JsonDocument.Parse(structuralJson))
        {
            var itemChanges = document.RootElement.GetProperty("change").GetProperty("members").GetProperty("servers");
            var edited = itemChanges.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "a");
            edited.GetProperty("kind").GetString().ShouldBe("update");
            edited.TryGetProperty("before", out _).ShouldBeFalse();
            edited.TryGetProperty("after", out _).ShouldBeFalse();
            edited.GetProperty("change").GetProperty("members").TryGetProperty("name", out _).ShouldBeTrue();
        }
        var structuralRestored = RoundTrip(structuralPayload).ToChangeSet();
        StructuralDictHolder.Patch.Between(structuralRestored.ToPatch().Apply(structuralBefore), structuralAfter).IsEmpty.ShouldBeTrue();

        var replacementBefore = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "old") } })
        );
        var replacementAfter = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "new", 2) } })
        );
        var replacement = PayloadJsonTestSupport.Deserialize<StructuralDictHolder.ChangePayload>(
            """{"version":"0.1","change":{"kind":"update","members":{"servers":{"kind":"update","items":[{"key":"a","kind":"replace","before":{"state":"value","value":{"Id":"a","Name":"old","Count":0}},"after":{"state":"value","value":{"Id":"a","Name":"new","Count":2}}}]}}}}"""
        )!;
        replacement.Validate().IsValid.ShouldBeTrue();
        var replacementChanges = RoundTrip(replacement).ToChangeSet();
        StructuralDictHolder.Patch.Between(replacementChanges.ToPatch().Apply(replacementBefore), replacementAfter).IsEmpty.ShouldBeTrue();
        StructuralDictHolder.Patch.Between(replacement.ToPatch().Apply(replacementBefore), replacementAfter).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void SparsePayloadTransitionsComposeAcrossKeyedAndDictionaryValues()
    {
        var keyedInitial = KeyedState();
        var keyedAdded = KeyedState(Server("a", "A"));
        var keyedEdited = KeyedState(Server("a", "B", 1));
        var keyedAdd = RoundTrip(KeyedServerHolder.ChangeSet.Between(keyedInitial, keyedAdded).ToPayload()).ToChangeSet();
        var keyedEdit = RoundTrip(KeyedServerHolder.ChangeSet.Between(keyedAdded, keyedEdited).ToPayload()).ToChangeSet();
        var keyedAddThenEdit = keyedAdd.Compose(keyedEdit);
        keyedAddThenEdit.Items.GetChange("a").IsAdded.ShouldBeTrue();
        KeyedServerHolder.Patch.Between(keyedAddThenEdit.ToPatch().Apply(keyedInitial), keyedEdited).IsEmpty.ShouldBeTrue();

        var keyedRemove = RoundTrip(KeyedServerHolder.ChangeSet.Between(keyedEdited, KeyedState()).ToPayload()).ToChangeSet();
        var keyedEditThenRemove = keyedEdit.Compose(keyedRemove);
        keyedEditThenRemove.Items.GetChange("a").IsRemoved.ShouldBeTrue();
        KeyedServerHolder.Patch.Between(keyedEditThenRemove.ToPatch().Apply(keyedAdded), KeyedState()).IsEmpty.ShouldBeTrue();

        var dictInitial = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() })
        );
        var dictAdded = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "A") } })
        );
        var dictEdited = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "B", 1) } })
        );
        var dictAdd = RoundTrip(StructuralDictHolder.ChangeSet.Between(dictInitial, dictAdded).ToPayload()).ToChangeSet();
        var dictEdit = RoundTrip(StructuralDictHolder.ChangeSet.Between(dictAdded, dictEdited).ToPayload()).ToChangeSet();
        var dictAddThenEdit = dictAdd.Compose(dictEdit);
        dictAddThenEdit.Servers.GetChange("a").IsAdded.ShouldBeTrue();
        StructuralDictHolder.Patch.Between(dictAddThenEdit.ToPatch().Apply(dictInitial), dictEdited).IsEmpty.ShouldBeTrue();

        var dictRemove = RoundTrip(StructuralDictHolder.ChangeSet.Between(dictEdited, dictInitial).ToPayload()).ToChangeSet();
        var dictEditThenRemove = dictEdit.Compose(dictRemove);
        dictEditThenRemove.Servers.GetChange("a").IsRemoved.ShouldBeTrue();
        StructuralDictHolder.Patch.Between(dictEditThenRemove.ToPatch().Apply(dictAdded), dictInitial).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_RejectsUnsupportedVersionsAndInvalidReplacementShape()
    {
        var payload = new Settings.ChangePayload { Version = "0.2" };
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());

        var future = new Settings.ChangePayload { Version = "1.0" };
        Should.Throw<ArgumentException>(() => future.ToChangeSet());

        var source = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = Optional<string?>.Present("before") }),
            Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = Optional<string?>.Present("after") })
        );
        var invalidReplacement = Settings
            .ChangeSet.Between(Optional<Settings.Fragment?>.Missing, Optional<Settings.Fragment?>.Present(new Settings.Fragment()))
            .ToPayload();
        invalidReplacement.Change!.After = null;
        Should.Throw<ArgumentException>(() => invalidReplacement.ToChangeSet());
    }

    [Test]
    [Arguments("0.1", true)]
    [Arguments(null, false)]
    [Arguments("0.2", false)]
    [Arguments("1.0", false)]
    [Arguments("999", false)]
    public void PayloadVersionGateCoversEveryPublicEntryPoint(string? version, bool valid)
    {
        var envelope = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = Optional<string?>.Present("before") }),
            Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = Optional<string?>.Present("after") })
        );
        var payload = envelope.ToPayload();
        payload.Version = version;

        if (valid)
        {
            payload.ToChangeSet().IsEmpty.ShouldBeFalse();
            payload.ToPatch().IsEmpty.ShouldBeFalse();
            payload.InvertReversibleChanges(out var skipped).IsEmpty.ShouldBeFalse();
            skipped.ShouldBeEmpty();
            return;
        }

        // Every interpretation path rejects the same version value before
        // touching any model state.
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        Should.Throw<ArgumentException>(() => payload.ToPatch());
        Should.Throw<ArgumentException>(() => payload.InvertReversibleChanges(out _));
        var current = new Settings { Label = "kept", RetryCount = 1 };
        Should.Throw<ArgumentException>(() => payload.TryApplyMixedTo(current, out _, out _));
        current.Label.ShouldBe("kept");
        current.RetryCount.ShouldBe(1);
    }

    [Test]
    public void Payload_WritesStableMinimalEnvelope()
    {
        var empty = Settings.ChangeSet.Between(Optional<Settings.Fragment?>.Missing, Optional<Settings.Fragment?>.Missing).ToPayload();
        var json = PayloadJsonTestSupport.Serialize(empty);
        json.ShouldBe("""{"version":"0.1","change":{"kind":"update","members":{}}}""");
        var restored = PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(json);
        restored.Version.ShouldBe("0.1");
        restored.Change!.Members!.Label.ShouldBeNull();
        restored.Change.Members.RetryCount.ShouldBeNull();
    }

    [Test]
    [Arguments("""{"change":{"kind":"update","members":{}}}""")]
    [Arguments("""{"version":null,"change":{"kind":"update","members":{}}}""")]
    [Arguments("""{"version":1,"change":{"kind":"update","members":{}}}""")]
    [Arguments("""{"version":"0.2","change":{"kind":"update","members":{}}}""")]
    [Arguments("""{"version":"1.0","change":{"kind":"update","members":{}}}""")]
    [Arguments("""{"version":"0.1"}""")]
    [Arguments("""{"version":"0.1","change":null}""")]
    [Arguments("""{"version":"0.1","change":{}}""")]
    [Arguments("""{"versoin":"0.1","change":{"kind":"update","members":{}}}""")]
    public void Payload_RejectsMalformedEnvelopes(string json)
    {
        Settings.ChangePayload? payload = null;
        var deserialized = false;
        try
        {
            payload = PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(json);
            deserialized = true;
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        // Missing required envelope fields may fail during decoding; unsupported
        // versions decode and fail before producing a ChangeSet.
        if (deserialized)
        {
            Should.Throw<ArgumentException>(() => payload!.ToChangeSet());
        }
    }

    [Test]
    [Arguments("""{"version":"0.1","version":"0.1","change":{"kind":"update","members":{}}}""")]
    [Arguments("""{"version":"0.1","change":{"kind":"update","members":{}},"change":{"kind":"update","members":{}}}""")]
    public void Payload_RejectsDuplicateEnvelopeProperties(string json)
    {
        Should.Throw<System.Text.Json.JsonException>(() => PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(json));
    }

    [Test]
    [Arguments("""{"version":"0.1","change":{"kind":"bogus","members":{}}}""")]
    [Arguments("""{"version":"0.1","change":{"kind":"update","members":[]}}""")]
    [Arguments("""{"version":"0.1","change":{"kind":"update","members":{"label":{}}}}""")]
    public void Payload_RejectsMalformedMemberVariants(string json)
    {
        Settings.ChangePayload? payload;
        try
        {
            payload = PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        if (payload is null)
        {
            return;
        }

        Should.Throw<ArgumentException>(() => payload.ToChangeSet());
    }

    [Test]
    public void Payload_RejectsMalformedNestedPayload()
    {
        var nested = """{"version":"0.1","change":{"kind":"update","members":{"nested":{"kind":"update","change":{"kind":"bogus","members":{}}}}}}""";
        Settings.ChangePayload? payload;
        try
        {
            payload = PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(nested);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        Should.Throw<ArgumentException>(() => payload!.ToChangeSet());
    }

    [Test]
    public void Payload_RejectsDuplicateKeyedChanges()
    {
        var before = KeyedState(Server("a", "A"));
        var after = KeyedState(Server("a", "B", 1));
        var payload = KeyedServerHolder.ChangeSet.Between(before, after).ToPayload();
        var candidate =
            """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"add","afterIndex":0,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}}},{"key":"a","kind":"add","afterIndex":1,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}}}]}}}}""";
        KeyedServerHolder.ChangePayload? restored;
        try
        {
            restored = PayloadJsonTestSupport.Deserialize<KeyedServerHolder.ChangePayload>(candidate);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        Should.Throw<ArgumentException>(() => restored!.ToChangeSet());
    }

    [Test]
    [Arguments(
        """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"add","afterIndex":0}]}}}}"""
    )]
    [Arguments(
        """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"remove","beforeIndex":0}]}}}}"""
    )]
    [Arguments(
        """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"bogus","beforeIndex":0,"afterIndex":0}]}}}}"""
    )]
    public void Payload_RejectsInvalidKeyedItems(string json)
    {
        try
        {
            var payload = PayloadJsonTestSupport.Deserialize<KeyedServerHolder.ChangePayload>(json);
            if (payload is null)
            {
                return;
            }

            Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        }
        catch (System.Text.Json.JsonException)
        {
            // Unknown enum tokens fail at deserialization; both rejections are
            // valid.
        }
    }

    [Test]
    public void Endpoint_RejectsNullValueStateWithoutValue()
    {
        var endpoint = new ChangePayloadEndpoint<string> { State = ChangePayloadState.Value };
        Should.Throw<InvalidOperationException>(() => endpoint.ToOptional());
    }

    [Test]
    public void Endpoint_RejectsInconsistentStateValueCombinations()
    {
        var missingWithValue = new ChangePayloadEndpoint<string> { State = ChangePayloadState.Missing, Value = "oops" };
        Should.Throw<InvalidOperationException>(() => missingWithValue.ToOptional());

        var nullWithValue = new ChangePayloadEndpoint<string> { State = ChangePayloadState.Null, Value = "oops" };
        Should.Throw<InvalidOperationException>(() => nullWithValue.ToOptional());
    }
}
