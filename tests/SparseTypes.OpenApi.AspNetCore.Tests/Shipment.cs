namespace SparseTypes.OpenApi.AspNetCore.Tests;

/// <summary>A payload model carrying a user domain enum.</summary>
[SparseModel]
public partial class Shipment
{
    /// <summary>Gets or sets the shipment state.</summary>
    public ShipmentState State { get; set; }

    /// <summary>Gets or sets the note.</summary>
    public string? Note { get; set; }
}

/// <summary>User domain enum that must stay an integer in the document.</summary>
public enum ShipmentState
{
    /// <summary>Draft state.</summary>
    Draft = 0,

    /// <summary>Shipped state.</summary>
    Shipped = 1,

    /// <summary>Delivered state.</summary>
    Delivered = 2,
}
