using System.Text.Json;
using System.Text.Json.Serialization;

namespace SparseTypes.Tests.Patch;

public enum ByteKind : byte
{
    None = 0,

    Open = 1,

    Closed = 2,
}

public enum ShortKind : short
{
    None = 0,

    Open = 1,

    Closed = 2,
}

[SparseModel]
public partial class ScalarLedger
{
    public long LedgerId { get; set; }

    public double Ratio { get; set; }

    public decimal Amount { get; set; }

    public int? MaybeCount { get; set; }

    public ByteKind Kind { get; set; }

    public ShortKind? MaybeKind { get; set; }

    public bool Flag { get; set; }

    public char Grade { get; set; }

    public Guid Token { get; set; }

    public DateTime Moment { get; set; }

    public List<long> Scores { get; set; } = [];

    public List<int?> MaybeScores { get; set; } = [];

    public Dictionary<string, decimal> Prices { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, int?> MaybePrices { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<long, string> NamesById { get; set; } = [];
}

public sealed class ScalarPayloadJsonTests
{
    private static ScalarLedger Ledger() =>
        new()
        {
            LedgerId = 123456789012345L,
            Ratio = 6.626,
            Amount = 1048576.125m,
            MaybeCount = 42,
            Kind = ByteKind.Closed,
            MaybeKind = ShortKind.Open,
            Flag = true,
            Grade = 'a',
            Token = new Guid("11111111-2222-3333-4444-555555555555"),
            Moment = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
            Scores = [1L, -2L],
            MaybeScores = [1, null, 3],
            Prices = new(StringComparer.Ordinal) { ["pi"] = 3.14m },
            MaybePrices = new(StringComparer.Ordinal) { ["a"] = 1, ["b"] = null },
            NamesById = new() { [42L] = "answer" },
        };

    private static string RoundTripPayload(ScalarLedger before, ScalarLedger after)
    {
        var payload = ScalarLedger
            .ChangeSet.Between(
                Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(before)),
                Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(after))
            )
            .ToPayload();
        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(json)!.ToChangeSet();
        ScalarLedger
            .Patch.Between(
                restored
                    .ToPatch()
                    .Apply(
                        Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(before))
                    ),
                Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(after))
            )
            .IsEmpty.ShouldBeTrue();
        return json;
    }

    [Test]
    public void ScalarEndpointsUseCanonicalWireEncoding()
    {
        var json = RoundTripPayload(new ScalarLedger(), Ledger());

        json.ShouldContain("\"value\":123456789012345");
        json.ShouldContain("\"value\":6.626");
        json.ShouldContain("\"value\":1048576.125");
        json.ShouldContain("\"value\":42");
        json.ShouldContain("\"value\":2");
        json.ShouldContain("\"value\":true");
        json.ShouldContain("\"value\":\"a\"");
        json.ShouldContain("\"value\":\"11111111-2222-3333-4444-555555555555\"");
        json.ShouldContain("\"value\":\"2024-05-06T07:08:09");
    }

    [Test]
    public void NullableElementCollectionsPreserveExplicitNull()
    {
        var json = RoundTripPayload(new ScalarLedger(), Ledger());

        json.ShouldContain("\"MaybeScores\"");
        var restored = JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(json)!.ToChangeSet();
        var replayed = restored
            .ToPatch()
            .Apply(
                Optional<ScalarLedger.Fragment?>.Present(
                    ScalarLedger.Fragment.From(new ScalarLedger())
                )
            );
        var scores = replayed.Value!.MaybeScores.Value!;
        scores.Count.ShouldBe(3);
        scores[0].ShouldBe(1);
        scores[1].ShouldBeNull();
        scores[2].ShouldBe(3);

        var prices = replayed.Value!.MaybePrices.Value!;
        prices["a"].ShouldBe(1);
        prices["b"].ShouldBeNull();
        replayed.Value!.NamesById.Value![42L].ShouldBe("answer");
    }

    [Test]
    public void ScalarTypeMismatchesThrowJsonException()
    {
        var before = new ScalarLedger();
        var intAfter = new ScalarLedger { LedgerId = 2L };
        var intJson = RoundTripPayload(before, intAfter);
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(
                intJson.Replace("\"value\":2}", "\"value\":\"oops\"}")
            )
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(
                intJson.Replace("\"value\":2}", "\"value\":2.5}")
            )
        );

        var enumAfter = new ScalarLedger { Kind = ByteKind.Closed };
        var enumJson = RoundTripPayload(before, enumAfter);
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(
                enumJson.Replace("\"value\":2}", "\"value\":\"Closed\"}")
            )
        );

        var charAfter = new ScalarLedger { Grade = 'a' };
        var charJson = RoundTripPayload(before, charAfter);
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(
                charJson.Replace("\"value\":\"a\"}", "\"value\":\"ab\"}")
            )
        );
    }

    [Test]
    public void ScalarWireIgnoresConvertersAndNumberHandling()
    {
        var payload = ScalarLedger
            .ChangeSet.Between(
                Optional<ScalarLedger.Fragment?>.Present(
                    ScalarLedger.Fragment.From(new ScalarLedger())
                ),
                Optional<ScalarLedger.Fragment?>.Present(
                    ScalarLedger.Fragment.From(new ScalarLedger { LedgerId = 3L })
                )
            )
            .ToPayload();
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter<ByteKind>() },
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        var json = JsonSerializer.Serialize(
            payload,
            ScalarLedger.PayloadJson.Default.ChangePayload
        );
        json.ShouldContain("\"value\":3");

        var converter = new ScalarLedger.PayloadJsonConverter();
        string withOptions;
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                converter.Write(writer, payload, options);
            }

            withOptions = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        // The canonical number survives even when a string-enum converter is registered.
        withOptions.ShouldContain("\"value\":3");
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(
                withOptions.Replace("\"value\":3}", "\"value\":\"3\"}")
            )
        );
    }

    [Test]
    public void FragmentConverterMatchesPayloadEncoding()
    {
        var options = new JsonSerializerOptions();
        var fragment = ScalarLedger.Fragment.From(Ledger());

        string json;
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                ScalarLedger.Fragment.JsonConverter.Write(writer, fragment, options);
            }

            json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        json.ShouldContain("\"LedgerId\":123456789012345");
        json.ShouldContain("\"Kind\":2");
        json.ShouldContain("\"MaybeCount\":42");

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        reader.Read().ShouldBeTrue();
        var restored = ScalarLedger.Fragment.JsonConverter.Read(
            ref reader,
            typeof(ScalarLedger.Fragment),
            options
        )!;
        restored.LedgerId.Value.ShouldBe(123456789012345L);
        restored.Kind.Value.ShouldBe(ByteKind.Closed);
        restored.MaybeCount.Value.ShouldBe(42);

        var badBytes = System.Text.Encoding.UTF8.GetBytes(
            """{"LedgerId":"oops","Ratio":0,"Amount":0,"Kind":0,"Flag":false,"Grade":"a","Token":"11111111-2222-3333-4444-555555555555","Moment":"2024-05-06T07:08:09Z","Scores":[],"MaybeScores":[],"Prices":{},"MaybePrices":{},"NamesById":{}}"""
        );
        var badReader = new Utf8JsonReader(badBytes);
        badReader.Read().ShouldBeTrue();
        var badReadThrew = false;
        try
        {
            ScalarLedger.Fragment.JsonConverter.Read(
                ref badReader,
                typeof(ScalarLedger.Fragment),
                options
            );
        }
        catch (JsonException)
        {
            badReadThrew = true;
        }

        badReadThrew.ShouldBeTrue();
    }

    [Test]
    public void BridgeEndpointConverterCoversScalarsWithoutMetadata()
    {
        var longConverter =
            new global::SparseTypes.Generated.ChangePayloadEndpointJsonConverter<long>();
        var options = new JsonSerializerOptions();
        options.Converters.Add(longConverter);

        var endpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<long>>(
            """{"state":"value","value":123456789012345}""",
            options
        )!;
        endpoint.Value.ShouldBe(123456789012345L);
        JsonSerializer.Serialize(endpoint, options).ShouldContain("\"value\":123456789012345");

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadEndpoint<long>>(
                """{"state":"value","value":"oops"}""",
                options
            )
        );

        var enumConverter =
            new global::SparseTypes.Generated.ChangePayloadEndpointJsonConverter<ByteKind>();
        var enumOptions = new JsonSerializerOptions();
        enumOptions.Converters.Add(enumConverter);
        var enumEndpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<ByteKind>>(
            """{"state":"value","value":2}""",
            enumOptions
        )!;
        enumEndpoint.Value.ShouldBe(ByteKind.Closed);
    }
}
