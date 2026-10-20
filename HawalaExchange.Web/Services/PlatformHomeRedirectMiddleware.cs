using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Web.Services;

/// <summary>Opening the application root with an existing platform session must not open the tenant dashboard.</summary>
public sealed class PlatformHomeRedirectMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
            (context.Request.Path == "/" || !context.Request.Path.HasValue) &&
            context.User.Identity?.IsAuthenticated == true && context.User.HasClaim("platform_user", "true") &&
            !ImpersonationIdentity.IsActive(context.User) && PlatformRoles.All.Any(context.User.IsInRole))
        {
            context.Response.Redirect($"{context.Request.PathBase}/platform");
            return Task.CompletedTask;
        }
        return next(context);
    }
}
