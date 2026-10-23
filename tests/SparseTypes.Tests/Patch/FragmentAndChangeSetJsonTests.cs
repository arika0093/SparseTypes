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
public partial class WideDispatchWidget
{
    public int Alpha { get; set; }
    public int Bravo { get; set; }
    public int Charlie { get; set; }
    public int Delta { get; set; }
    public int Echo { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("fixedName")]
    public int Explicit { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    [System.Text.Json.Serialization.JsonPropertyName("Alpha_1")]
    public int IgnoredAlias { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Hidden { get; set; }
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

public sealed class CountingSuffixNamingPolicy : JsonNamingPolicy
{
    public string Suffix { get; set; } = "_1";

    public int ConversionCount { get; private set; }

    public void ResetCount() => ConversionCount = 0;

    public override string ConvertName(string name)
    {
        ConversionCount++;
        return name + Suffix;
    }
}

public sealed class DuplicateNameNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) => "duplicate";
}

public sealed class FragmentAndChangeSetJsonTests
{
    [Test]
    public void WideFragmentReadBuildsEffectiveNamesOnceAndHonorsVisiblePriority()
    {
        var namingPolicy = new CountingSuffixNamingPolicy();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = namingPolicy,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var reader = new System.Text.Json.Utf8JsonReader(
            System.Text.Encoding.UTF8.GetBytes(
                """{"alpha_1":1,"Bravo_1":2,"Charlie_1":3,"Delta_1":4,"Echo_1":5,"fixedName":6,"hidden_1":7,"unknown":{"nested":true}}"""
            )
        );
        reader.Read().ShouldBeTrue();

        var fragment = WideDispatchWidget.Fragment.JsonConverter.Read(ref reader, typeof(WideDispatchWidget.Fragment), options)!;

        namingPolicy.ConversionCount.ShouldBe(6);
        fragment.Alpha.Value.ShouldBe(1);
        fragment.Bravo.Value.ShouldBe(2);
        fragment.Charlie.Value.ShouldBe(3);
        fragment.Delta.Value.ShouldBe(4);
        fragment.Echo.Value.ShouldBe(5);
        fragment.Explicit.Value.ShouldBe(6);
        fragment.IgnoredAlias.IsPresent.ShouldBeFalse();
        fragment.Hidden.IsPresent.ShouldBeFalse();

        namingPolicy.Suffix = "_2";
        namingPolicy.ResetCount();
        var nextReader = new System.Text.Json.Utf8JsonReader(
            System.Text.Encoding.UTF8.GetBytes("""{"Alpha_1":8,"Bravo_1":9,"Charlie_1":10,"Delta_1":11,"Echo_1":12,"fixedName":13,"Hidden_1":14}""")
        );
        nextReader.Read().ShouldBeTrue();

        var next = WideDispatchWidget.Fragment.JsonConverter.Read(ref nextReader, typeof(WideDispatchWidget.Fragment), options)!;

        namingPolicy.ConversionCount.ShouldBe(0);
        next.Alpha.Value.ShouldBe(8);
        next.Explicit.Value.ShouldBe(13);
        next.Hidden.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void CustomNamingPolicyResultsAreCachedPerOptionsInstance()
    {
        var namingPolicy = new CountingSuffixNamingPolicy();
        var firstOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = namingPolicy,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        firstOptions.Converters.Add(WideDispatchWidget.Fragment.JsonConverter);
        firstOptions.MakeReadOnly();

        ReadWideFragment("""{"Alpha_1":1}""", firstOptions).Alpha.Value.ShouldBe(1);
        namingPolicy.ConversionCount.ShouldBe(6);

        namingPolicy.Suffix = "_2";
        ReadWideFragment("""{"Bravo_1":2}""", firstOptions).Bravo.Value.ShouldBe(2);
        ReadWideFragment("""{"Bravo_2":3}""", firstOptions).Bravo.IsPresent.ShouldBeFalse();
        namingPolicy.ConversionCount.ShouldBe(6);

        var secondOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = namingPolicy,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        secondOptions.Converters.Add(WideDispatchWidget.Fragment.JsonConverter);
        secondOptions.MakeReadOnly();
        ReadWideFragment("""{"Bravo_2":4}""", secondOptions).Bravo.Value.ShouldBe(4);
        namingPolicy.ConversionCount.ShouldBe(12);

        var fragment = new WideDispatchWidget.Fragment { Alpha = Optional<int>.Present(3), Explicit = Optional<int>.Present(4) };
        var firstJson = JsonSerializer.Serialize(fragment, firstOptions);
        var secondJson = JsonSerializer.Serialize(fragment, secondOptions);

        firstJson.ShouldContain("\"Alpha_1\"");
        firstJson.ShouldContain("\"fixedName\"");
        secondJson.ShouldContain("\"Alpha_2\"");
        namingPolicy.ConversionCount.ShouldBe(12);
    }

    [Test]
    public void WideFragmentReadRejectsVisibleCollisionsAndDisallowedUnknownNames()
    {
        var collisionOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new DuplicateNameNamingPolicy(),
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        Should.Throw<JsonException>(() => ReadWideFragment("{}", collisionOptions));

        var duplicateWriteOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new DuplicateNameNamingPolicy(),
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        duplicateWriteOptions.Converters.Add(WideDispatchWidget.Fragment.JsonConverter);
        var duplicateJson = JsonSerializer.Serialize(
            new WideDispatchWidget.Fragment { Alpha = Optional<int>.Present(1), Bravo = Optional<int>.Present(2) },
            duplicateWriteOptions
        );
        duplicateJson.Split("\"duplicate\"", StringSplitOptions.None).Length.ShouldBe(3);

        var strictOptions = new JsonSerializerOptions
        {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        Should.Throw<JsonException>(() => ReadWideFragment("""{"unknown":{"nested":true}}""", strictOptions));
    }

    private static WideDispatchWidget.Fragment ReadWideFragment(string json, JsonSerializerOptions options)
    {
        var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read();
        return WideDispatchWidget.Fragment.JsonConverter.Read(ref reader, typeof(WideDispatchWidget.Fragment), options)!;
    }

    [Test]
    public void FragmentWireNamesRoundTrip()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(NamingWidget.Fragment.JsonConverter);

        var fragment = new NamingWidget.Fragment { Value = Optional<string?>.Present("v"), Slash = Optional<int>.Present(7) };
        var json = JsonSerializer.Serialize(fragment, options);
        json.ShouldContain("customName");
        json.ShouldContain("a/b");

        var back = JsonSerializer.Deserialize<NamingWidget.Fragment>(json, options)!;
        back.Value.Value.ShouldBe("v");
        back.Slash.Value.ShouldBe(7);
        back.Plain.IsPresent.ShouldBeFalse();
    }

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
    public void CachedIndexDoesNotGoStaleWhenSharedOptionsAreMutated()
    {
        // Each options instance owns its snapshot, and a mutable options
        // instance changing policies must rebuild before it is locked.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        // Unknown-only reads skip member lookup, so the options stay mutable
        // while these repeated reads exercise the cached camelCase index.
        ReadWideFragment("""{"unknown":1}""", options).Alpha.IsPresent.ShouldBeFalse();
        ReadWideFragment("""{"unknown":2}""", options).Alpha.IsPresent.ShouldBeFalse();

        options.PropertyNamingPolicy = null;
        var identity = ReadWideFragment("""{"Alpha":3,"fixedName":4}""", options);
        identity.Alpha.Value.ShouldBe(3);
        identity.Explicit.Value.ShouldBe(4);
        ReadWideFragment("""{"Alpha":5}""", options).Alpha.Value.ShouldBe(5);
    }

    [Test]
    public void CachedIndexKeysCaseHandlingAcrossOptionsInstances()
    {
        var sensitive = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var insensitive = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        for (var i = 0; i < 3; i++)
        {
            ReadWideFragment("""{"ALPHA":1}""", sensitive).Alpha.IsPresent.ShouldBeFalse();
            ReadWideFragment("""{"ALPHA":2}""", insensitive).Alpha.Value.ShouldBe(2);
        }
    }

    [Test]
    public void CachedIndexSurvivesAlternatingOptionsInstances()
    {
        var camel = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var identity = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        for (var i = 0; i < 3; i++)
        {
            ReadWideFragment("""{"alpha":1}""", camel).Alpha.Value.ShouldBe(1);
            ReadWideFragment("""{"Alpha":2}""", identity).Alpha.Value.ShouldBe(2);
        }
    }

    [Test]
    public void ConcurrentReadsShareTheCachedIndex()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        System.Threading.Tasks.Parallel.For(0, 64, _ => ReadWideFragment("""{"alpha":1,"fixedName":2}""", options).Alpha.Value.ShouldBe(1));
    }

    [Test]
    public void CachedReadsAvoidPerReadIndexAllocations()
    {
        var cachedOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        const string cachedPayload = """{"alpha":1,"bravo":2,"charlie":3,"delta":4,"echo":5,"fixedName":6}""";
        for (var i = 0; i < 10; i++)
        {
            ReadWideFragment(cachedPayload, cachedOptions);
        }

        var cachedOptionsPerRead = Enumerable.Repeat(cachedOptions, 200).ToArray();
        var cachedBytes = MeasureWideReadBytes(cachedPayload, cachedOptionsPerRead);

        var namingPolicy = new CountingSuffixNamingPolicy();
        var uncachedOptionsPerRead = Enumerable
            .Range(0, 200)
            .Select(_ => new JsonSerializerOptions
            {
                PropertyNamingPolicy = namingPolicy,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            })
            .ToArray();
        const string uncachedPayload = """{"Alpha_1":1,"Bravo_1":2,"Charlie_1":3,"Delta_1":4,"Echo_1":5,"fixedName":6}""";
        var uncachedBytes = MeasureWideReadBytes(uncachedPayload, uncachedOptionsPerRead);
        cachedBytes.ShouldBeLessThan(uncachedBytes);
        cachedBytes.ShouldBeLessThan(uncachedBytes);
    }

    private static long MeasureWideReadBytes(string json, JsonSerializerOptions[] optionsPerRead)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        var before = System.GC.GetAllocatedBytesForCurrentThread();
        foreach (var options in optionsPerRead)
        {
            ReadWideFragment(bytes, options);
        }

        return System.GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static WideDispatchWidget.Fragment ReadWideFragment(byte[] json, JsonSerializerOptions options)
    {
        var reader = new System.Text.Json.Utf8JsonReader(json);
        reader.Read();
        return WideDispatchWidget.Fragment.JsonConverter.Read(ref reader, typeof(WideDispatchWidget.Fragment), options)!;
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
        payload.Changes.ShouldHaveSingleItem();
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
        mixed.ToPayload().Changes!.Count.ShouldBe(2);
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(mixed.ToPayload(), NamingWidget.PayloadJson.Default));

        var patchPayload = NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Plain = 3 });
        patchPayload.Changes.ShouldHaveSingleItem();
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(patchPayload, NamingWidget.PayloadJson.Default));
        NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Value = "b" }).Changes.ShouldHaveSingleItem();
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
        visiblePayload.Changes.ShouldHaveSingleItem();
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
