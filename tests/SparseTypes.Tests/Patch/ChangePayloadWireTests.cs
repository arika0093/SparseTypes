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
        envelope.EnumerateObject().Select(static property => property.Name).ShouldBe(new[] { "version", "change" });
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
    public void PayloadDtosDoNotExposeReflectionJsonAttributes()
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
        typeof(Settings.ChangePayload).GetCustomAttribute<JsonConverterAttribute>().ShouldBeNull();
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
        var restored = JsonSerializer.Deserialize<Settings.ChangePayload>(json, Settings.PayloadJson.Default)!;

        restored.ToChangeSet().Label.After.Value.ShouldBe("after");

        var nestedJson =
            """{"version":"0.1","change":{"kind":"update","members":{"nested":{"kind":"update","change":{"kind":"update","members":{"host":{"kind":"replace","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}}}},"extension":1}}}}""";
        var nested = JsonSerializer.Deserialize<Settings.ChangePayload>(nestedJson, Settings.PayloadJson.Default)!;
        nested.ToChangeSet().Nested.Host.After.Value.ShouldBe("b");

        var keyedJson =
            """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"add","afterIndex":0,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}},"extension":"x"}],"beforeOrder":[],"afterOrder":["a"]}}}}""";
        var keyed = JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(keyedJson, KeyedServerHolder.PayloadJson.Default)!;
        keyed.ToChangeSet().Items.GetChange("a").IsAdded.ShouldBeTrue();
    }

    [Test]
    public void PayloadStillRejectsUnknownOperations()
    {
        // Unknown model-member names are additive extensions and are skipped.
        var unknownMember = JsonSerializer.Deserialize<Settings.ChangePayload>(
            """{"version":"0.1","change":{"kind":"update","members":{"nope":{"kind":"replace","after":{"state":"value","value":"b"}}}}}""",
            Settings.PayloadJson.Default
        )!;
        unknownMember.ToChangeSet().ShouldNotBeNull();

        // Unknown and legacy item kinds never drop an operation silently.
        const string itemJson =
            """{"version":"0.1","change":{"kind":"update","members":{"items":{"kind":"update","items":[{"key":"a","kind":"__kind__","afterIndex":0,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}}}],"beforeOrder":[],"afterOrder":["a"]}}}}""";
        foreach (var kind in new[] { "bogus", "edit", "reorder" })
        {
            var json = itemJson.Replace("__kind__", kind, StringComparison.Ordinal);
            Should.Throw<JsonException>(() =>
                JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(json, KeyedServerHolder.PayloadJson.Default)
            );
        }

        // Unknown endpoint states never decode silently.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.ChangePayload>(
                """{"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"nope"},"after":{"state":"value","value":"b"}}}}}""",
                Settings.PayloadJson.Default
            )
        );
    }

    [Test]
    public void NestedSnapshotEndpointsRejectValuesForValueFreeStates()
    {
        var json =
            """{"version":"0.1","change":{"kind":"replace","before":{"state":"value","value":{"nested":{"state":"missing","value":{"host":{"state":"value","value":"secret"}}}}},"after":{"state":"value","value":{}}}}""";

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Settings.ChangePayload>(json, Settings.PayloadJson.Default));
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
                JsonSerializer.Deserialize(json, Settings.PayloadJson.Default);
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

        foreach (var kind in Enum.GetValues<ChangePayloadItemKind>())
        {
            System
                .Text.Encoding.UTF8.GetBytes(ChangePayloadWire.ToToken(kind))
                .AsSpan()
                .SequenceEqual(ChangePayloadWire.KindTokenUtf8(kind))
                .ShouldBeTrue();
        }

        Should.Throw<System.ArgumentOutOfRangeException>(() =>
            ChangePayloadWire.StateTokenUtf8((ChangePayloadState)99)
        );
        Should.Throw<System.ArgumentOutOfRangeException>(() =>
            ChangePayloadWire.KindTokenUtf8((ChangePayloadItemKind)99)
        );
    }

    [Test]
    public void BridgeDispatchesEscapedEnvelopeNamesAndTokens()
    {
        var endpoint = ReadEndpoint("""{"\u0073tate":"\u0076alue","value":"kept"}""");

        endpoint.State.ShouldBe(ChangePayloadState.Value);
        endpoint.Value.ShouldBe("kept");
    }

    [Test]
    public void BridgeDispatchesMultiSegmentEnvelopeInput()
    {
        // The envelope name and state token are split across reader segments
        // and the escape byte sits on a boundary, like real streaming reads.
        var firstChunk = System.Text.Encoding.UTF8.GetBytes(@"{""st");
        var secondChunk = System.Text.Encoding.UTF8.GetBytes(@"\u0061te"":""\u0076");
        var thirdChunk = System.Text.Encoding.UTF8.GetBytes(@"alue"",""value"":""kept""}");
        var firstSegment = new TestSequenceSegment(firstChunk);
        var middleSegment = firstSegment.Append(secondChunk);
        var endSegment = middleSegment.Append(thirdChunk);
        var reader = new Utf8JsonReader(
            new System.Buffers.ReadOnlySequence<byte>(
                firstSegment,
                0,
                endSegment,
                endSegment.Memory.Length
            )
        );
        reader.Read().ShouldBeTrue();

        var endpoint = CreateEndpointConverter<string>()
            .Read(
                ref reader,
                typeof(ChangePayloadEndpoint<string>),
                new JsonSerializerOptions
                {
                    TypeInfoResolver =
                        new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
                }
            );
        endpoint!.State.ShouldBe(ChangePayloadState.Value);
        endpoint.Value.ShouldBe("kept");
    }

    [Test]
    public void BridgeKeepsThePreviousTokenFailureBehavior()
    {
        // An unknown string keeps its wire-level JsonException.
        Should.Throw<JsonException>(() => ReadEndpoint("""{"state":"nope"}"""));

        // An explicit null token stays "unknown", as TryParseState(null) did.
        Should.Throw<JsonException>(() => ReadEndpoint("""{"state":null}"""));

        // A non-string token still surfaces through GetString's
        // InvalidOperationException rather than a silent mismatch.
        Should.Throw<InvalidOperationException>(() => ReadEndpoint("""{"state":5}"""));
        Should.Throw<InvalidOperationException>(() => ReadEndpoint("""{"state":true}"""));

        // Item kinds keep the same error taxonomy.
        var kindConverterType =
            typeof(global::SparseTypes.Generated.ChangePayloadItemKindJsonConverter);
        var kindConverter =
            (JsonConverter<ChangePayloadItemKind>)Activator.CreateInstance(kindConverterType)!;
        var options = new JsonSerializerOptions();
        options.Converters.Add(kindConverter);

        // A non-string root token keeps the direct-converter taxonomy: the
        // converter surfaces GetString's InvalidOperationException.
        Should.Throw<InvalidOperationException>(() => ReadKindToken(kindConverter));

        // The serializer stays diagnostic for invalid tokens too (wrapped).
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadItemKind>("5", options)
        );
    }

    [Test]
    public void BridgeKeepsEnvelopeDispatchInsensitiveToNamingPolicy()
    {
        // The v1 envelope names are fixed constants: options that restyle
        // fragment members never move them, and Pascal-cased input stays unknown.
        var policyOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        policyOptions.Converters.Add(CreateEndpointConverter<string>());

        var endpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
            """{"state":"value","value":"kept"}""",
            policyOptions
        )!;
        endpoint.State.ShouldBe(ChangePayloadState.Value);

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
                """{"State":"value","Value":null}""",
                policyOptions
            )
        );
    }

    [Test]
    public void BridgeKeepsLastDuplicateStateAndValue()
    {
        var first = ReadEndpoint(
            """{"state":"missing","state":"value","value":null,"value":"kept"}"""
        );
        first.State.ShouldBe(ChangePayloadState.Value);
        first.Value.ShouldBe("kept");
    }

    internal static JsonConverter<ChangePayloadEndpoint<TValue>> CreateEndpointConverter<TValue>()
    {
        var converterType =
            typeof(global::SparseTypes.Generated.ChangePayloadEndpointJsonConverter<>).MakeGenericType(
                typeof(TValue)
            );
        return (JsonConverter<ChangePayloadEndpoint<TValue>>)
            Activator.CreateInstance(converterType)!;
    }

    internal static ChangePayloadEndpoint<string> ReadEndpoint(string json) =>
        ReadEndpoint(System.Text.Encoding.UTF8.GetBytes(json));

    internal static ChangePayloadEndpoint<string> ReadEndpoint(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        reader.Read().ShouldBeTrue();
        // Direct converter calls need resolvable declared-type metadata for
        // the value branch, exactly like the fragment converter.
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(CreateEndpointConverter<string>());
        return CreateEndpointConverter<string>()
            .Read(ref reader, typeof(ChangePayloadEndpoint<string>), options)!;
    }

    private static void ReadKindToken(JsonConverter<ChangePayloadItemKind> converter)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes("5"));
        reader.Read().ShouldBeTrue();
        converter.Read(ref reader, typeof(ChangePayloadItemKind), new JsonSerializerOptions());
    }

    private sealed class TestSequenceSegment : System.Buffers.ReadOnlySequenceSegment<byte>
    {
        public TestSequenceSegment(byte[] data) => Memory = data;

        public TestSequenceSegment Append(byte[] data)
        {
            var next = new TestSequenceSegment(data);
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
            return next;
        }
    }
}
