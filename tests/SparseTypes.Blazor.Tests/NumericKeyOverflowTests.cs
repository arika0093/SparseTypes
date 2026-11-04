using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor.Tests;

[SparseModel]
public partial class NarrowKeyBook
{
    public Dictionary<byte, OrderCustomer> ByByte { get; set; } = new();

    public Dictionary<uint, OrderCustomer> ByUnsigned { get; set; } = new();

    public Dictionary<short, OrderCustomer> ByShort { get; set; } = new();
}

public sealed class NumericKeyOverflowTests
{
    private static NarrowKeyBook Book() =>
        new()
        {
            ByByte = { [7] = new OrderCustomer { Name = "Ada" } },
            ByUnsigned = { [7] = new OrderCustomer { Name = "Ada" } },
            ByShort = { [7] = new OrderCustomer { Name = "Ada" } },
        };

    [Test]
    [Arguments("ByByte[\"300\"].Name")]
    [Arguments("ByByte[256].Name")]
    [Arguments("ByShort[40000].Name")]
    [Arguments("ByUnsigned[\"-1\"].Name")]
    [Arguments("ByUnsigned[-1].Name")]
    public void GeneratedSessionRejectsOutOfRangeKeysAsInvalidPaths(string text)
    {
        var session = Book().CreateEditSession();
        var path = SparsePath.Parse<NarrowKeyBook>(text);

        Should.Throw<ArgumentException>(() => session.Field(path));
    }

    [Test]
    [Arguments("ByByte[\"300\"].Name")]
    [Arguments("ByShort[40000].Name")]
    [Arguments("ByUnsigned[\"-1\"].Name")]
    public void ReflectionResolutionFallsBackToNearestField(string text)
    {
        var model = Book();
        var path = SparsePath.Parse<NarrowKeyBook>(text);

        var resolution = SparseFieldPathResolver.ResolveNearest(model, path);

        resolution.Resolved.ShouldBeFalse();
        Should.Throw<ArgumentException>(() => SparseFieldPathResolver.Resolve(model, path));
    }

    [Test]
    public void InRangeKeysStillResolve()
    {
        var model = Book();
        var path = SparsePath.Parse<NarrowKeyBook>("ByByte[7].Name");

        SparseFieldPathResolver.Resolve(model, path).Model.ShouldBeSameAs(model.ByByte[7]);
    }
}
