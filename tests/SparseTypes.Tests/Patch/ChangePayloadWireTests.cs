using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SparseTypes.Tests.Patch;

/// <summary>Certifies the optional-JSON split.</summary>
/// <remarks>The base runtime carries no System.Text.Json reference; the generated resolver owns the payload wire shape.</remarks>
public sealed class ChangePayloadWireTests
{
    [Test]
    public void RuntimeCarriesNoSystemTextJsonReference()
    {
        typeof(ChangePayloadEndpoint<>).Assembly.GetReferencedAssemblies().Select(static name => name.Name).ShouldNotContain("System.Text.Json");
    }

    [Test]
    public void EndpointAndEnumsCarryNoSerializerAttributes()
    {
        foreach (
            var attribute in typeof(ChangePayloadEndpoint<>)
                .GetCustomAttributes()
                .Concat(typeof(ChangePayloadEndpoint<string>).GetCustomAttributes())
                .Concat(typeof(ChangePayloadState).GetCustomAttributes())
                .Concat(typeof(ChangePayloadItemKind).GetCustomAttributes())
        )
        {
            (attribute.GetType().FullName ?? string.Empty).ShouldNotStartWith("System.Text.Json", Case.Sensitive);
        }
    }

    [Test]
    public void WireTokensMatchVersion1()
    {
        ChangePayloadWire.Version.ShouldBe("0.1");
        ChangePayloadWire.VersionProperty.ShouldBe("version");
        ChangePayloadWire.SchemaProperty.ShouldBe("schema");
        ChangePayloadWire.RecordedAtProperty.ShouldBe("recordedAt");
        ChangePayloadWire.ChangeProperty.ShouldBe("change");
        ChangePayloadWire.KindProperty.ShouldBe("kind");
        ChangePayloadWire.MembersProperty.ShouldBe("members");
        ChangePayloadWire.ItemsProperty.ShouldBe("items");
        ChangePayloadWire.BeforeProperty.ShouldBe("before");
        ChangePayloadWire.AfterProperty.ShouldBe("after");
        ChangePayloadWire.KeyProperty.ShouldBe("key");
        ChangePayloadWire.BeforeIndexProperty.ShouldBe("beforeIndex");
        ChangePayloadWire.AfterIndexProperty.ShouldBe("afterIndex");
        ChangePayloadWire.BeforeOrderProperty.ShouldBe("beforeOrder");
        ChangePayloadWire.AfterOrderProperty.ShouldBe("afterOrder");
        ChangePayloadWire.IsReorderedProperty.ShouldBe("isReordered");
        ChangePayloadWire.TemporaryKeyProperty.ShouldBe("temporaryKey");
        ChangePayloadWire.ToToken(ChangePayloadState.Missing).ShouldBe("missing");
        ChangePayloadWire.ToToken(ChangePayloadState.Null).ShouldBe("null");
        ChangePayloadWire.ToToken(ChangePayloadState.Value).ShouldBe("value");
        ChangePayloadWire.ToToken(ChangePayloadState.Redacted).ShouldBe("redacted");
        ChangePayloadWire.UpdateKind.ShouldBe("update");
        ChangePayloadWire.ReplaceKind.ShouldBe("replace");
        ChangePayloadWire.AddKind.ShouldBe("add");
        ChangePayloadWire.RemoveKind.ShouldBe("remove");

        foreach (var state in Enum.GetValues<ChangePayloadState>())
        {
            ChangePayloadWire.TryParseState(ChangePayloadWire.ToToken(state), out var parsedState).ShouldBeTrue();
            parsedState.ShouldBe(state);
        }
        ChangePayloadWire.TryParseState("nope", out _).ShouldBeFalse();
    }

    [Test]
    public void GeneratedResolverWritesStateBeforeValue()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var payload = Settings
            .ChangeSet.Between(Optional<Settings.Fragment?>.Present(before), Optional<Settings.Fragment?>.Present(after))
            .ToPayload();
        var json = JsonSerializer.Serialize(payload, Settings.PayloadJson.Default);

        // v1 order: state(0) precedes value(1) inside every endpoint.
        var state = json.IndexOf("\"state\":\"value\"", StringComparison.Ordinal);
        var value = json.IndexOf("\"value\":\"after\"", StringComparison.Ordinal);
        state.ShouldBeGreaterThanOrEqualTo(0);
        value.ShouldBeGreaterThan(state);
    }

    [Test]
    public void GeneratedResolverWritesTypedChangeEnvelopeWithoutDiscriminators()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var payload = Settings
            .ChangeSet.Between(Optional<Settings.Fragment?>.Present(before), Optional<Settings.Fragment?>.Present(after))
            .ToPayload();

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, Settings.PayloadJson.Default));
        var envelope = document.RootElement;
        envelope.EnumerateObject().Select(static property => property.Name).ShouldBe(new[] { "version", "schema", "recordedAt", "change" });
        envelope.TryGetProperty("changes", out _).ShouldBeFalse();

        var change = envelope.GetProperty("change");
        change.GetProperty("kind").GetString().ShouldBe("update");
        var members = change.GetProperty("members");
        var label = members.GetProperty("label");
        label.GetProperty("kind").GetString().ShouldBe("replace");
        label.TryGetProperty("member", out _).ShouldBeFalse();
        label.GetProperty("after").GetProperty("value").GetString().ShouldBe("after");
    }

    [Test]
    public void SchemaIdentityIsStableRequiredAndDoesNotRejectAValidMismatch()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var payload = Settings
            .ChangeSet.Between(Optional<Settings.Fragment?>.Present(before), Optional<Settings.Fragment?>.Present(after))
            .ToPayload();
        var identity = payload.Schema!;

        identity.ShouldStartWith("sha256:");
        identity.Length.ShouldBe(71);
        new Settings.ChangePayload().Schema.ShouldBe(identity);
        payload.Validate().IsValid.ShouldBeTrue();

        payload.Schema = "sha256:" + new string('0', 64);
        payload.Validate().IsValid.ShouldBeTrue();
        payload.ToChangeSet().Label.After.Value.ShouldBe("after");

        payload.Schema = "sha256:invalid";
        var validation = payload.Validate();
        validation.IsValid.ShouldBeFalse();
        validation.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.SchemaIdentity);
        Should.Throw<ArgumentException>(() => payload.ToPatch());
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(payload, Settings.PayloadJson.Default));

        var validJson = JsonSerializer.Serialize(
            Settings.ChangeSet.Between(Optional<Settings.Fragment?>.Present(before), Optional<Settings.Fragment?>.Present(after)).ToPayload(),
            Settings.PayloadJson.Default
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.ChangePayload>(
                validJson.Replace("\"schema\":\"" + identity + "\",", string.Empty, StringComparison.Ordinal),
                Settings.PayloadJson.Default
            )
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.ChangePayload>(
                validJson.Replace("\"schema\":\"" + identity + "\"", "\"schema\":7", StringComparison.Ordinal),
                Settings.PayloadJson.Default
            )
        );
    }

    [Test]
    public void ChangePayloadKeepsItsTypedChangeAsTheCanonicalMutableShape()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var payload = Settings
            .ChangeSet.Between(Optional<Settings.Fragment?>.Present(before), Optional<Settings.Fragment?>.Present(after))
            .ToPayload();
        var change = payload.Change!;
        change.Members!.Label!.After = ChangePayloadEndpoint<string?>.FromOptional(Optional<string?>.Present("edited"));

        ReferenceEquals(payload.Change, change).ShouldBeTrue();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, Settings.PayloadJson.Default));
        document
            .RootElement.GetProperty("change")
            .GetProperty("members")
            .GetProperty("label")
            .GetProperty("after")
            .GetProperty("value")
            .GetString()
            .ShouldBe("edited");
        payload.ToChangeSet().Label.After.Value.ShouldBe("edited");
    }

    [Test]
    public void PayloadEnvelopeSelectsConverterWithoutAnnotatingEndpoints()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;

        // DTO reflection serialization is not the payload wire route.
        var endpointProperties = container
            .GetNestedTypes()
            .SelectMany(static type => type.GetProperties())
            .Where(static property =>
                property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(ChangePayloadEndpoint<>)
            )
            .ToArray();
        endpointProperties.ShouldNotBeEmpty();
        foreach (var property in endpointProperties)
        {
            property.GetCustomAttribute<JsonConverterAttribute>().ShouldBeNull();
        }
        var converterType = typeof(Settings.ChangePayload).GetCustomAttribute<JsonConverterAttribute>()!.ConverterType!;
        converterType.IsVisible.ShouldBeTrue();
        typeof(JsonConverter<Settings.ChangePayload>).IsAssignableFrom(converterType).ShouldBeTrue();
        converterType.GetConstructor(Type.EmptyTypes).ShouldNotBeNull();
        Settings.PayloadJson.Default.ShouldNotBeNull();
    }

    [Test]
    public void CollectionChangesAndItemOperationsUseDistinctTypedRoles()
    {
        var container = typeof(KeyedServerHolder.ChangePayload).BaseType!.DeclaringType!;
        var roles = container.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
        var collectionRole = roles.Single(static type => type.Name.StartsWith("CollectionChange", StringComparison.Ordinal));
        var itemRole = roles.Single(static type => type.Name.StartsWith("ItemOperation", StringComparison.Ordinal));
        var membersDelta = roles.Single(static type => type.Name.StartsWith("MembersDelta", StringComparison.Ordinal));
        var collectionChange = membersDelta.GetProperty("Items")!.PropertyType;
        var itemOperation = collectionChange.GetProperty("Items")!.PropertyType.GetGenericArguments()[0];

        collectionChange.BaseType!.GetGenericTypeDefinition().ShouldBe(collectionRole);
        itemOperation.BaseType!.GetGenericTypeDefinition().ShouldBe(itemRole);
    }

    [Test]
    public void PayloadIgnoresExtensionPropertiesAtEveryLevel()
    {
        var json =
            """{"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"},"extension":{"nested":true}}},"extension":123}}""";
        var restored = PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(json);

        restored.ToChangeSet().Label.After.Value.ShouldBe("after");

        var nestedJson =
            """{"version":"0.1","change":{"kind":"update","members":{"nested":{"kind":"update","members":{"host":{"kind":"replace","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}}},"extension":1}}}}""";
        var nested = PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(nestedJson);
        nested.ToChangeSet().Nested.Host.After.Value.ShouldBe("b");

        var keyedJson =
            """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"add","afterIndex":0,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}},"extension":"x"}],"beforeOrder":[],"afterOrder":["a"]}}}}""";
        var keyed = PayloadJsonTestSupport.Deserialize<KeyedServerHolder.ChangePayload>(keyedJson);
        keyed.ToChangeSet().Items.GetChange("a").IsAdded.ShouldBeTrue();
    }

    [Test]
    public void PayloadStillRejectsUnknownOperations()
    {
        foreach (
            var json in new[]
            {
                """{"version":"0.1","change":{"kind":"update","members":{"nope":{"kind":"replace","after":{"state":"value","value":"b"}}}}}""",
                """{"version":"0.1","change":{"kind":"update","members":{"nested":{"kind":"update","members":{"nope":{"kind":"replace","after":{"state":"value","value":"b"}}}}}}}""",
            }
        )
        {
            var exception = Should.Throw<JsonException>(() => PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(json));
            exception.Message.ShouldContain("nope");
            exception.Message.ShouldContain("member target");
        }
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<KeyedServerHolder.ChangePayload>(
                """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"update","beforeIndex":0,"afterIndex":0,"members":{"nope":{"kind":"replace","after":{"state":"value","value":"b"}}}}],"beforeOrder":["a"],"afterOrder":["a"]}}}}"""
            )
        );

        // Unknown and legacy item kinds never drop an operation silently.
        const string itemJson =
            """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"__kind__","afterIndex":0,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}}}],"beforeOrder":[],"afterOrder":["a"]}}}}""";
        foreach (var kind in new[] { "bogus", "edit", "reorder" })
        {
            var json = itemJson.Replace("__kind__", kind, StringComparison.Ordinal);
            Should.Throw<JsonException>(() => PayloadJsonTestSupport.Deserialize<KeyedServerHolder.ChangePayload>(json));
        }

        // Unknown endpoint states never decode silently.
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<Settings.ChangePayload>(
                """{"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"nope"},"after":{"state":"value","value":"b"}}}}}"""
            )
        );
    }

    [Test]
    public void NestedSnapshotEndpointsRejectValuesForValueFreeStates()
    {
        var json =
            """{"version":"0.1","change":{"kind":"replace","before":{"state":"value","value":{"nested":{"state":"missing","value":{"host":{"state":"value","value":"secret"}}}}},"after":{"state":"value","value":{}}}}""";

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.ChangePayload>(
                PayloadJsonTestSupport.WithSchemaIdentity<Settings.ChangePayload>(json),
                Settings.PayloadJson.Default
            )
        );
    }

    [Test]
    public void PayloadRejectsDuplicateKnownAndUnknownProperties()
    {
        foreach (
            var json in new[]
            {
                """{"version":"0.1","version":"0.1","change":{"kind":"update","members":{}}}""",
                """{"version":"0.1","extension":1,"extension":2,"change":{"kind":"update","members":{}}}""",
                """{"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"value","value":"a"},"before":{"state":"value","value":"b"},"after":{"state":"value","value":"c"}}}}}""",
                """{"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"},"extension":1,"extension":2}}}""",
            }
        )
        {
            var rejected = false;
            try
            {
                JsonSerializer.Deserialize(PayloadJsonTestSupport.WithSchemaIdentity<Settings.ChangePayload>(json), Settings.PayloadJson.Default);
            }
            catch (JsonException)
            {
                rejected = true;
            }

            rejected.ShouldBeTrue(json);
        }
    }

    [Test]
    public void WireByteFormsMatchTheStringVocabulary()
    {
        // The byte comparisons are one encoding of the same constants, so a
        // wire vocabulary edit must move both together.
        System
            .Text.Encoding.UTF8.GetBytes(ChangePayloadWire.StateProperty)
            .AsSpan()
            .SequenceEqual(ChangePayloadWire.StatePropertyUtf8())
            .ShouldBeTrue();
        System
            .Text.Encoding.UTF8.GetBytes(ChangePayloadWire.ValueProperty)
            .AsSpan()
            .SequenceEqual(ChangePayloadWire.ValuePropertyUtf8())
            .ShouldBeTrue();
        foreach (var state in Enum.GetValues<ChangePayloadState>())
        {
            System
                .Text.Encoding.UTF8.GetBytes(ChangePayloadWire.ToToken(state))
                .AsSpan()
                .SequenceEqual(ChangePayloadWire.StateTokenUtf8(state))
                .ShouldBeTrue();
        }

        Should.Throw<System.ArgumentOutOfRangeException>(() => ChangePayloadWire.StateTokenUtf8((ChangePayloadState)99));
    }
}
