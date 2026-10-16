using Microsoft.AspNetCore.Components.Server.Circuits;

namespace HawalaExchange.Web.Services;

/// <summary>Per-circuit cancellation and notification for non-financial browser UI work.</summary>
public sealed class UiConnectionState : CircuitHandler, IDisposable
{
    private CancellationTokenSource connection = new();
    private CancellationToken connectionToken;
    private bool disposed;
    public UiConnectionState() => connectionToken = connection.Token;
    public bool IsConnected { get; private set; } = true;
    public CancellationToken ConnectionToken => connectionToken;
    public event Action? Changed;

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (disposed || !IsConnected) return Task.CompletedTask;
        IsConnected = false;
        connection.Cancel();
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (disposed || IsConnected) return Task.CompletedTask;
        connection.Dispose();
        connection = new CancellationTokenSource();
        connectionToken = connection.Token;
        IsConnected = true;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        IsConnected = false;
        connection.Cancel();
        connection.Dispose();
        Changed = null;
    }
}
