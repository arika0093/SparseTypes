using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>Appearance-free edit root over a path-capable session with Session-only parameters.</summary>
/// <remarks>Declares only <see cref="Session"/> and <see cref="ChildContent"/>.</remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
/// <typeparam name="TPaths">The concrete generated root path builder type.</typeparam>
public sealed partial class SparseEdit<TModel, TPaths> : ComponentBase, IDisposable
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
{
    private ISparseEditSessionWithPaths<TModel, TPaths>? _session;
    private ISparseEditController<TModel>? _controller;
    private SparseEditContext<TModel, TPaths>? _context;
    private INotifyPropertyChanged? _notifier;
    private Action? _unsubscribeHistoryChanged;
    private ISparseEditHistory? _history;
    private bool _disposed;

    /// <summary>Gets or sets the active path-capable edit session.</summary>
    /// <remarks>Replacing the reference unsubscribes the previous session and history.</remarks>
    [Parameter]
    public ISparseEditSessionWithPaths<TModel, TPaths>? Session { get; set; }

    /// <summary>Gets or sets the optional save and validation controller for the session.</summary>
    [Parameter]
    public ISparseEditController<TModel>? Controller { get; set; }

    /// <summary>Gets or sets the content rendered inside the session cascade.</summary>
    [Parameter]
    public RenderFragment<SparseEditContext<TModel, TPaths>>? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (Controller is not null && !ReferenceEquals(Controller.Session, Session))
        {
            throw new InvalidOperationException("The edit controller must own the same session supplied to SparseEdit.");
        }

        if (!ReferenceEquals(_session, Session) || !ReferenceEquals(_controller, Controller))
        {
            Unsubscribe();
            _context?.Dispose();
            _session = Session;
            _controller = Controller;
            if (Session is not null)
            {
                // Created through trusted framework access, so the observable
                // change cache stays intact for later HasChanges reads.
                _context = new SparseEditContext<TModel, TPaths>(Session, Session.Paths, Session.CreateEditContext(), Controller);
                Subscribe(Session);
            }
            else
            {
                _context = null;
            }
        }
        else
        {
            _context?.RefreshStateSnapshotForParameterUpdate();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unsubscribe();
        _context?.Dispose();
        _context = null;
    }

    private void Subscribe(ISparseEditSessionWithPaths<TModel, TPaths> session)
    {
        if (_controller is not null)
        {
            _controller.PropertyChanged += OnSessionChanged;
        }

        if (session is INotifyPropertyChanged notifier)
        {
            _notifier = notifier;
            notifier.PropertyChanged += OnSessionChanged;
        }

        _unsubscribeHistoryChanged = SparseSessionInterop.SubscribeHistoryChanged(session, OnHistoryAvailabilityChanged);
        WireHistory(SparseSessionInterop.TryGetHistory(session));
    }

    private void Unsubscribe()
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSessionChanged;
            _notifier = null;
        }

        _unsubscribeHistoryChanged?.Invoke();
        _unsubscribeHistoryChanged = null;
        if (_history is not null)
        {
            // Detach only; the history stays owned by its session and is
            // never disposed here, so remounts observe the retained stacks.
            _history.StateChanged -= OnHistoryStateChanged;
            _history = null;
        }

        if (_controller is not null)
        {
            _controller.PropertyChanged -= OnSessionChanged;
        }
    }

    private void WireHistory(ISparseEditHistory? history)
    {
        if (!ReferenceEquals(_history, history))
        {
            if (_history is not null)
            {
                _history.StateChanged -= OnHistoryStateChanged;
            }

            _history = history;
            if (_history is not null)
            {
                _history.StateChanged += OnHistoryStateChanged;
            }
        }
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        // Session notifications arrive on the editing thread, so the render
        // is always marshalled through the dispatcher.
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnHistoryAvailabilityChanged()
    {
        if (_disposed)
        {
            return;
        }

        // EnableHistory/DisableHistory swaps the owned control; rewire the
        // stack subscription instead of keeping the released instance.
        if (_session is not null)
        {
            WireHistory(SparseSessionInterop.TryGetHistory(_session));
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private void OnHistoryStateChanged()
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(StateHasChanged);
    }
}
