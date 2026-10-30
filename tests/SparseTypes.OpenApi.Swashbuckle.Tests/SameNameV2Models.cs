namespace SparseTypes.OpenApi.Swashbuckle.Tests.SameName.V2;

/// <summary>A model sharing its short class name with its V1 counterpart.</summary>
[SparseModel]
public partial class Note
{
    /// <summary>Gets or sets the note text.</summary>
    public string? Text { get; set; }
}
