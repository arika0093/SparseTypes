using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
    [Test]
    public void PayloadRoundTripsWithoutSerializerContext()
    {
        var patch = ScalarLedger.Patch.Between(
            Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(new ScalarLedger())),
            Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(Ledger()))
        );
        var payload = ScalarLedger.ChangePayload.FromPatch(patch);
        var json = JsonSerializer.Serialize(payload);

        json.ShouldBe(PayloadJsonTestSupport.Serialize(payload));
        var restored = JsonSerializer.Deserialize<ScalarLedger.ChangePayload>(json)!;
        var replayed = restored.ToPatch().Apply(Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(new ScalarLedger())));
        ScalarLedger.Patch.Between(replayed, Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(Ledger()))).IsEmpty.ShouldBeTrue();
        JsonSerializer.Deserialize<ScalarLedger.ChangePayload>("null").ShouldBeNull();
    }

    [Test]
    public void PayloadWithoutSerializerContextRejectsMalformedEnvelope()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ScalarLedger.ChangePayload>("{}"));
    }

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
        var json = PayloadJsonTestSupport.Serialize(payload);
        var restored = PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(json).ToChangeSet();
        ScalarLedger
            .Patch.Between(
                restored.ToPatch().Apply(Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(before))),
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

        json.ShouldContain("\"maybeScores\"");
        var restored = PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(json).ToChangeSet();
        var replayed = restored.ToPatch().Apply(Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(new ScalarLedger())));
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
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(intJson.Replace("\"value\":2}", "\"value\":\"oops\"}"))
        );
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(intJson.Replace("\"value\":2}", "\"value\":2.5}"))
        );

        var enumAfter = new ScalarLedger { Kind = ByteKind.Closed };
        var enumJson = RoundTripPayload(before, enumAfter);
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(enumJson.Replace("\"value\":2}", "\"value\":\"Closed\"}"))
        );

        var charAfter = new ScalarLedger { Grade = 'a' };
        var charJson = RoundTripPayload(before, charAfter);
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(charJson.Replace("\"value\":\"a\"}", "\"value\":\"ab\"}"))
        );
    }

    [Test]
    public void ScalarWireIgnoresConvertersAndNumberHandling()
    {
        var payload = ScalarLedger
            .ChangeSet.Between(
                Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(new ScalarLedger())),
                Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(new ScalarLedger { LedgerId = 3L }))
            )
            .ToPayload();
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter<ByteKind>() },
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        var json = JsonSerializer.Serialize(payload, ScalarLedger.PayloadJson.Default);
        json.ShouldContain("\"value\":3");

        options.TypeInfoResolver = ScalarLedger.PayloadJson.Resolver;
        var payloadTypeInfo = (JsonTypeInfo<ScalarLedger.ChangePayload>)options.GetTypeInfo(typeof(ScalarLedger.ChangePayload));
        var withOptions = JsonSerializer.Serialize(payload, payloadTypeInfo);

        // The canonical number survives even when a string-enum converter is registered.
        withOptions.ShouldContain("\"value\":3");
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(withOptions.Replace("\"value\":3}", "\"value\":\"3\"}"), payloadTypeInfo));
    }
}
