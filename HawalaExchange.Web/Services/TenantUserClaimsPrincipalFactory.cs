using System.Security.Claims;
using HawalaExchange.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Services;

public sealed class TenantUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<long>> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<long>>(
        userManager,
        roleManager,
        optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!user.IsPlatformUser)
            identity.AddClaim(new Claim(CurrentTenant.TenantIdClaim, user.TenantId.ToString()));
        identity.AddClaim(new Claim("platform_user", user.IsPlatformUser ? "true" : "false"));
        identity.AddClaim(new Claim("local_user_name", user.LocalUserName));
        return identity;
    }
}
