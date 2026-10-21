namespace SparseTypes.NativeAotFixtures;

// Enum endpoint shapes for every CLR-valid underlying integral type,
// including a flags combination. Payload and fragment converters carry these
// as canonical numeric wire values with no application JsonSerializerContext.
public enum ScalarSByte : sbyte
{
    None = 0,

    A = 1,

    B = 2,
}

public enum ScalarByte : byte
{
    None = 0,

    A = 1,

    B = 2,
}

public enum ScalarShort : short
{
    None = 0,

    A = 1,

    B = 2,
}

public enum ScalarUShort : ushort
{
    None = 0,

    A = 1,

    B = 2,
}

public enum ScalarInt : int
{
    None = 0,

    A = 1,

    B = 2,
}

public enum ScalarUInt : uint
{
    None = 0,

    A = 1,

    B = 2,
}

public enum ScalarLong : long
{
    None = 0,

    A = 1,

    B = 2,
}

public enum ScalarULong : ulong
{
    None = 0,

    A = 1,

    B = 2,
}

[System.Flags]
public enum ScalarFlags : int
{
    None = 0,

    A = 1,

    B = 2,

    C = 4,
}

// Scalar endpoint coverage: every common BCL scalar, nullable value-type
// counterparts, and enums with all underlying types. All of these round-trip
// through the generated payload resolver alone.
[SparseModel]
public partial class PayloadScalars
{
    public sbyte SByteValue { get; set; }

    public byte ByteValue { get; set; }

    public short ShortValue { get; set; }

    public ushort UShortValue { get; set; }

    public int IntValue { get; set; }

    public uint UIntValue { get; set; }

    public long LongValue { get; set; }

    public ulong ULongValue { get; set; }

    public float FloatValue { get; set; }

    public double DoubleValue { get; set; }

    public decimal DecimalValue { get; set; }

    public bool BoolValue { get; set; }

    public char CharValue { get; set; }

    public string? StringValue { get; set; }

    public Guid GuidValue { get; set; }

    public DateTime DateTimeValue { get; set; }

    public DateTimeOffset DateTimeOffsetValue { get; set; }

    public int? NullableInt { get; set; }

    public long? NullableLong { get; set; }

    public double? NullableDouble { get; set; }

    public decimal? NullableDecimal { get; set; }

    public bool? NullableBool { get; set; }

    public char? NullableChar { get; set; }

    public Guid? NullableGuid { get; set; }

    public DateTime? NullableDateTime { get; set; }

    public DateTimeOffset? NullableOffset { get; set; }

    public ScalarSByte SByteEnum { get; set; }

    public ScalarByte ByteEnum { get; set; }

    public ScalarShort ShortEnum { get; set; }

    public ScalarUShort UShortEnum { get; set; }

    public ScalarInt IntEnum { get; set; }

    public ScalarUInt UIntEnum { get; set; }

    public ScalarLong LongEnum { get; set; }

    public ScalarULong ULongEnum { get; set; }

    public ScalarFlags Flags { get; set; }

    public ScalarByte? NullableByteEnum { get; set; }
}

// Scalar collection elements and map keys. The structural container codec is
// generated regardless of element type; covered element and key types need no
// metadata while custom element types keep the metadata fallback.
[SparseModel]
public partial class PayloadScalarCollections
{
    public List<long> Longs { get; set; } = [];

    public List<int?> NullableInts { get; set; } = [];

    public List<double> Doubles { get; set; } = [];

    public List<decimal> Decimals { get; set; } = [];

    public HashSet<Guid> Ids { get; set; } = [];

    public List<ScalarByte> ByteEnums { get; set; } = [];

    public List<DateTime> Moments { get; set; } = [];

    public Dictionary<string, long> LongByName { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, int?> NullableByName { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, decimal> DecimalByName { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<long, string> ByLongKey { get; set; } = [];
}

[SparseModel]
public partial class PayloadLongKeyItem
{
    [SparseKey]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Score { get; set; }
}

[SparseModel]
public partial class PayloadLongKeyHolder
{
    public List<PayloadLongKeyItem> Items { get; set; } = [];
}

[SparseModel]
public partial class PayloadEnumKeyItem
{
    [SparseKey]
    public ScalarByte Kind { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class PayloadEnumKeyHolder
{
    public List<PayloadEnumKeyItem> Items { get; set; } = [];
}
