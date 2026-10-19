using Microsoft.JSInterop;

namespace SparseTypes.Playground.Models;

/// <summary>Waits for the Monaco AMD module before editor components create instances.</summary>
/// <remarks>Blazor can render before Monaco's asynchronous module execution finishes. Creating an editor while <c>window.monaco</c> is undefined fails permanently for that instance, so wrappers await this gate first.</remarks>
internal static class MonacoReadiness
{
    /// <summary>Waits until the playground Monaco setup reports readiness.</summary>
    /// <param name="js">The runtime used to await the playground readiness promise.</param>
    /// <returns>Null when Monaco is ready; otherwise a message describing why editors cannot render.</returns>
    internal static async Task<string?> WaitForMonacoAsync(IJSRuntime js)
    {
        try
        {
            await js.InvokeVoidAsync("pgMonaco.whenReady");
            return null;
        }
        catch (Exception exception)
            when (exception
                    is JSDisconnectedException
                        or JSException
                        or InvalidOperationException
                        or TaskCanceledException
                        or ObjectDisposedException
            )
        {
            // Monaco failed to load, JavaScript is unavailable, or teardown
            // raced the wait: report instead of faulting the render batch.
            return exception.Message;
        }
    }
}
