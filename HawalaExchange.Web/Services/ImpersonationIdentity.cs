using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HawalaExchange.Web.Services;

public static class ImpersonationIdentity
{
    public const string ReturnScheme = "ImpersonationReturn";
    public const string SessionClaim = "impersonation_session";
    public const string ActorClaim = "impersonation_actor";
    public const string ActorNameClaim = "impersonation_actor_name";
    public const string ExpiryClaim = "impersonation_expires";
    public static bool IsActive(ClaimsPrincipal? user) => user?.HasClaim(c=>c.Type==SessionClaim)==true;

    public static Task SigningInAsync(CookieSigningInContext context)
    {
        // Identity profile refresh must not turn a bounded impersonation into an ordinary, long-lived login.
        var previous=context.HttpContext.User;
        if (context.Principal?.Identity is ClaimsIdentity identity)
        {
            if (!IsActive(context.Principal) && IsActive(previous) &&
                previous.FindFirstValue(ClaimTypes.NameIdentifier)==context.Principal.FindFirstValue(ClaimTypes.NameIdentifier))
                foreach(var claim in previous.Claims.Where(c=>c.Type is SessionClaim or ActorClaim or ActorNameClaim or ExpiryClaim))identity.AddClaim(claim);
            if (IsActive(context.Principal))
            {
                if (!long.TryParse(context.Principal.FindFirstValue(ExpiryClaim),out var seconds))throw new InvalidOperationException("Invalid impersonation expiration.");
                context.Properties.ExpiresUtc=DateTimeOffset.FromUnixTimeSeconds(seconds);
                context.Properties.IsPersistent=false; context.Properties.AllowRefresh=false;
            }
        }
        return Task.CompletedTask;
    }
}
