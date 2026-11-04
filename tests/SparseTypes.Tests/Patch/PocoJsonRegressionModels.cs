using System.Text.Json.Serialization;

namespace SparseTypes.Tests;

public sealed class PocoJsonRegressionDefaultsLeaf
{
    public int Count { get; set; } = 42;

    public string Label { get; set; } = "unset";

    public int Fixed { get; init; } = 7;

    public required string Name { get; set; }
}

public sealed class PocoJsonRegressionBoundLeaf
{
    public PocoJsonRegressionBoundLeaf(int size = 5)
    {
        Size = size;
    }

    public int Size { get; set; }

    public int Extra { get; set; } = 9;
}

public sealed class PocoJsonRegressionIgnoreLeaf
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int ForcedZero { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int NullOnlyZero { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? DefaultOnly { get; set; }

    public int Plain { get; set; }

    public string? PlainText { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NullOnlyText { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ForcedNull { get; set; }
}

public sealed class PocoJsonRegressionInner
{
    public int Value { get; set; }
}

public sealed class PocoJsonRegressionOuter
{
    public PocoJsonRegressionInner? Reference { get; set; }

    public PocoJsonRegressionInnerStruct Struct { get; set; }

    public PocoJsonRegressionInnerStruct? MaybeStruct { get; set; }
}

public struct PocoJsonRegressionInnerStruct
{
    public int Value { get; set; }
}

public sealed class PocoJsonRegressionRequiredBound
{
    public PocoJsonRegressionRequiredBound(string name)
    {
        Name = name;
    }

    public required string Name { get; set; }

    public int Other { get; set; }
}

[SparseModel]
public partial class PocoJsonRegressionHolder
{
    [SparseMerge(MergeMode.Replace)]
    [SparseCloneReferenceSafe]
    public PocoJsonRegressionDefaultsLeaf? Defaults { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public PocoJsonRegressionBoundLeaf? Bound { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public PocoJsonRegressionIgnoreLeaf? Ignore { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public PocoJsonRegressionOuter? Outer { get; set; }

    [SparseMerge(MergeMode.Replace)]
    [SparseCloneReferenceSafe]
    public PocoJsonRegressionRequiredBound? RequiredBound { get; set; }
}
