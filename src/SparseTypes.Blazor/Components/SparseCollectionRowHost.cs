using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

internal sealed class SparseCollectionRowHost : ComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);

    // Razor discovers public components only; keep this keyed host internal.
    internal static RenderFragment Render(Guid key, RenderFragment content) =>
        builder =>
        {
            builder.OpenComponent<SparseCollectionRowHost>(0);
            builder.SetKey(key);
            builder.AddAttribute(1, nameof(ChildContent), content);
            builder.CloseComponent();
        };
}
