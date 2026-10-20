using SparseTypes;

namespace SparseTypes.OpenApi.AspNetCore.Tests;

/// <summary>A nested line model with its own payload schemas.</summary>
[SparseModel]
public partial class BasketItem
{
    /// <summary>Gets or sets the persisted identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    /// <summary>Gets or sets the quantity.</summary>
    public int Quantity { get; set; }
}

/// <summary>A parent model holding keyed lines of another payload model.</summary>
[SparseModel]
public partial class Basket
{
    /// <summary>Gets or sets the lines.</summary>
    public List<BasketItem> Lines { get; set; } = [];
}
