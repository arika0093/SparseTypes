using Microsoft.AspNetCore.Components;

namespace SparseTypes.Blazor;

/// <summary>Opens a retained child session by typed path and scopes its content with <see cref="SparseEdit{TModel, TPaths}"/>.</summary>
/// <typeparam name="TParent">The model owned by the parent session scope.</typeparam>
/// <typeparam name="TChild">The child model addressed by <see cref="Path"/>.</typeparam>
/// <typeparam name="TChildPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TConflict">The generated structured conflict type for the parent session.</typeparam>
/// <typeparam name="TChildSession">The generated child edit-session type.</typeparam>
public partial class SparseChildEdit<TParent, TChild, TChildPaths, TConflict, TChildSession> : ComponentBase
    where TParent : class
    where TChild : class
    where TChildPaths : ISparseTypedPath<TChild, TChild>
    where TConflict : class
    where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
{
    private SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>? _context;
    private ISparseEditSession<TParent>? _boundParentSession;
    private SparseSessionScopeContext<TParent>? _boundScopeContext;
    private SparsePath _boundPath;
    private bool _hasBoundPath;

    /// <summary>Gets the parent session cascaded by the owning session scope.</summary>
    [CascadingParameter]
    public ISparseEditSession<TParent>? ParentSession { get; set; }

    /// <summary>Gets the child registry cascaded by the owning session scope.</summary>
    [CascadingParameter]
    public SparseSessionScopeContext<TParent>? ScopeContext { get; set; }

    /// <summary>Gets or sets the typed path to the child model.</summary>
    [Parameter]
    public ISparseChildEditPath<TParent, TChild, TChildPaths, TChildSession>? Path { get; set; }

    /// <summary>Gets or sets the content rendered in the child edit scope.</summary>
    [Parameter]
    public RenderFragment<SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>>? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var parent =
            ParentSession ?? throw new InvalidOperationException("SparseChildEdit must be placed inside a SparseSessionScope for its parent model.");
        var scope =
            ScopeContext ?? throw new InvalidOperationException("SparseChildEdit requires the ScopeContext from its owning SparseSessionScope.");
        var path = Path ?? throw new InvalidOperationException("SparseChildEdit requires a child Path.");
        if (!scope.OwnsSession(parent))
        {
            _context = null;
            return;
        }

        var untypedPath = path.UntypedPath;
        if (
            !ReferenceEquals(_boundParentSession, parent)
            || !ReferenceEquals(_boundScopeContext, scope)
            || !_hasBoundPath
            || !_boundPath.Equals(untypedPath)
            || _context is null
            || !scope.IsChildSessionCurrent(untypedPath, _context)
        )
        {
            if (parent is not ISparseEditSessionWithChildren<TParent, TConflict> parentWithChildren)
            {
                throw new InvalidOperationException($"The parent session type '{parent.GetType()}' does not support child sessions.");
            }

            if (!scope.TryOpenChildSession(parentWithChildren, path, out _context))
            {
                _context = null;
                _boundParentSession = parent;
                _boundScopeContext = scope;
                _boundPath = untypedPath;
                _hasBoundPath = true;
                return;
            }

            _boundParentSession = parent;
            _boundScopeContext = scope;
            _boundPath = untypedPath;
            _hasBoundPath = true;
        }
    }

    /// <summary>Resolves the live child context without ever reading a disposed one.</summary>
    /// <remarks>Shares the scope check with <see cref="OnParametersSet"/> but reports transient states (teardown, replaced target, mid-disposal scope) as null instead of throwing; the rebind render that follows fills the subtree back in. Opening here is side-effect free for rendering: it only registers the retained child, never notifies.</remarks>
    /// <returns>The current context, or null when the subtree must stay blank for this batch.</returns>
    private SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>? TryResolveCurrentContext()
    {
        var scope = ScopeContext;
        var parent = ParentSession;
        var path = Path;
        if (scope is null || parent is null || path is null || !scope.OwnsSession(parent))
        {
            return null;
        }

        if (parent is not ISparseEditSessionWithChildren<TParent, TConflict> parentWithChildren)
        {
            return null;
        }

        try
        {
            if (!scope.TryOpenChildSession(parentWithChildren, path, out var resolved) || resolved is null || resolved.IsDisposed)
            {
                return null;
            }

            _context = resolved;
            _boundParentSession = parent;
            _boundScopeContext = scope;
            _boundPath = path.UntypedPath;
            _hasBoundPath = true;
            return resolved;
        }
        catch (ObjectDisposedException)
        {
            // The scope is mid-disposal (its entries are being released while
            // its own disposed flag is already set). The teardown or rebind
            // render that follows owns the subtree from here.
            return null;
        }
    }
}
