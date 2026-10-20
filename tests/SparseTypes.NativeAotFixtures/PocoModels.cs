using System.Text.Json.Serialization;

namespace SparseTypes.NativeAotFixtures;

// Ordinary custom leaf object: no SparseModel, no partial, no context registration.
public class PocoLeafChild
{
    public string Code { get; set; } = string.Empty;

    public int Level { get; set; }
}

public class PocoLeaf
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    public string? Nickname { get; set; }

    public PocoLeafChild? Child { get; set; }

    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("tag")]
    public string? Label { get; set; }

    [JsonIgnore]
    public string? Secret { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }
}

[SparseModel]
public partial class PocoCatalog
{
    [SparseMerge(MergeMode.Replace)]
    public PocoLeaf? Leaf { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public PocoLeaf? SharedA { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public PocoLeaf? SharedB { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public List<PocoLeaf> Leaves { get; set; } = new();

    [SparseMerge(MergeMode.Replace)]
    public Dictionary<string, PocoLeaf> Lookup { get; set; } = new();
}

[SparseModel]
public partial class PocoItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [SparseMerge(MergeMode.Replace)]
    public PocoLeaf? Detail { get; set; }

    public string Display => Id + ":" + Name;
}

[SparseModel]
public partial class PocoKeyed
{
    public List<PocoItem> Items { get; set; } = new();
}

public struct PocoPoint
{
    public int X { get; set; }

    public int Y { get; set; }
}

[SparseModel]
public partial class PocoWaypoint
{
    public PocoPoint? Location { get; set; }

    public List<PocoPoint> Trail { get; set; } = new();
}

[SparseModel]
public partial class PocoPolyHolder
{
    [SparseCloneReferenceSafe]
    public object? Anything { get; set; }
}
