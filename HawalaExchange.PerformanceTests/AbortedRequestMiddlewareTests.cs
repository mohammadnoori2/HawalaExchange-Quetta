using HawalaExchange.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HawalaExchange.PerformanceTests;

public sealed class AbortedRequestMiddlewareTests
{
    [Fact]
    public async Task Already_aborted_request_never_reaches_authentication_or_the_endpoint()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var called = false;
        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.False(called);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Cancellation_during_validation_unwinds_request_without_signing_out()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var endpointReached = false;
        await Middleware(async _ =>
        {
            cancellation.Cancel();
            await Task.FromCanceled(cancellation.Token);
            endpointReached = true;
        }).InvokeAsync(context);
        Assert.False(endpointReached);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Independent_timeout_is_not_swallowed_or_treated_as_success()
    {
        var context = new DefaultHttpContext();
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            Middleware(_ => throw new TaskCanceledException("Connection timeout")).InvokeAsync(context));
        Assert.Empty(context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Database_failure_is_not_hidden_even_if_client_also_disconnects()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Middleware(_ =>
        {
            cancellation.Cancel();
            throw new InvalidOperationException("Database unavailable");
        }).InvokeAsync(context));
        Assert.Empty(context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Active_request_continues_normally()
    {
        var context = new DefaultHttpContext();
        var called = false;
        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.True(called);
        Assert.Equal(200, context.Response.StatusCode);
    }

    private static AbortedRequestMiddleware Middleware(RequestDelegate next)
        => new(next, NullLogger<AbortedRequestMiddleware>.Instance);
}
