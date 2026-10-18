using Microsoft.JSInterop;

namespace HawalaExchange.Web.Helpers;

/// <summary>Expected cancellation of optional browser UI work is not a circuit failure.
/// Never use these methods to acknowledge persistence, downloads, or successful financial actions.</summary>
public static class BrowserInteropExtensions
{
    public static async ValueTask<bool> TryInvokeUiVoidAsync(this IJSRuntime browser, string identifier, params object?[]? args)
    {
        try { await browser.InvokeVoidAsync(identifier, args); return true; }
        catch (JSDisconnectedException) { return false; }
        catch (OperationCanceledException) { return false; }
        // Real script errors remain observable. No generic catch and no automatic replay.
    }

    public static async ValueTask<bool> ConfirmOrCancelAsync(this IJSRuntime browser, string message)
    {
        try { return await browser.InvokeAsync<bool>("confirm", message); }
        catch (JSDisconnectedException) { return false; }
        catch (OperationCanceledException) { return false; } // No confirmation means no permission to proceed.
    }

    public static async ValueTask<bool> TryInvokeUiBooleanAsync(this IJSRuntime browser, string identifier, params object?[]? args)
    {
        try { return await browser.InvokeAsync<bool>(identifier, args); }
        catch (JSDisconnectedException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    public static async ValueTask<bool> TryDisposeBrowserModuleAsync(this IJSObjectReference module)
    {
        try { await module.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); return true; }
        catch (JSDisconnectedException) { return false; }
        catch (OperationCanceledException) { return false; }
        catch (TimeoutException) { return false; }
    }
}
