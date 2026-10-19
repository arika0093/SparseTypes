using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PackageSparse.Blazor.Consumer;
using SparseTypes;
using SparseTypes.Blazor;

internal static class BlazorApiSamples
{
    public static async Task RunAsync()
    {
        await BindFieldAsync();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            foreach (var passThrough in new[] { false, true })
            {
                var parameters = ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        [nameof(BlazorApiReference.PassThrough)] = passThrough,
                    }
                );
                var rendered = await renderer.RenderComponentAsync<BlazorApiReference>(parameters);
                var html = rendered.ToHtmlString();
                DocsCheck.Require(
                    html.Contains("ORD-1", StringComparison.Ordinal),
                    "order input renders"
                );
                DocsCheck.Require(
                    html.Contains("SKU-1", StringComparison.Ordinal),
                    "collection row renders"
                );
                DocsCheck.Require(
                    html.Contains(
                        passThrough ? "Customer clean" : "Customer draft clean",
                        StringComparison.Ordinal
                    ),
                    "child template renders in its selected mode"
                );
            }
        });
    }

    private static async Task BindFieldAsync()
    {
        // sample: blazor-reference-binding
        var order = new BlazorApiOrder { Number = "ORD-1" };
        var session = order.CreateEditSession();
        var editContext = session.CreateEditContext();
        var history = session.EnableHistory();
        var number = session.Bind(BlazorApiOrder.SparsePath.Number, editContext);

        await number.SetAsync("ORD-2");
        var changed = session.FieldState(BlazorApiOrder.SparsePath.Number).IsChanged;

        history.Undo();
        var restored = number.Value;
        history.Redo();
        var reapplied = number.Value;
        // /sample

        DocsCheck.Require(changed, "binding edit changes semantic field state");
        DocsCheck.Require(editContext.IsModified(), "binding edit notifies the EditContext");
        DocsCheck.Require(restored == "ORD-1", "Undo restores the baseline value");
        DocsCheck.Require(reapplied == "ORD-2", "Redo restores the edited value");
        session.DisableHistory();
    }
}

// sample: blazor-reference-models
[SparseModel]
public partial class BlazorApiOrder
{
    [Required]
    public string Number { get; set; } = string.Empty;
    public BlazorApiCustomer Customer { get; set; } = new();
    public List<BlazorApiLine> Lines { get; set; } = new();
}

[SparseModel]
public partial class BlazorApiCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class BlazorApiLine
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
// /sample
