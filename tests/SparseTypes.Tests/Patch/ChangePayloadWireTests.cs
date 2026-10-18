using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SparseTypes.Tests.Patch;

/// <summary>Certifies the optional-JSON split.</summary>
/// <remarks>The base runtime carries no System.Text.Json reference; the v1 wire shape is implemented by generated bridge converters.</remarks>
public sealed class ChangePayloadWireTests
{
    [Test]
    public void RuntimeCarriesNoSystemTextJsonReference()
    {
        typeof(ChangePayloadEndpoint<>)
            .Assembly.GetReferencedAssemblies()
            .Select(static name => name.Name)
            .ShouldNotContain("System.Text.Json");
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
            (attribute.GetType().FullName ?? string.Empty).ShouldNotStartWith(
                "System.Text.Json",
                Case.Sensitive
            );
        }
    }

    [Test]
    public void WireTokensMatchVersion1()
    {
        ChangePayloadWire.Version.ShouldBe("0.1");
        ChangePayloadWire.ToToken(ChangePayloadState.Missing).ShouldBe("missing");
        ChangePayloadWire.ToToken(ChangePayloadState.Null).ShouldBe("null");
        ChangePayloadWire.ToToken(ChangePayloadState.Value).ShouldBe("value");
        ChangePayloadWire.ToToken(ChangePayloadState.Redacted).ShouldBe("redacted");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Add).ShouldBe("add");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Remove).ShouldBe("remove");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Edit).ShouldBe("edit");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Reorder).ShouldBe("reorder");

        foreach (var state in Enum.GetValues<ChangePayloadState>())
        {
            ChangePayloadWire
                .TryParseState(ChangePayloadWire.ToToken(state), out var parsedState)
                .ShouldBeTrue();
            parsedState.ShouldBe(state);
        }
        foreach (var kind in Enum.GetValues<ChangePayloadItemKind>())
        {
            ChangePayloadWire
                .TryParseKind(ChangePayloadWire.ToToken(kind), out var parsedKind)
                .ShouldBeTrue();
            parsedKind.ShouldBe(kind);
        }
        ChangePayloadWire.TryParseState("nope", out _).ShouldBeFalse();
        ChangePayloadWire.TryParseKind("nope", out _).ShouldBeFalse();
    }

    [Test]
    public void GeneratedBridgeWritesStateBeforeValue()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var payload = Settings
            .ChangeSet.Between(
                Optional<Settings.Fragment?>.Present(before),
                Optional<Settings.Fragment?>.Present(after)
            )
            .ToPayload();
        var json = JsonSerializer.Serialize(payload);

        // v1 order: state(0) precedes value(1) inside every endpoint.
        var state = json.IndexOf("\"state\":\"value\"", StringComparison.Ordinal);
        var value = json.IndexOf("\"value\":\"after\"", StringComparison.Ordinal);
        state.ShouldBeGreaterThanOrEqualTo(0);
        value.ShouldBeGreaterThan(state);
    }

    [Test]
    public void GeneratedPayloadContainerOwnsEndpointAndKindConverters()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var endpointConverter = container
            .GetNestedTypes()
            .SingleOrDefault(static type =>
                type
                    is {
                        IsGenericTypeDefinition: true,
                        Name: "ChangePayloadEndpointJsonConverter`1"
                    }
            );
        endpointConverter.ShouldNotBeNull();
        var kindConverter = container
            .GetNestedTypes()
            .SingleOrDefault(static type => type.Name == "ChangePayloadItemKindJsonConverter");
        kindConverter.ShouldNotBeNull();

        // Every endpoint-typed DTO property routes through the bridge, so the
        // wire no longer depends on runtime attributes.
        var endpointProperties = container
            .GetNestedTypes()
            .SelectMany(static type => type.GetProperties())
            .Where(static property =>
                property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition()
                    == typeof(ChangePayloadEndpoint<>)
            )
            .ToArray();
        endpointProperties.ShouldNotBeEmpty();
        foreach (var property in endpointProperties)
            property
                .GetCustomAttribute<JsonConverterAttribute>()
                .ShouldNotBeNull(property.DeclaringType!.Name + "." + property.Name);
    }

    [Test]
    public void BridgeRejectsUnknownStateTokens()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(string));
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        var endpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
            """{"state":"value","value":"kept"}""",
            options
        )!;
        endpoint.State.ShouldBe(ChangePayloadState.Value);
        endpoint.Value.ShouldBe("kept");

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
                """{"state":"nope","value":null}""",
                options
            )
        );
    }

    [Test]
    public void BridgeRoundTripsEveryEndpointState()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(string));
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        // Scalar, keyed, dictionary, and nested round-trips live in
        // ChangePayloadTests; redaction round-trips live in
        foreach (var state in Enum.GetValues<ChangePayloadState>())
        {
            var token = ChangePayloadWire.ToToken(state);
            var endpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
                "{\"state\":\"" + token + "\",\"value\":null}",
                options
            )!;
            endpoint.State.ShouldBe(state);

            var json = JsonSerializer.Serialize(endpoint, options);
            json.ShouldContain("\"state\":\"" + token + "\"");
        }
    }

    [Test]
    public void BridgeRequiresExplicitEndpointState()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(string));
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        // An explicit missing state stays valid.
        var missing = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
            """{"state":"missing","value":null}""",
            options
        )!;
        missing.State.ShouldBe(ChangePayloadState.Missing);

        // An omitted state is not an explicit missing endpoint, even when
        // only additive extension properties are present.
        foreach (var json in new[] { "{}", """{"value":"kept"}""", """{"extension":123}""" })
        {
            Should.Throw<JsonException>(() =>
                JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(json, options)
            );
        }

        // Null and unknown states still fail.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
                """{"state":null,"value":null}""",
                options
            )
        );
    }

    [Test]
    public void BridgeIgnoresUnknownExtensionProperties()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(string));
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        // Extra fields ride along without changing the interpreted endpoint;
        // later duplicates still win.
        var endpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
            """{"state":"value","value":"kept","extension":123}""",
            options
        )!;
        endpoint.State.ShouldBe(ChangePayloadState.Value);
        endpoint.Value.ShouldBe("kept");
    }

    [Test]
    public void PayloadIgnoresExtensionPropertiesAtEveryLevel()
    {
        var json =
            """{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"},"extension":{"nested":true}}],"extension":123}""";
        var restored = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!;

        restored.ToChangeSet().Label.After.Value.ShouldBe("after");

        var nestedJson =
            """{"version":"0.1","changes":[{"member":"Nested","nested":{"changes":[{"member":"Host","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}}]},"extension":1}]}""";
        var nested = JsonSerializer.Deserialize<Settings.ChangePayload>(nestedJson)!;
        nested.ToChangeSet().Nested.Host.After.Value.ShouldBe("b");

        var keyedJson =
            """{"version":"0.1","changes":[{"member":"Items","items":[{"key":"a","kind":"add","beforeIndex":-1,"afterIndex":0,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}},"extension":"x"}],"beforeOrder":[],"afterOrder":["a"]}]}""";
        var keyed = JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(keyedJson)!;
        keyed.ToChangeSet().Items.GetChange("a").IsAdded.ShouldBeTrue();
    }

    [Test]
    public void PayloadStillRejectsUnknownOperations()
    {
        // Unknown member discriminators never drop an operation silently.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.ChangePayload>(
                """{"version":"0.1","changes":[{"member":"Nope","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}}]}"""
            )
        );

        // Unknown item kinds never drop an operation silently.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(
                """{"version":"0.1","changes":[{"member":"Items","items":[{"key":"a","kind":"bogus","beforeIndex":-1,"afterIndex":0,"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}}}],"beforeOrder":[],"afterOrder":["a"]}]}"""
            )
        );

        // Unknown endpoint states never decode silently.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.ChangePayload>(
                """{"version":"0.1","changes":[{"member":"Label","before":{"state":"nope"},"after":{"state":"value","value":"b"}}]}"""
            )
        );
    }

    [Test]
    public void BridgeRoundTripsEveryItemKindAndRejectsUnknownTokens()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadItemKindJsonConverter");
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        foreach (var kind in Enum.GetValues<ChangePayloadItemKind>())
        {
            var token = ChangePayloadWire.ToToken(kind);
            var json = JsonSerializer.Serialize(kind, options);
            json.ShouldBe("\"" + token + "\"");
            JsonSerializer.Deserialize<ChangePayloadItemKind>(json, options).ShouldBe(kind);
        }

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadItemKind>("\"bogus\"", options)
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadItemKind>("null", options)
        );
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
            System
                .Text.Encoding.UTF8.GetBytes(ChangePayloadWire.ToToken(state))
                .AsSpan()
                .SequenceEqual(ChangePayloadWire.StateTokenUtf8(state))
                .ShouldBeTrue();
        foreach (var kind in Enum.GetValues<ChangePayloadItemKind>())
            System
                .Text.Encoding.UTF8.GetBytes(ChangePayloadWire.ToToken(kind))
                .AsSpan()
                .SequenceEqual(ChangePayloadWire.KindTokenUtf8(kind))
                .ShouldBeTrue();

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
        var kindConverterType = container()
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadItemKindJsonConverter");
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
        var converterType = container()
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(TValue));
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

    private static Type container()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        container.ShouldNotBeNull();
        return container;
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

    [Test]
    public void KnownEndpointReadsStayBelowTheStringDispatchBaseline()
    {
        const string endpointJson = """{"state":"value","value":42}""";
        var converter = CreateEndpointConverter<int>();
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var bytes = System.Text.Encoding.UTF8.GetBytes(endpointJson);
        for (var i = 0; i < 50; i++)
        {
            var warmReader = new Utf8JsonReader(bytes);
            warmReader.Read();
            converter.Read(ref warmReader, typeof(ChangePayloadEndpoint<int>), options);
        }

        // The old dispatch materialized two envelope name strings, a state
        // token string and a boxed int per read; a value read now touches
        // none of those, so the warm loop stays under a quarter of that
        // per-read budget. Compare thresholds allow for metadata lookup noise.
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        var before = System.GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            var reader = new Utf8JsonReader(bytes);
            reader.Read();
            converter.Read(ref reader, typeof(ChangePayloadEndpoint<int>), options);
        }
        var perRead = (System.GC.GetAllocatedBytesForCurrentThread() - before) / (double)500;

        perRead.ShouldBeLessThan(30.0);
    }
}
