namespace SparseTypes.OpenApi.AspNetCore.Tests.SameName.V1;

/// <summary>A model sharing its short class name with its V2 counterpart.</summary>
[SparseModel]
public partial class Note
{
    /// <summary>Gets or sets the note text.</summary>
    public string? Text { get; set; }
}
