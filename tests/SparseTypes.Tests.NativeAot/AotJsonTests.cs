using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SparseTypes.NativeAotFixtures;

namespace SparseTypes.Tests.NativeAot;

public sealed class AotJsonTests
{
    private static JsonSerializerOptions PayloadCollectionOptions() =>
        new() { TypeInfoResolver = JsonTypeInfoResolver.Combine(PayloadCollection.PayloadJson.Resolver, PayloadLeafContext.Default) };

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<PayloadCollection.ChangePayload> PayloadCollectionTypeInfo(
        JsonSerializerOptions options
    ) =>
        (System.Text.Json.Serialization.Metadata.JsonTypeInfo<PayloadCollection.ChangePayload>)
            options.GetTypeInfo(typeof(PayloadCollection.ChangePayload));

    private static JsonSerializerOptions AotOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(AotWidget.Fragment.JsonConverter);
        return options;
    }

    // Serializes through the generated converter directly (the same path the
    // Patch converter uses for fragments).
    private static string ToCanonicalJson(AotWidget.Fragment fragment, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            AotWidget.Fragment.JsonConverter.Write(writer, fragment, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AotWidget.Fragment JsonBaseline() =>
        new()
        {
            Name = Optional<string?>.Present("a"),
            Count = Optional<int>.Present(1),
            Nested = Optional<AotNested.Fragment?>.Present(new AotNested.Fragment { Host = Optional<string>.Present("h") }),
        };

    [Test]
    public async Task FragmentSerializesPresentMembersAndOmitsMissing()
    {
        var canonical = ToCanonicalJson(JsonBaseline(), AotOptions());

        await Assert.That(canonical.Contains("\"Name\":\"a\"")).IsTrue();
        await Assert.That(!canonical.Contains("Plugins")).IsTrue();
    }

    [Test]
    public async Task FragmentPreservesExplicitNullInJson()
    {
        var explicitNullFragment = new AotWidget.Fragment { Name = Optional<string?>.Present(null) };

        await Assert.That(ToCanonicalJson(explicitNullFragment, AotOptions()).Contains("\"Name\":null")).IsTrue();
    }

    [Test]
    public async Task ChangePayloadRoundTripsWithSourceGenAndNormalizedJson()
    {
        // sample: payload-native-aot
        Optional<PayloadRoot.Fragment?> WidgetState(string label, string name) =>
            Optional<PayloadRoot.Fragment?>.Present(
                PayloadRoot.Fragment.From(
                    new PayloadRoot
                    {
                        Label = label,
                        Child = new PayloadChild { Name = name, Count = 1 },
                    }
                )
            );

        var widgetBefore = WidgetState("old", "h1");
        var widgetAfter = WidgetState("new", "h2");
        var changes = PayloadRoot.ChangeSet.Between(widgetBefore, widgetAfter);
        var json = JsonSerializer.Serialize(changes.ToPayload(), PayloadRoot.PayloadJson.Default);
        // /sample
        using (var document = JsonDocument.Parse(json))
        {
            await Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(["version", "change"])).IsTrue();
            await Assert.That(document.RootElement.GetProperty("version").GetString()).IsEqualTo("0.1");
            var change = document.RootElement.GetProperty("change");
            await Assert.That(change.GetProperty("kind").GetString()).IsEqualTo("update");
            var members = change.GetProperty("members");
            var nested = members.GetProperty("child").GetProperty("change");
            await Assert.That(nested.TryGetProperty("version", out _)).IsFalse();
            await Assert.That(nested.GetProperty("kind").GetString()).IsEqualTo("update");
            await Assert.That(nested.TryGetProperty("members", out _)).IsTrue();
            var endpoint = members.GetProperty("label").GetProperty("after");
            await Assert.That(endpoint.EnumerateObject().Select(property => property.Name).SequenceEqual(["state", "value"])).IsTrue();
        }
        await Assert.That(json.Contains("\"state\":\"value\"")).IsTrue();

        // sample: payload-native-aot
        var restored = JsonSerializer.Deserialize(json, PayloadRoot.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadRoot.Patch.Between(restored.ToPatch().Apply(widgetBefore), widgetAfter).IsEmpty).IsTrue();
        // /sample

        Optional<PayloadCollection.Fragment?> ServerState(params PayloadItem[] items) =>
            Optional<PayloadCollection.Fragment?>.Present(PayloadCollection.Fragment.From(new PayloadCollection { Items = items.ToList() }));
        var serverBefore = ServerState(new PayloadItem { Id = "a", Name = "A" });
        var serverAfter = ServerState(new PayloadItem { Id = "a", Name = "A2" }, new PayloadItem { Id = "b", Name = "B" });
        var serverChanges = PayloadCollection.ChangeSet.Between(serverBefore, serverAfter);
        var serverOptions = PayloadCollectionOptions();
        var serverTypeInfo = PayloadCollectionTypeInfo(serverOptions);
        var serverJson = JsonSerializer.Serialize(serverChanges.ToPayload(), serverTypeInfo);
        using var serverDocument = JsonDocument.Parse(serverJson);
        var serverItems = serverDocument.RootElement.GetProperty("change").GetProperty("members").GetProperty("items").GetProperty("items");
        var editedItem = serverItems.EnumerateArray().Single(item => item.GetProperty("key").GetString() == "a");
        await Assert
            .That(
                editedItem.EnumerateObject().Select(property => property.Name).SequenceEqual(["key", "kind", "beforeIndex", "afterIndex", "change"])
            )
            .IsTrue();
        await Assert.That(editedItem.GetProperty("kind").GetString()).IsEqualTo("update");
        await Assert.That(editedItem.GetProperty("change").GetProperty("members").TryGetProperty("name", out _)).IsTrue();
        await Assert
            .That(serverItems.EnumerateArray().Single(item => item.GetProperty("key").GetString() == "b").GetProperty("kind").GetString())
            .IsEqualTo("add");
        var serverRestored = JsonSerializer.Deserialize(serverJson, serverTypeInfo)!.ToChangeSet();
        await Assert.That(PayloadCollection.Patch.Between(serverRestored.ToPatch().Apply(serverBefore), serverAfter).IsEmpty).IsTrue();
    }

    [Test]
    public async Task ChangePayloadRedactsBeforeStateWithSourceGen()
    {
        Optional<PayloadSecret.Fragment?> SecretState(string? label, string? token) =>
            Optional<PayloadSecret.Fragment?>.Present(PayloadSecret.Fragment.From(new PayloadSecret { Label = label, Token = token }));

        var before = SecretState("before", "before-token");
        var after = SecretState("after", "after-token");
        var json = JsonSerializer.Serialize(PayloadSecret.ChangeSet.Between(before, after).ToPayload(), PayloadSecret.PayloadJson.Default);

        await Assert.That(json.Contains("\"version\":\"0.1\"")).IsTrue();
        await Assert.That(json.Contains("\"state\":\"redacted\"")).IsTrue();
        await Assert.That(json.Contains("before-token")).IsFalse();

        var restored = JsonSerializer.Deserialize(json, PayloadSecret.PayloadJson.Default)!.ToPatch();
        await Assert.That(PayloadSecret.Patch.Between(restored.Apply(before), after).IsEmpty).IsTrue();

        var incomplete = JsonSerializer.Deserialize(json, PayloadSecret.PayloadJson.Default)!;
        var rejected = false;
        try
        {
            incomplete.ToChangeSet();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task EnumEndpointRoundTripsWithSourceGen()
    {
        // Enum endpoint values travel as canonical numeric wire values.
        Optional<PayloadRoot.Fragment?> EnumState(AotOrderStatus status) =>
            Optional<PayloadRoot.Fragment?>.Present(
                PayloadRoot.Fragment.From(
                    new PayloadRoot
                    {
                        Label = "kept",
                        Child = new PayloadChild
                        {
                            Name = "n",
                            Count = 1,
                            Status = status,
                        },
                    }
                )
            );

        var before = EnumState(AotOrderStatus.Open);
        var after = EnumState(AotOrderStatus.Closed);
        var json = JsonSerializer.Serialize(PayloadRoot.ChangeSet.Between(before, after).ToPayload(), PayloadRoot.PayloadJson.Default);
        var restored = JsonSerializer.Deserialize(json, PayloadRoot.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadRoot.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task KeyedRemovalAndReorderRoundTripWithSourceGen()
    {
        Optional<PayloadCollection.Fragment?> ServerState(params PayloadItem[] items) =>
            Optional<PayloadCollection.Fragment?>.Present(PayloadCollection.Fragment.From(new PayloadCollection { Items = items.ToList() }));

        var before = ServerState(new PayloadItem { Id = "a", Name = "A" }, new PayloadItem { Id = "b", Name = "B" });
        var removed = ServerState(new PayloadItem { Id = "a", Name = "A" });
        var collectionOptions = PayloadCollectionOptions();
        var collectionTypeInfo = PayloadCollectionTypeInfo(collectionOptions);
        var removeJson = JsonSerializer.Serialize(PayloadCollection.ChangeSet.Between(before, removed).ToPayload(), collectionTypeInfo);
        await Assert.That(removeJson.Contains("\"kind\":\"remove\"")).IsTrue();
        var removeRestored = JsonSerializer.Deserialize(removeJson, collectionTypeInfo)!.ToChangeSet();
        await Assert.That(PayloadCollection.Patch.Between(removeRestored.ToPatch().Apply(before), removed).IsEmpty).IsTrue();

        // Scalar removals carry the missing state explicitly, unlike keyed
        // items which omit the unneeded endpoint.
        var missingBefore = Optional<PayloadRoot.Fragment?>.Present(new PayloadRoot.Fragment { Label = Optional<string?>.Present("gone") });
        var missingAfter = Optional<PayloadRoot.Fragment?>.Present(new PayloadRoot.Fragment());
        var missingJson = JsonSerializer.Serialize(
            PayloadRoot.ChangeSet.Between(missingBefore, missingAfter).ToPayload(),
            PayloadRoot.PayloadJson.Default
        );
        await Assert.That(missingJson.Contains("\"state\":\"missing\"")).IsTrue();
        var missingRestored = JsonSerializer.Deserialize(missingJson, PayloadRoot.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadRoot.Patch.Between(missingRestored.ToPatch().Apply(missingBefore), missingAfter).IsEmpty).IsTrue();

        var reordered = ServerState(new PayloadItem { Id = "b", Name = "B" }, new PayloadItem { Id = "a", Name = "A" });
        var reorderJson = JsonSerializer.Serialize(PayloadCollection.ChangeSet.Between(before, reordered).ToPayload(), collectionTypeInfo);
        using (var reorderDocument = JsonDocument.Parse(reorderJson))
        {
            var itemChanges = reorderDocument.RootElement.GetProperty("change").GetProperty("members").GetProperty("items");
            await Assert.That(itemChanges.GetProperty("beforeOrder").GetArrayLength()).IsEqualTo(2);
            await Assert.That(itemChanges.GetProperty("afterOrder")[0].GetString()).IsEqualTo("b");
            await Assert.That(itemChanges.GetProperty("items").EnumerateArray().All(item => item.GetProperty("isReordered").GetBoolean())).IsTrue();
        }
        var reorderRestored = JsonSerializer.Deserialize(reorderJson, collectionTypeInfo)!.ToChangeSet();
        await Assert.That(PayloadCollection.Patch.Between(reorderRestored.ToPatch().Apply(before), reordered).IsEmpty).IsTrue();
    }

    [Test]
    public async Task UnknownKindTokenRejectedWithSourceGen()
    {
        Optional<PayloadCollection.Fragment?> ServerState(params PayloadItem[] items) =>
            Optional<PayloadCollection.Fragment?>.Present(PayloadCollection.Fragment.From(new PayloadCollection { Items = items.ToList() }));

        var before = ServerState(new PayloadItem { Id = "a", Name = "A" });
        var after = ServerState(new PayloadItem { Id = "a", Name = "A2" });
        var kindOptions = PayloadCollectionOptions();
        var kindTypeInfo = PayloadCollectionTypeInfo(kindOptions);
        var json = JsonSerializer.Serialize(PayloadCollection.ChangeSet.Between(before, after).ToPayload(), kindTypeInfo);
        await Assert.That(json.Contains("\"kind\":\"update\"")).IsTrue();

        var rejected = false;
        try
        {
            JsonSerializer.Deserialize(
                json.Replace("\"kind\":\"update\",\"beforeIndex\":0", "\"kind\":\"bogus\",\"beforeIndex\":0", StringComparison.Ordinal),
                kindTypeInfo
            );
        }
        catch (JsonException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ObservableCollectionViewsRemainAotSafe()
    {
        var numbers = new List<int> { 1 };
        var model = new AotIntCollections { Numbers = numbers };
        var notified = 0;
        var session = model.CreateEditSession(onChanged: () => notified++);

        session.Observable.Numbers.Add(2);
        session.Observable.Scores.Add("a", 3);

        await Assert.That(ReferenceEquals(numbers, model.Numbers)).IsTrue();
        await Assert.That(model.Numbers.Count).IsEqualTo(2);
        await Assert.That(model.Scores["a"]).IsEqualTo(3);
        await Assert.That(notified).IsEqualTo(2);
    }

    private static PocoLeaf SampleLeaf(string name, int count, bool withNote = false, string? secret = null) =>
        new()
        {
            Name = name,
            Count = count,
            Nickname = name + "-nick",
            Child = new PocoLeafChild { Code = "c-" + name, Level = count },
            Tags = new List<string> { "t1", "t2" },
            Label = "label-" + name,
            Secret = secret,
            Note = withNote ? "note-" + name : null,
        };

    private static JsonTypeInfo<PocoCatalog.ChangePayload> PocoCatalogTypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<PocoCatalog.ChangePayload>)options.GetTypeInfo(typeof(PocoCatalog.ChangePayload));

    [Test]
    public async Task PocoLeafRoundTripsThroughDefaultResolverAlone()
    {
        Optional<PocoCatalog.Fragment?> CatalogState(PocoLeaf? leaf, string suffix, bool withSecond = true) =>
            Optional<PocoCatalog.Fragment?>.Present(
                PocoCatalog.Fragment.From(
                    new PocoCatalog
                    {
                        Leaf = leaf,
                        SharedA = SampleLeaf("shared", 1),
                        SharedB = new PocoLeaf { Name = "sparse", Count = 0 },
                        Leaves = withSecond
                            ? new List<PocoLeaf> { SampleLeaf("first", 1), SampleLeaf("second", 2, withNote: true) }
                            : new List<PocoLeaf> { SampleLeaf("first", 1) },
                        Lookup = new Dictionary<string, PocoLeaf> { ["k" + suffix] = SampleLeaf("mapped", 3) },
                    }
                )
            );

        // Ignored members never travel: the secret round-trips as null, just
        // like System.Text.Json source generation.
        var before = CatalogState(SampleLeaf("old", 1, secret: "secret-old"), "old", false);
        var after = CatalogState(SampleLeaf("new", 2, secret: "secret-new"), "new");
        var json = JsonSerializer.Serialize(PocoCatalog.ChangeSet.Between(before, after).ToPayload(), PocoCatalog.PayloadJson.Default);

        await Assert.That(json.Contains("\"tag\":\"label-")).IsTrue();
        await Assert.That(!json.Contains("secret-")).IsTrue();
        await Assert.That(!json.Contains("\"Secret\"")).IsTrue();

        // The wire is stable through a second round-trip.
        var restored = JsonSerializer.Deserialize(json, PocoCatalog.PayloadJson.Default)!;
        var stable = JsonSerializer.Serialize(restored, PocoCatalog.PayloadJson.Default);
        await Assert.That(stable).IsEqualTo(json);

        // Before/after state arrives field-complete (custom leaves compare by
        // reference in fragments, so state is asserted per field).
        var applied = restored.ToPatch().Apply(before).Value!;
        var leaf = applied.Leaf.Value!;
        await Assert.That(leaf.Name).IsEqualTo("new");
        await Assert.That(leaf.Count).IsEqualTo(2);
        await Assert.That(leaf.Nickname).IsEqualTo("new-nick");
        await Assert.That(leaf.Child!.Code).IsEqualTo("c-new");
        await Assert.That(leaf.Child.Level).IsEqualTo(2);
        await Assert.That(leaf.Tags.SequenceEqual(new[] { "t1", "t2" })).IsTrue();
        await Assert.That(leaf.Label).IsEqualTo("label-new");
        await Assert.That(leaf.Secret).IsNull();
        await Assert.That(applied.SharedA.Value!.Name).IsEqualTo("shared");
        await Assert.That(applied.SharedB.Value!.Name).IsEqualTo("sparse");
        await Assert.That(applied.Leaves.Value!.Count).IsEqualTo(2);
        await Assert.That(applied.Leaves.Value[1].Note).IsEqualTo("note-second");
        await Assert.That(applied.Lookup.Value!.Count).IsEqualTo(1);
        await Assert.That(applied.Lookup.Value["knew"].Name).IsEqualTo("mapped");
    }

    [Test]
    public async Task PocoLeafWireMatchesSystemTextJsonDefaults()
    {
        var leaf = SampleLeaf("parity", 7, withNote: true);
        var options = new JsonSerializerOptions { TypeInfoResolver = PocoCatalog.PayloadJson.Resolver };
        var generated = JsonSerializer.Serialize(leaf, (JsonTypeInfo<PocoLeaf>)options.GetTypeInfo(typeof(PocoLeaf)));
        var baseline = JsonSerializer.Serialize(leaf, PocoParityContext.Default.PocoLeaf);

        await Assert.That(generated).IsEqualTo(baseline);

        var roundTripped = JsonSerializer.Deserialize(generated, (JsonTypeInfo<PocoLeaf>)options.GetTypeInfo(typeof(PocoLeaf)))!;
        await Assert.That(roundTripped.Name).IsEqualTo("parity");
        await Assert.That(roundTripped.Count).IsEqualTo(7);
        await Assert.That(roundTripped.Child!.Code).IsEqualTo("c-parity");
        await Assert.That(roundTripped.Tags.SequenceEqual(new[] { "t1", "t2" })).IsTrue();
        await Assert.That(roundTripped.Label).IsEqualTo("label-parity");
        await Assert.That(roundTripped.Secret).IsNull();
        await Assert.That(roundTripped.Note).IsEqualTo("note-parity");
    }

    [Test]
    public async Task PocoKeyedAddRemoveEditRoundTripsWithoutLeafContext()
    {
        Optional<PocoKeyed.Fragment?> KeyedState(params PocoItem[] items) =>
            Optional<PocoKeyed.Fragment?>.Present(PocoKeyed.Fragment.From(new PocoKeyed { Items = items.ToList() }));

        var before = KeyedState(
            new PocoItem
            {
                Id = "a",
                Name = "A",
                Detail = SampleLeaf("detail-a", 1),
            },
            new PocoItem { Id = "b", Name = "B" }
        );
        var after = KeyedState(
            new PocoItem
            {
                Id = "a",
                Name = "A2",
                Detail = SampleLeaf("detail-a", 2),
            },
            new PocoItem { Id = "c", Name = "C" }
        );
        var json = JsonSerializer.Serialize(PocoKeyed.ChangeSet.Between(before, after).ToPayload(), PocoKeyed.PayloadJson.Default);

        await Assert.That(json.Contains("\"kind\":\"add\"")).IsTrue();
        await Assert.That(json.Contains("\"kind\":\"remove\"")).IsTrue();
        await Assert.That(json.Contains("\"kind\":\"update\"")).IsTrue();
        await Assert.That(json.Contains("\"Display\":\"c:C\"")).IsTrue();
        await Assert.That(json.Contains("\"tag\":\"label-detail-a\"")).IsTrue();

        var restored = JsonSerializer.Deserialize(json, PocoKeyed.PayloadJson.Default)!;
        var stable = JsonSerializer.Serialize(restored, PocoKeyed.PayloadJson.Default);
        await Assert.That(stable).IsEqualTo(json);

        var applied = restored.ToPatch().Apply(before).Value!;
        await Assert.That(applied.Items.Value!.Count).IsEqualTo(2);
        await Assert.That(applied.Items.Value[0].Id).IsEqualTo("a");
        await Assert.That(applied.Items.Value[0].Name).IsEqualTo("A2");
        await Assert.That(applied.Items.Value[0].Detail!.Count).IsEqualTo(2);
        await Assert.That(applied.Items.Value[0].Detail!.Child!.Level).IsEqualTo(2);
        await Assert.That(applied.Items.Value[0].Display).IsEqualTo("a:A2");
        await Assert.That(applied.Items.Value[1].Id).IsEqualTo("c");
        await Assert.That(applied.Items.Value[1].Detail).IsNull();
        await Assert.That(applied.Items.Value[1].Display).IsEqualTo("c:C");
    }

    [Test]
    public async Task PocoRootSnapshotRoundTripsThroughGeneratedCodec()
    {
        var fragment = PocoCatalog.Fragment.From(
            new PocoCatalog
            {
                Leaf = SampleLeaf("root", 9),
                Leaves = new List<PocoLeaf> { SampleLeaf("root-item", 1) },
                Lookup = new Dictionary<string, PocoLeaf> { ["rk"] = SampleLeaf("root-mapped", 2) },
            }
        );
        var before = Optional<PocoCatalog.Fragment?>.Missing;
        var after = Optional<PocoCatalog.Fragment?>.Present(fragment);
        var payload = PocoCatalog.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload, PocoCatalog.PayloadJson.Default);

        await Assert.That(json.Contains("\"tag\":\"label-root\"")).IsTrue();

        var restored = JsonSerializer.Deserialize(json, PocoCatalog.PayloadJson.Default)!;
        var applied = restored.ToChangeSet().ToPatch().Apply(before);
        var restoredFragment = applied.Value!;
        await Assert.That(restoredFragment.Leaf.Value!.Name).IsEqualTo("root");
        await Assert.That(restoredFragment.Leaf.Value!.Child!.Level).IsEqualTo(9);
        await Assert.That(restoredFragment.Leaves.Value!.Count).IsEqualTo(1);
        await Assert.That(restoredFragment.Leaves.Value[0].Label).IsEqualTo("label-root-item");
        await Assert.That(restoredFragment.Lookup.Value!["rk"].Count).IsEqualTo(2);

        var stable = JsonSerializer.Serialize(restored, PocoCatalog.PayloadJson.Default);
        await Assert.That(stable).IsEqualTo(json);
    }

    [Test]
    public async Task PocoNamingPolicyAndCaseInsensitiveRoundTrip()
    {
        Optional<PocoCatalog.Fragment?> CatalogState(string name) =>
            Optional<PocoCatalog.Fragment?>.Present(PocoCatalog.Fragment.From(new PocoCatalog { Leaf = SampleLeaf(name, 4) }));

        var before = CatalogState("old");
        var after = CatalogState("new");
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = PocoCatalog.PayloadJson.Resolver,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        var typeInfo = PocoCatalogTypeInfo(options);
        var json = JsonSerializer.Serialize(PocoCatalog.ChangeSet.Between(before, after).ToPayload(), typeInfo);

        await Assert.That(json.Contains("\"nickname\"")).IsTrue();
        await Assert.That(json.Contains("\"tag\":\"label-new\"")).IsTrue();

        var restored = JsonSerializer.Deserialize(json, typeInfo)!;
        var stable = JsonSerializer.Serialize(restored, typeInfo);
        await Assert.That(stable).IsEqualTo(json);

        var applied = restored.ToPatch().Apply(before).Value!;
        await Assert.That(applied.Leaf.Value!.Name).IsEqualTo("new");
        await Assert.That(applied.Leaf.Value!.Nickname).IsEqualTo("new-nick");
        await Assert.That(applied.Leaf.Value!.Child!.Level).IsEqualTo(4);

        var upper = JsonSerializer.Deserialize(json.Replace("\"nickname\"", "\"NICKNAME\""), typeInfo)!;
        var upperApplied = upper.ToPatch().Apply(before).Value!;
        await Assert.That(upperApplied.Leaf.Value!.Nickname).IsEqualTo("new-nick");
    }

    private sealed class PocoLeafMarkerConverter : JsonConverter<PocoLeaf>
    {
        public override PocoLeaf? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A marker must be an object.");
            }

            string? marker = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Malformed marker.");
                }

                if (reader.ValueTextEquals("marker"))
                {
                    if (!reader.Read())
                    {
                        throw new JsonException("Malformed marker.");
                    }

                    marker = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                }
                else
                {
                    if (!reader.Read())
                    {
                        throw new JsonException("Malformed marker.");
                    }

                    reader.Skip();
                }
            }

            return new PocoLeaf { Name = marker ?? string.Empty };
        }

        public override void Write(Utf8JsonWriter writer, PocoLeaf? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("marker", value.Name);
            writer.WriteEndObject();
        }
    }

    [Test]
    public async Task PocoCustomConverterTakesPrecedence()
    {
        Optional<PocoCatalog.Fragment?> CatalogState(string name) =>
            Optional<PocoCatalog.Fragment?>.Present(PocoCatalog.Fragment.From(new PocoCatalog { Leaf = SampleLeaf(name, 5) }));

        var before = CatalogState("old");
        var after = CatalogState("new");
        var options = new JsonSerializerOptions { TypeInfoResolver = PocoCatalog.PayloadJson.Resolver };
        options.Converters.Add(new PocoLeafMarkerConverter());
        var typeInfo = PocoCatalogTypeInfo(options);
        var json = JsonSerializer.Serialize(PocoCatalog.ChangeSet.Between(before, after).ToPayload(), typeInfo);

        await Assert.That(json.Contains("\"marker\":\"new\"")).IsTrue();
        await Assert.That(!json.Contains("\"Count\"")).IsTrue();

        var restored = JsonSerializer.Deserialize(json, typeInfo)!.ToChangeSet();
        var applied = restored.ToPatch().Apply(before);
        await Assert.That(applied.Value!.Leaf.Value!.Name).IsEqualTo("new");
    }

    [Test]
    public async Task PocoNullableStructRoundTripsThroughDefaultResolverAlone()
    {
        Optional<PocoWaypoint.Fragment?> WaypointState(PocoPoint? location, params PocoPoint[] trail) =>
            Optional<PocoWaypoint.Fragment?>.Present(PocoWaypoint.Fragment.From(new PocoWaypoint { Location = location, Trail = trail.ToList() }));

        var before = WaypointState(null);
        var after = WaypointState(new PocoPoint { X = 1, Y = 2 }, new PocoPoint { X = 3, Y = 4 });
        var json = JsonSerializer.Serialize(PocoWaypoint.ChangeSet.Between(before, after).ToPayload(), PocoWaypoint.PayloadJson.Default);

        var restored = JsonSerializer.Deserialize(json, PocoWaypoint.PayloadJson.Default)!;
        var stable = JsonSerializer.Serialize(restored, PocoWaypoint.PayloadJson.Default);
        await Assert.That(stable).IsEqualTo(json);

        var applied = restored.ToPatch().Apply(before).Value!;
        await Assert.That(applied.Location.Value?.X).IsEqualTo(1);
        await Assert.That(applied.Location.Value?.Y).IsEqualTo(2);
        await Assert.That(applied.Trail.Value!.Count).IsEqualTo(1);
        await Assert.That(applied.Trail.Value[0].X).IsEqualTo(3);
        await Assert.That(applied.Trail.Value[0].Y).IsEqualTo(4);
    }

    [Test]
    public async Task UnsupportedPolymorphicMemberReportsActionableError()
    {
        Optional<PocoPolyHolder.Fragment?> PolyState(object? value) =>
            Optional<PocoPolyHolder.Fragment?>.Present(PocoPolyHolder.Fragment.From(new PocoPolyHolder { Anything = value }));

        // Object-typed members keep the standard System.Text.Json failure
        // pointing at source-generated registration; a combined resolver or a
        // custom converter remains the opt-in for such shapes.
        var rejected = false;
        var message = string.Empty;
        try
        {
            JsonSerializer.Serialize(
                PocoPolyHolder.ChangeSet.Between(PolyState("a"), PolyState("b")).ToPayload(),
                PocoPolyHolder.PayloadJson.Default
            );
        }
        catch (NotSupportedException exception)
        {
            rejected = true;
            message = exception.Message;
        }

        await Assert.That(rejected).IsTrue();
        await Assert.That(message.Contains("JsonSerializableAttribute")).IsTrue();
    }
}
