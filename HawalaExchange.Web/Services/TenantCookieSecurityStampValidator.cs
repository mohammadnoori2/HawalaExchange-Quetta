using System.Security.Claims;
using HawalaExchange.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HawalaExchange.Web.Services;

/// <summary>Keeps Identity's cookie renewal/rejection flow, without relying on an initialized tenant filter.</summary>
public sealed class TenantCookieSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory loggerFactory,
    TenantSessionValidator sessions)
    : SecurityStampValidator<ApplicationUser>(options, signInManager, loggerFactory)
{
    public override async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (await sessions.ValidateAsync(context.Principal,context.HttpContext.RequestAborted) is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        if (!ImpersonationIdentity.IsActive(context.Principal)) { await base.ValidateAsync(context); return; }
        context.ShouldRenew=false;
    }
    protected override Task<ApplicationUser?> VerifySecurityStamp(ClaimsPrincipal? principal)
        => sessions.ValidateAsync(principal, SignInManager.Context.RequestAborted);
}
