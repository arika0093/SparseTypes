namespace SparseTypes.Tests.OpenApi;

/// <summary>A payload model carrying a user domain enum.</summary>
[SparseModel]
public partial class OpenApiShipment
{
    /// <summary>Gets or sets the shipment state.</summary>
    public OpenApiShipmentState State { get; set; }
}

/// <summary>User domain enum that must stay an integer in the document.</summary>
public enum OpenApiShipmentState
{
    /// <summary>Draft state.</summary>
    Draft = 0,

    /// <summary>Shipped state.</summary>
    Shipped = 1,

    /// <summary>Delivered state.</summary>
    Delivered = 2,
}

/// <summary>A nested line model for kind-enum coverage.</summary>
[SparseModel]
public partial class OpenApiBasketItem
{
    /// <summary>Gets or sets the persisted identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    /// <summary>Gets or sets the quantity.</summary>
    public int Quantity { get; set; }
}

/// <summary>A parent model holding keyed lines.</summary>
[SparseModel]
public partial class OpenApiBasket
{
    /// <summary>Gets or sets the lines.</summary>
    public List<OpenApiBasketItem> Lines { get; set; } = [];
}
