using Microsoft.AspNetCore.Components;

namespace SparseTypes.Blazor.Tests;

public sealed class SparseEditSessionReplacementTests
{
    [Test]
    public async Task ReplacingSessionDoesNotRenderRemovedRowFieldsAgainstNewModel()
    {
        var original = new OrderDto
        {
            Lines = [new OrderLine { Sku = "a", Quantity = 1 }, new OrderLine { Sku = "b", Quantity = 2 }, new OrderLine { Sku = "c", Quantity = 3 }],
        }.CreateEditSession();
        var replacement = new OrderDto { Lines = [new OrderLine { Sku = "a", Quantity = 10 }] }.CreateEditSession();
        var values = new Dictionary<string, int>();
        SparseCollection<OrderDto, OrderLine>? collection = null;

        RenderFragment Render(OrderDto.EditSession session) =>
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    builder =>
                    {
                        builder.OpenComponent<SparseCollection<OrderDto, OrderLine>>(0);
                        builder.AddAttribute(1, "Path", edit.Path.Lines.UntypedPath);
                        builder.AddAttribute(
                            2,
                            "RowTemplate",
                            (RenderFragment<SparseCollectionRow<OrderDto, OrderLine>>)(
                                row => Headless.Field(row.Select(line => line.Quantity), field => values[row.Item.Sku] = field.Value)
                            )
                        );
                        builder.AddComponentReferenceCapture(3, component => collection = (SparseCollection<OrderDto, OrderLine>)component);
                        builder.CloseComponent();
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(Render(original));
        values.Count.ShouldBe(3);
        var originalCollection = collection.ShouldNotBeNull();
        await tree.ShowAsync(Render(original));
        collection.ShouldBeSameAs(originalCollection);
        values.Clear();

        var failure = await tree.TryShowAsync(Render(replacement));

        failure.ShouldBeNull();
        collection.ShouldNotBeSameAs(originalCollection);
        values.Count.ShouldBe(1);
        values["a"].ShouldBe(10);
        await tree.DispatchAsync(() => replacement.Observable.Lines[0].Quantity = 20);
        await tree.FlushAsync();
        values["a"].ShouldBe(20);
    }
}
