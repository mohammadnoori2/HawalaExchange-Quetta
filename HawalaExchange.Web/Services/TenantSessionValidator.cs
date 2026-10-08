using System.Security.Claims;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Services;

/// <summary>Validates a protected principal before the HTTP/circuit tenant context is available.</summary>
public sealed class TenantSessionValidator(
    ApplicationDbContext context,
    IOptions<IdentityOptions> options,
    ILogger<TenantSessionValidator> logger)
{
    public async Task<ApplicationUser?> ValidateAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        var userIdClaim = principal?.FindFirstValue(options.Value.ClaimsIdentity.UserIdClaimType);
        if (principal?.Identity?.IsAuthenticated != true || !long.TryParse(userIdClaim, out var userId) || userId <= 0)
            return Reject("Missing or invalid user identity");

        // Query only this protected identity, then explicitly verify its tenant/privilege claims.
        // Never expose an unfiltered user store to other application queries.
        var user = await context.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive) return Reject("User missing or inactive", userId);
        if (!string.Equals(principal.FindFirstValue("platform_user"), user.IsPlatformUser ? "true" : "false", StringComparison.Ordinal))
            return Reject("Platform access claim changed", userId);

        if (!user.IsPlatformUser)
        {
            var tenantClaim = principal.FindFirstValue(CurrentTenant.TenantIdClaim);
            if (!long.TryParse(tenantClaim, out var tenantId) || tenantId <= 0 || tenantId != user.TenantId)
                return Reject("Tenant claim missing or mismatched", userId);
            if (!await context.Tenants.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.Id == tenantId && x.IsActive, cancellationToken))
                return Reject("Tenant missing or inactive", userId);
        }

        var stamp = principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrEmpty(stamp) || !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
            return Reject("Security stamp missing or changed", userId);
        return user;
    }

    private ApplicationUser? Reject(string reason, long? userId = null)
    {
        logger.LogDebug("Session validation rejected for user {UserId}: {Reason}", userId, reason);
        return null;
    }
}
