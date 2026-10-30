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

public enum WideLedgerKind : ulong
{
    None = 0,

    Huge = 9007199254740993,
}

[SparseModel]
public partial class ScalarLedger
{
    public long LedgerId { get; set; }

    public ulong UnsignedLedgerId { get; set; }

    public double Ratio { get; set; }

    public decimal Amount { get; set; }

    public int? MaybeCount { get; set; }

    public ByteKind Kind { get; set; }

    public ShortKind? MaybeKind { get; set; }

    public WideLedgerKind WideKind { get; set; }

    public bool Flag { get; set; }

    public char Grade { get; set; }

    public Guid Token { get; set; }

    public DateTime Moment { get; set; }

    public DateTimeOffset OffsetMoment { get; set; }

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
            OffsetMoment = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(9)),
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

        json.ShouldContain("\"value\":\"123456789012345\"");
        json.ShouldContain("\"value\":6.626");
        json.ShouldContain("\"value\":\"1048576.125\"");
        json.ShouldContain("\"value\":42");
        json.ShouldContain("\"value\":2");
        json.ShouldContain("\"value\":true");
        json.ShouldContain("\"value\":\"a\"");
        json.ShouldContain("\"value\":\"11111111-2222-3333-4444-555555555555\"");
        json.ShouldContain("\"value\":\"2024-05-06T07:08:09.0000000Z\"");
        json.ShouldContain("\"value\":\"2024-05-06T07:08:09.0000000\\u002B09:00\"");
    }

    [Test]
    public void WideScalarsAndTemporalValuesRoundTripWithExactWireStrings()
    {
        var before = new ScalarLedger
        {
            LedgerId = long.MinValue,
            Amount = 0.10m,
            OffsetMoment = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(-5)),
        };
        var after = new ScalarLedger
        {
            LedgerId = long.MaxValue,
            UnsignedLedgerId = ulong.MaxValue,
            Amount = 0.20m,
            WideKind = WideLedgerKind.Huge,
            Moment = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc).AddTicks(1234567),
            OffsetMoment = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(9)).AddTicks(1234567),
        };
        var json = RoundTripPayload(before, after);

        json.ShouldContain("\"value\":\"-9223372036854775808\"");
        json.ShouldContain("\"value\":\"9223372036854775807\"");
        json.ShouldContain("\"value\":\"18446744073709551615\"");
        json.ShouldContain("\"value\":\"9007199254740993\"");
        json.ShouldContain("\"value\":\"0.10\"");
        json.ShouldContain("\"value\":\"0.20\"");
        json.ShouldContain("\"value\":\"2024-05-06T07:08:09.1234567Z\"");
        json.ShouldContain("\"value\":\"2024-05-06T07:08:09.1234567\\u002B09:00\"");
        var restored = PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(json).ToChangeSet();
        restored.LedgerId.Before.Value.ShouldBe(long.MinValue);
        restored.LedgerId.After.Value.ShouldBe(long.MaxValue);
        restored.UnsignedLedgerId.After.Value.ShouldBe(ulong.MaxValue);
        restored.WideKind.After.Value.ShouldBe(WideLedgerKind.Huge);
        restored.Amount.Before.Value.ShouldBe(0.10m);
        restored.Amount.After.Value.ShouldBe(0.20m);
        restored.Moment.After.Value.Ticks.ShouldBe(after.Moment.Ticks);
        restored.OffsetMoment.Before.Value.Offset.ShouldBe(TimeSpan.FromHours(-5));
        restored.OffsetMoment.After.Value.Offset.ShouldBe(TimeSpan.FromHours(9));
    }

    [Test]
    public void DecimalBoundariesAndUnspecifiedDateTimePreserveTheirWireMeaning()
    {
        var before = new ScalarLedger { Amount = decimal.MinValue };
        var after = new ScalarLedger
        {
            Amount = decimal.MaxValue,
            Moment = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified).AddTicks(1234567),
        };
        var json = RoundTripPayload(before, after);

        json.ShouldContain("\"value\":\"-79228162514264337593543950335\"");
        json.ShouldContain("\"value\":\"79228162514264337593543950335\"");
        json.ShouldContain("\"value\":\"2024-05-06T07:08:09.1234567\"");
        var restored = PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(json).ToChangeSet();
        restored.Amount.Before.Value.ShouldBe(decimal.MinValue);
        restored.Amount.After.Value.ShouldBe(decimal.MaxValue);
        restored.Moment.After.Value.Kind.ShouldBe(DateTimeKind.Unspecified);
        restored.Moment.After.Value.Ticks.ShouldBe(after.Moment.Ticks);
    }

    [Test]
    public void DecimalPayloadAcceptsJsonNumbers()
    {
        var json = RoundTripPayload(new ScalarLedger(), new ScalarLedger { Amount = 0.1m });
        var numericJson = json.Replace("\"value\":\"0.1\"", "\"value\":0.1");

        var restored = PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(numericJson).ToChangeSet();
        restored.Amount.After.Value.ShouldBe(0.1m);

        // Exponent-form numbers are accepted when the value fits in System.Decimal.
        var exponentJson = json.Replace("\"value\":\"0.1\"", "\"value\":1e-1");
        PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(exponentJson).ToChangeSet().Amount.After.Value.ShouldBe(0.1m);

        // Out-of-range numbers never convert to a decimal.
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(json.Replace("\"value\":\"0.1\"", "\"value\":1e30}"))
        );
    }

    [Test]
    public void LocalDateTimeIsRejectedByPayloadSerializer()
    {
        var before = Optional<ScalarLedger.Fragment?>.Present(ScalarLedger.Fragment.From(new ScalarLedger()));
        var after = Optional<ScalarLedger.Fragment?>.Present(
            ScalarLedger.Fragment.From(new ScalarLedger { Moment = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Local) })
        );
        var payload = ScalarLedger.ChangeSet.Between(before, after).ToPayload();

        Should.Throw<JsonException>(() => JsonSerializer.Serialize(payload, ScalarLedger.PayloadJson.Default));
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
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(intJson.Replace("\"value\":\"2\"}", "\"value\":\"oops\"}"))
        );
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(intJson.Replace("\"value\":\"2\"}", "\"value\":2.5}"))
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
    public void NonCanonicalPayloadScalarStringsThrowJsonException()
    {
        var before = new ScalarLedger();
        var longJson = RoundTripPayload(before, new ScalarLedger { LedgerId = 2L });
        foreach (var invalid in new[] { "02", "+2", "2e0", "9223372036854775808" })
        {
            var malformed = longJson.Replace("\"value\":\"2\"}", "\"value\":\"" + invalid + "\"}");
            Should.Throw<JsonException>(() => PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(malformed));
        }
        var ulongJson = RoundTripPayload(before, new ScalarLedger { UnsignedLedgerId = 2UL });
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(
                ulongJson.Replace("\"value\":\"2\"}", "\"value\":\"18446744073709551616\"}")
            )
        );
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(ulongJson.Replace("\"value\":\"2\"}", "\"value\":2}"))
        );

        var decimalJson = RoundTripPayload(before, new ScalarLedger { Amount = 0.1m });
        foreach (var invalid in new[] { "1e-1", "+0.1", " 0.1", "00.1" })
        {
            Should.Throw<JsonException>(() =>
                PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(decimalJson.Replace("\"value\":\"0.1\"}", "\"value\":\"" + invalid + "\"}"))
            );
        }

        var guidJson = RoundTripPayload(before, new ScalarLedger { Token = new Guid("11111111-2222-3333-4444-555555555555") });
        Should.Throw<JsonException>(() =>
            PayloadJsonTestSupport.Deserialize<ScalarLedger.ChangePayload>(
                guidJson.Replace("11111111-2222-3333-4444-555555555555", "11111111-2222-3333-4444-55555555555A")
            )
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
        json.ShouldContain("\"value\":\"3\"");

        options.TypeInfoResolver = ScalarLedger.PayloadJson.Resolver;
        var payloadTypeInfo = (JsonTypeInfo<ScalarLedger.ChangePayload>)options.GetTypeInfo(typeof(ScalarLedger.ChangePayload));
        var withOptions = JsonSerializer.Serialize(payload, payloadTypeInfo);

        // The canonical string survives even when numeric handling is relaxed.
        withOptions.ShouldContain("\"value\":\"3\"");
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(withOptions.Replace("\"value\":\"3\"}", "\"value\":3}"), payloadTypeInfo));
    }
}
