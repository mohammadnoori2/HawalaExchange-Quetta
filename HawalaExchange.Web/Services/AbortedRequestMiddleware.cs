namespace HawalaExchange.Web.Services;

/// <summary>Ends disconnected requests without treating cancellation as an authentication failure.</summary>
public sealed class AbortedRequestMiddleware(RequestDelegate next, ILogger<AbortedRequestMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Avoid opening a database connection for a request that has already ended.
        if (context.RequestAborted.IsCancellationRequested)
        {
            MarkAborted(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Unwind the whole request: never continue to an endpoint with an unchecked
            // identity, reject a valid cookie, or issue a sign-out for a disconnection.
            logger.LogDebug("Request {TraceIdentifier} ended because the client disconnected.", context.TraceIdentifier);
            MarkAborted(context);
        }
    }

    private static void MarkAborted(HttpContext context)
    {
        if (!context.Response.HasStarted)
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
    }
}
