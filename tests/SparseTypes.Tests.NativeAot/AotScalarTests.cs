using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SparseTypes.NativeAotFixtures;

namespace SparseTypes.Tests.NativeAot;

[JsonSerializable(typeof(PayloadLongKeyItem))]
[JsonSerializable(typeof(PayloadEnumKeyItem))]
internal sealed partial class ScalarKeyLeafContext : JsonSerializerContext { }

public sealed class AotScalarTests
{
    private static PayloadScalars ScalarModel() =>
        new()
        {
            SByteValue = -5,
            ByteValue = 250,
            ShortValue = -32000,
            UShortValue = 60000,
            IntValue = -2000000000,
            UIntValue = 4000000000,
            LongValue = -9000000000000000000L,
            ULongValue = 18000000000000000000UL,
            FloatValue = 1.5f,
            DoubleValue = -123456.789,
            DecimalValue = 79228162514264337593543950335m,
            BoolValue = true,
            CharValue = 'z',
            StringValue = "scalar",
            GuidValue = new Guid("11111111-2222-3333-4444-555555555555"),
            DateTimeValue = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
            DateTimeOffsetValue = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)),
            NullableInt = 7,
            NullableLong = -8000000000000000000L,
            NullableDouble = 2.5,
            NullableDecimal = 123.456m,
            NullableBool = false,
            NullableChar = 'q',
            NullableGuid = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            NullableDateTime = new DateTime(2023, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            NullableOffset = new DateTimeOffset(2023, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5)),
            SByteEnum = ScalarSByte.B,
            ByteEnum = ScalarByte.A,
            ShortEnum = ScalarShort.B,
            UShortEnum = ScalarUShort.A,
            IntEnum = ScalarInt.B,
            UIntEnum = ScalarUInt.A,
            LongEnum = ScalarLong.B,
            ULongEnum = ScalarULong.A,
            Flags = ScalarFlags.A | ScalarFlags.C,
            NullableByteEnum = ScalarByte.B,
        };

    private static PayloadScalarCollections CollectionModel() =>
        new()
        {
            Longs = [1L, -2L, 9000000000000000000L],
            NullableInts = [1, null, 3],
            Doubles = [1.5, -2.25],
            Decimals = [1.1m, 2.2m],
            Ids = [new Guid("11111111-2222-3333-4444-555555555555")],
            ByteEnums = [ScalarByte.A, ScalarByte.B],
            Moments = [new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc)],
            LongByName = new(StringComparer.Ordinal) { ["a"] = 1L, ["b"] = -2L },
            NullableByName = new(StringComparer.Ordinal) { ["a"] = 1, ["b"] = null },
            DecimalByName = new(StringComparer.Ordinal) { ["pi"] = 3.14m },
            ByLongKey = new() { [42L] = "answer", [-7L] = "negative" },
        };

    private static string SerializePayload<T>(T payload, JsonTypeInfo<T> typeInfo) => JsonSerializer.Serialize(payload, typeInfo);

    [Test]
    public async Task MinimalScalarReproducerRoundTripsWithoutApplicationContext()
    {
        var before = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars()));
        var after = Optional<PayloadScalars.Fragment?>.Present(
            PayloadScalars.Fragment.From(
                new PayloadScalars
                {
                    LongValue = 123456789012345L,
                    DoubleValue = 6.626,
                    DecimalValue = 1048576.125m,
                    NullableInt = 42,
                    ByteEnum = ScalarByte.B,
                }
            )
        );

        var json = SerializePayload(PayloadScalars.ChangeSet.Between(before, after).ToPayload(), PayloadScalars.PayloadJson.Default);
        await Assert.That(json.Contains("\"value\":123456789012345")).IsTrue();
        await Assert.That(json.Contains("\"value\":42")).IsTrue();

        var restored = JsonSerializer.Deserialize(json, PayloadScalars.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadScalars.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task AllScalarWidthsAndNullablesRoundTripWithoutApplicationContext()
    {
        var before = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars()));
        var after = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(ScalarModel()));

        var json = SerializePayload(PayloadScalars.ChangeSet.Between(before, after).ToPayload(), PayloadScalars.PayloadJson.Default);
        var restored = JsonSerializer.Deserialize(json, PayloadScalars.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadScalars.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task NullableScalarsPreserveNullVersusMissing()
    {
        var before = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars { NullableInt = 5 }));
        var nulled = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars { NullableInt = null }));
        var removed = Optional<PayloadScalars.Fragment?>.Present(new PayloadScalars.Fragment());

        var nullJson = SerializePayload(PayloadScalars.ChangeSet.Between(before, nulled).ToPayload(), PayloadScalars.PayloadJson.Default);
        await Assert.That(nullJson.Contains("\"state\":\"null\"")).IsTrue();
        var nullRestored = JsonSerializer.Deserialize(nullJson, PayloadScalars.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadScalars.Patch.Between(nullRestored.ToPatch().Apply(before), nulled).IsEmpty).IsTrue();

        var missingJson = SerializePayload(PayloadScalars.ChangeSet.Between(before, removed).ToPayload(), PayloadScalars.PayloadJson.Default);
        await Assert.That(missingJson.Contains("\"state\":\"missing\"")).IsTrue();
        var missingRestored = JsonSerializer.Deserialize(missingJson, PayloadScalars.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadScalars.Patch.Between(missingRestored.ToPatch().Apply(before), removed).IsEmpty).IsTrue();
    }

    [Test]
    public async Task FragmentConverterRoundTripsScalarsWithoutMetadata()
    {
        var options = new JsonSerializerOptions();
        var fragment = PayloadScalars.Fragment.From(ScalarModel());

        string json;
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                PayloadScalars.Fragment.JsonConverter.Write(writer, fragment, options);
            }

            json = Encoding.UTF8.GetString(stream.ToArray());
        }

        await Assert.That(json.Contains("\"LongValue\":-9000000000000000000")).IsTrue();
        await Assert.That(json.Contains("\"ByteEnum\":1")).IsTrue();

        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        reader.Read();
        var restored = PayloadScalars.Fragment.JsonConverter.Read(ref reader, typeof(PayloadScalars.Fragment), options)!;
        await Assert
            .That(
                PayloadScalars
                    .Patch.Between(Optional<PayloadScalars.Fragment?>.Present(restored), Optional<PayloadScalars.Fragment?>.Present(fragment))
                    .IsEmpty
            )
            .IsTrue();
    }

    [Test]
    public async Task FragmentConverterPreservesExplicitNullWithoutMetadata()
    {
        var options = new JsonSerializerOptions();
        var fragment = new PayloadScalars.Fragment
        {
            NullableInt = Optional<int?>.Present(null),
            NullableByteEnum = Optional<ScalarByte?>.Present(null),
        };

        string json;
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                PayloadScalars.Fragment.JsonConverter.Write(writer, fragment, options);
            }

            json = Encoding.UTF8.GetString(stream.ToArray());
        }

        await Assert.That(json.Contains("\"NullableInt\":null")).IsTrue();

        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        reader.Read();
        var restored = PayloadScalars.Fragment.JsonConverter.Read(ref reader, typeof(PayloadScalars.Fragment), options)!;
        await Assert.That(restored.NullableInt.IsPresent).IsTrue();
        await Assert.That(restored.NullableInt.Value is null).IsTrue();
        await Assert.That(restored.NullableByteEnum.IsPresent).IsTrue();
    }

    [Test]
    public async Task ScalarCollectionsAndKeysRoundTripWithoutApplicationContext()
    {
        var before = Optional<PayloadScalarCollections.Fragment?>.Present(PayloadScalarCollections.Fragment.From(new PayloadScalarCollections()));
        var after = Optional<PayloadScalarCollections.Fragment?>.Present(PayloadScalarCollections.Fragment.From(CollectionModel()));

        var json = SerializePayload(
            PayloadScalarCollections.ChangeSet.Between(before, after).ToPayload(),
            PayloadScalarCollections.PayloadJson.Default
        );
        var restored = JsonSerializer.Deserialize(json, PayloadScalarCollections.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadScalarCollections.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task LongKeyedCollectionRoundTripsWithoutApplicationContext()
    {
        Optional<PayloadLongKeyHolder.Fragment?> HolderState(params PayloadLongKeyItem[] items) =>
            Optional<PayloadLongKeyHolder.Fragment?>.Present(PayloadLongKeyHolder.Fragment.From(new PayloadLongKeyHolder { Items = [.. items] }));

        // Edits and reorders carry only keys, indexes, and nested scalar
        // cores: no application context is needed for the keyed paths.
        var before = HolderState(
            new PayloadLongKeyItem
            {
                Id = 7L,
                Name = "a",
                Score = 1.5,
            },
            new PayloadLongKeyItem
            {
                Id = 9000000000000000000L,
                Name = "b",
                Score = 3.5,
            }
        );
        var edited = HolderState(
            new PayloadLongKeyItem
            {
                Id = 7L,
                Name = "a2",
                Score = 2.5,
            },
            new PayloadLongKeyItem
            {
                Id = 9000000000000000000L,
                Name = "b",
                Score = 3.5,
            }
        );

        var editJson = SerializePayload(PayloadLongKeyHolder.ChangeSet.Between(before, edited).ToPayload(), PayloadLongKeyHolder.PayloadJson.Default);
        await Assert.That(editJson.Contains("\"key\":7")).IsTrue();
        await Assert.That(editJson.Contains("\"kind\":\"update\"")).IsTrue();
        var editRestored = JsonSerializer.Deserialize(editJson, PayloadLongKeyHolder.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadLongKeyHolder.Patch.Between(editRestored.ToPatch().Apply(before), edited).IsEmpty).IsTrue();

        var reordered = HolderState(
            new PayloadLongKeyItem
            {
                Id = 9000000000000000000L,
                Name = "b",
                Score = 3.5,
            },
            new PayloadLongKeyItem
            {
                Id = 7L,
                Name = "a2",
                Score = 2.5,
            }
        );
        var reorderJson = SerializePayload(
            PayloadLongKeyHolder.ChangeSet.Between(edited, reordered).ToPayload(),
            PayloadLongKeyHolder.PayloadJson.Default
        );
        await Assert.That(reorderJson.Contains("\"isReordered\":true")).IsTrue();
        var reorderRestored = JsonSerializer.Deserialize(reorderJson, PayloadLongKeyHolder.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadLongKeyHolder.Patch.Between(reorderRestored.ToPatch().Apply(edited), reordered).IsEmpty).IsTrue();
    }

    [Test]
    public async Task EnumKeyedCollectionRoundTripsWithoutApplicationContext()
    {
        Optional<PayloadEnumKeyHolder.Fragment?> HolderState(params PayloadEnumKeyItem[] items) =>
            Optional<PayloadEnumKeyHolder.Fragment?>.Present(PayloadEnumKeyHolder.Fragment.From(new PayloadEnumKeyHolder { Items = [.. items] }));

        var before = HolderState(new PayloadEnumKeyItem { Kind = ScalarByte.A, Name = "a" });
        var after = HolderState(new PayloadEnumKeyItem { Kind = ScalarByte.A, Name = "a2" });

        var json = SerializePayload(PayloadEnumKeyHolder.ChangeSet.Between(before, after).ToPayload(), PayloadEnumKeyHolder.PayloadJson.Default);
        await Assert.That(json.Contains("\"key\":1")).IsTrue();
        var restored = JsonSerializer.Deserialize(json, PayloadEnumKeyHolder.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadEnumKeyHolder.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task KeyedModelValuesResolveThroughLeafContext()
    {
        // Whole-item add/remove endpoints carry ordinary POCO models, so they
        // resolve through a source-generated leaf context. Keys, key orders,
        // and nested edits stay context-free (see the tests above).
        Optional<PayloadLongKeyHolder.Fragment?> HolderState(params PayloadLongKeyItem[] items) =>
            Optional<PayloadLongKeyHolder.Fragment?>.Present(PayloadLongKeyHolder.Fragment.From(new PayloadLongKeyHolder { Items = [.. items] }));

        var before = HolderState(
            new PayloadLongKeyItem
            {
                Id = 7L,
                Name = "a",
                Score = 1.5,
            }
        );
        var after = HolderState(
            new PayloadLongKeyItem
            {
                Id = 7L,
                Name = "a2",
                Score = 2.5,
            },
            new PayloadLongKeyItem
            {
                Id = 9000000000000000000L,
                Name = "b",
                Score = 3.5,
            }
        );

        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(PayloadLongKeyHolder.PayloadJson.Resolver, ScalarKeyLeafContext.Default),
        };
        var typeInfo = (JsonTypeInfo<PayloadLongKeyHolder.ChangePayload>)options.GetTypeInfo(typeof(PayloadLongKeyHolder.ChangePayload));
        var json = JsonSerializer.Serialize(PayloadLongKeyHolder.ChangeSet.Between(before, after).ToPayload(), typeInfo);
        await Assert.That(json.Contains("\"kind\":\"add\"")).IsTrue();
        var restored = JsonSerializer.Deserialize(json, typeInfo)!.ToChangeSet();
        await Assert.That(PayloadLongKeyHolder.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task RootSnapshotRoundTripsScalarsWithoutApplicationContext()
    {
        var after = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(ScalarModel()));

        var json = SerializePayload(
            PayloadScalars.ChangeSet.Between(Optional<PayloadScalars.Fragment?>.Missing, after).ToPayload(),
            PayloadScalars.PayloadJson.Default
        );
        using (var document = JsonDocument.Parse(json))
        {
            var change = document.RootElement.GetProperty("change");
            await Assert.That(change.GetProperty("kind").GetString()).IsEqualTo("replace");
            await Assert.That(change.GetProperty("before").GetProperty("state").GetString()).IsEqualTo("missing");
            await Assert.That(change.GetProperty("after").GetProperty("state").GetString()).IsEqualTo("value");
        }
        var restored = JsonSerializer.Deserialize(json, PayloadScalars.PayloadJson.Default)!.ToChangeSet();
        await Assert.That(PayloadScalars.Patch.Between(restored.ToPatch().Apply(Optional<PayloadScalars.Fragment?>.Missing), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task MalformedScalarTokensAreRejected()
    {
        var before = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars()));

        var intAfter = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars { IntValue = 2 }));
        var intJson = SerializePayload(PayloadScalars.ChangeSet.Between(before, intAfter).ToPayload(), PayloadScalars.PayloadJson.Default);
        await Assert.That(ReadFails(intJson.Replace("\"value\":2}", "\"value\":\"oops\"}"))).IsTrue();
        await Assert.That(ReadFails(intJson.Replace("\"value\":2}", "\"value\":2.5}"))).IsTrue();

        var enumAfter = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars { ByteEnum = ScalarByte.B }));
        var enumJson = SerializePayload(PayloadScalars.ChangeSet.Between(before, enumAfter).ToPayload(), PayloadScalars.PayloadJson.Default);
        await Assert.That(ReadFails(enumJson.Replace("\"value\":2}", "\"value\":\"B\"}"))).IsTrue();

        var charAfter = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars { CharValue = 'a' }));
        var charJson = SerializePayload(PayloadScalars.ChangeSet.Between(before, charAfter).ToPayload(), PayloadScalars.PayloadJson.Default);
        await Assert.That(ReadFails(charJson.Replace("\"value\":\"a\"}", "\"value\":\"ab\"}"))).IsTrue();

        static bool ReadFails(string json)
        {
            try
            {
                JsonSerializer.Deserialize(json, PayloadScalars.PayloadJson.Default);
            }
            catch (JsonException)
            {
                return true;
            }

            return false;
        }
    }

    [Test]
    public async Task ScalarWireIgnoresEnumConverterAndNumberHandling()
    {
        var before = Optional<PayloadScalars.Fragment?>.Present(PayloadScalars.Fragment.From(new PayloadScalars()));
        var after = Optional<PayloadScalars.Fragment?>.Present(
            PayloadScalars.Fragment.From(new PayloadScalars { IntValue = 3, ByteEnum = ScalarByte.B })
        );
        var payload = PayloadScalars.ChangeSet.Between(before, after).ToPayload();
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter<ScalarByte>() },
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            TypeInfoResolver = PayloadScalars.PayloadJson.Resolver,
        };
        var payloadInfo = (JsonTypeInfo<PayloadScalars.ChangePayload>)options.GetTypeInfo(typeof(PayloadScalars.ChangePayload));
        var json = JsonSerializer.Serialize(payload, payloadInfo);

        // Canonical 0.1 encoding wins over supplied converters: the enum
        // still travels as its underlying number.
        await Assert.That(json.Contains("\"value\":2")).IsTrue();

        static bool ReadFails(string candidate, JsonSerializerOptions candidateOptions)
        {
            try
            {
                JsonSerializer.Deserialize(
                    candidate,
                    (JsonTypeInfo<PayloadScalars.ChangePayload>)candidateOptions.GetTypeInfo(typeof(PayloadScalars.ChangePayload))
                );
            }
            catch (JsonException)
            {
                return true;
            }

            return false;
        }

        await Assert.That(ReadFails(json.Replace("\"value\":2}", "\"value\":\"B\"}"), options)).IsTrue();
        await Assert.That(ReadFails(json.Replace("\"value\":3}", "\"value\":\"3\"}"), options)).IsTrue();
    }
}
