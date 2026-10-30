using System.Globalization;

namespace SparseTypes.Tests.Comparison;

/// <summary>Locks the C# scalar equality contract that JS codegen mirrors in equalWireValue.</summary>
public sealed class TemporalScalarEqualityTests
{
    [Test]
    public void GuidEqualityIgnoresCase()
    {
        var lower = Guid.Parse("f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", CultureInfo.InvariantCulture);
        var upper = Guid.Parse("F4EF9705-B5E2-41F6-A7AC-1F3B0A8C14DE", CultureInfo.InvariantCulture);
        EqualityComparer<Guid>.Default.Equals(lower, upper).ShouldBeTrue();
        EqualityComparer<Guid>.Default.Equals(lower, Guid.Empty).ShouldBeFalse();
    }

    [Test]
    public void DateTimeOffsetEqualityComparesInstantsNotOffsets()
    {
        var utc = DateTimeOffset.Parse("2024-05-01T12:00:00.0000000Z", CultureInfo.InvariantCulture);
        var plusTwo = DateTimeOffset.Parse("2024-05-01T14:00:00.0000000+02:00", CultureInfo.InvariantCulture);
        EqualityComparer<DateTimeOffset>.Default.Equals(utc, plusTwo).ShouldBeTrue();
        EqualityComparer<DateTimeOffset>.Default.Equals(utc, utc.AddTicks(1)).ShouldBeFalse();
    }

    [Test]
    public void DateTimeEqualityIgnoresKindWhileWireRetainsIt()
    {
        var utc = DateTime.Parse("2024-05-01T12:00:00.0000000Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var unspecified = DateTime.Parse("2024-05-01T12:00:00.0000000", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        utc.Kind.ShouldBe(DateTimeKind.Utc);
        unspecified.Kind.ShouldBe(DateTimeKind.Unspecified);

        // C# value equality compares ticks only; the wire form keeps the kind suffix.
        EqualityComparer<DateTime>.Default.Equals(utc, unspecified).ShouldBeTrue();
        utc.ToString("O", CultureInfo.InvariantCulture).ShouldBe("2024-05-01T12:00:00.0000000Z");
        unspecified.ToString("O", CultureInfo.InvariantCulture).ShouldBe("2024-05-01T12:00:00.0000000");
        EqualityComparer<DateTime>.Default.Equals(utc, utc.AddTicks(1)).ShouldBeFalse();
        EqualityComparer<DateTime>.Default.Equals(utc, utc).ShouldBeTrue();
    }
}
