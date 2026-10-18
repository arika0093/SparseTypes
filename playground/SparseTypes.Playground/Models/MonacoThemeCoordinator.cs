using Microsoft.JSInterop;

namespace SparseTypes.Playground.Models;

/// <summary>Page-wide coordinator applying the shared Monaco theme once per effective change.</summary>
/// <remarks>Every mounted Monaco wrapper used to subscribe to <see cref="PlaygroundThemeState.Changed"/> and invoke the same global <c>blazorMonaco.editor.setTheme</c> operation, so one notification cost one interop call per editor. The coordinator owns that shared operation: components keep their construction theme and content lifecycle, and only signal readiness here.</remarks>
public sealed class MonacoThemeCoordinator : IDisposable
{
    private readonly IJSRuntime _js;
    private readonly PlaygroundThemeState _themes;
    private string? _lastApplied;
    private string? _pending;
    private bool _applying;
    private bool _disposed;

    /// <summary>Initializes the coordinator and subscribes to theme changes.</summary>
    /// <param name="js">The runtime used for the shared theme operation.</param>
    /// <param name="themes">The shared playground theme state.</param>
    public MonacoThemeCoordinator(IJSRuntime js, PlaygroundThemeState themes)
    {
        _js = js;
        _themes = themes;
        _themes.Changed += OnThemeChanged;
    }

    /// <summary>Gets the effective Monaco theme name for the current theme state.</summary>
    public string CurrentTheme => _themes.IsDark ? "pg-dark" : "pg-light";

    /// <summary>Signals that a Monaco editor finished initializing.</summary>
    /// <remarks>Retries the latest requested theme when earlier attempts ran before Monaco was ready. Editor construction already uses <see cref="CurrentTheme"/>, so this never re-applies an unchanged theme.</remarks>
    public void NotifyEditorReady() => RequestApply();

    private void OnThemeChanged() => RequestApply();

    private void RequestApply()
    {
        if (_disposed)
        {
            return;
        }

        _pending = CurrentTheme;
        if (_applying || _pending == _lastApplied)
        {
            return;
        }

        _ = ApplyLoopAsync();
    }

    // Applies the latest pending theme until it matches the last applied
    // one, so a newer request arriving mid-flight wins and an older async
    // attempt never overwrites it. A failed attempt retains the pending
    // theme for the next theme change or editor-ready signal instead of
    // retrying: the tail re-request below would otherwise restart the loop
    // synchronously forever while the failure persists.
    private async Task ApplyLoopAsync()
    {
        _applying = true;
        var failed = false;
        try
        {
            string? theme;
            while ((theme = _pending) != _lastApplied)
            {
                if (theme is null)
                {
                    break;
                }

                try
                {
                    await _js.InvokeVoidAsync("blazorMonaco.editor.setTheme", theme);
                }
                catch (Exception exception)
                    when (exception
                        is JSDisconnectedException
                            or JSException
                            or InvalidOperationException
                            or TaskCanceledException
                            or ObjectDisposedException)
                {
                    // Monaco (or the playground theme definitions) is not
                    // ready, JavaScript is unavailable, or teardown raced the
                    // call: retain the request for the next editor instead of
                    // faulting an unobserved task.
                    failed = true;
                    break;
                }

                _lastApplied = theme;
            }
        }
        finally
        {
            _applying = false;
        }

        // A request that landed after the final loop check still needs one
        // pass. A failed attempt never re-requests on its own.
        if (!failed && !_disposed && _pending is not null && _pending != _lastApplied)
        {
            RequestApply();
        }
    }

    /// <summary>Unsubscribes from theme changes.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _themes.Changed -= OnThemeChanged;
    }
}
