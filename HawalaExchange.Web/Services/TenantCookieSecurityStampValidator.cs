using System.Security.Claims;
using HawalaExchange.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Services;

/// <summary>Keeps Identity's cookie renewal/rejection flow, without relying on an initialized tenant filter.</summary>
public sealed class TenantCookieSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory loggerFactory,
    TenantSessionValidator sessions)
    : SecurityStampValidator<ApplicationUser>(options, signInManager, loggerFactory)
{
    protected override Task<ApplicationUser?> VerifySecurityStamp(ClaimsPrincipal? principal)
        => sessions.ValidateAsync(principal, SignInManager.Context.RequestAborted);
}
