using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace SparseTypes.Blazor;

/// <summary>Connects edit state to Blazor's standard internal and external navigation guards.</summary>
/// <typeparam name="TModel">The generated edit-session model type.</typeparam>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
public sealed partial class SparseNavigationGuard<TModel, TChangeSet> : ComponentBase, IDisposable
    where TModel : class
    where TChangeSet : class
{
    private SparseEditController<TModel, TChangeSet>? _subscribedEditor;
    private bool _disposed;

    /// <summary>Gets or sets the controller whose unsaved state is monitored.</summary>
    [Parameter, EditorRequired]
    public SparseEditController<TModel, TChangeSet> Editor { get; set; } = null!;

    /// <summary>Gets or sets whether to enable the browser's standard external-navigation confirmation.</summary>
    [Parameter]
    public bool EnableExternalConfirmation { get; set; }

    /// <summary>Gets or sets the application callback for an internal navigation attempt.</summary>
    /// <remarks>The callback can call <see cref="LocationChangingContext.PreventNavigation"/>. When unset, navigation proceeds without a library-owned prompt.</remarks>
    [Parameter]
    public Func<LocationChangingContext, Task>? BeforeInternalNavigation { get; set; }

    /// <summary>Gets whether the editor has any state that should block or warn about navigation.</summary>
    public bool HasUnsavedWork =>
        Editor is not null
        && (
            Editor.HasChanges
            || Editor.HasPendingDrafts
            || Editor.IsBusy
            || Editor.SaveState is SparseEditSaveState.OutcomeUnknown or SparseEditSaveState.PersistedButUnreconciled
        );

    /// <summary>Gets whether Blazor should request the browser's standard external-navigation confirmation.</summary>
    public bool ShouldConfirmExternalNavigation => EnableExternalConfirmation && HasUnsavedWork;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (Editor is null)
        {
            throw new InvalidOperationException("SparseNavigationGuard requires an edit controller.");
        }

        if (ReferenceEquals(_subscribedEditor, Editor))
        {
            return;
        }

        if (_subscribedEditor is not null)
        {
            _subscribedEditor.PropertyChanged -= OnEditorChanged;
        }

        _subscribedEditor = Editor;
        _subscribedEditor.PropertyChanged += OnEditorChanged;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribedEditor is not null)
        {
            _subscribedEditor.PropertyChanged -= OnEditorChanged;
        }
    }

    internal async Task HandleInternalNavigation(LocationChangingContext context)
    {
        if (!_disposed && HasUnsavedWork && BeforeInternalNavigation is { } callback)
        {
            await callback(context);
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }
}
