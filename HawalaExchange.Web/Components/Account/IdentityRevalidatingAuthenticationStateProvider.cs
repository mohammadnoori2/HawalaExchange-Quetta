using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace HawalaExchange.Web.Components.Account
{
    // Revalidates the connected user's security stamp without depending on a
    // tenant-scoped query filter that is unavailable in the background scope.
    internal sealed class IdentityRevalidatingAuthenticationStateProvider(
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopeFactory,
            IOptions<IdentityOptions> options)
        : RevalidatingServerAuthenticationStateProvider(loggerFactory)
    {
        protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

        protected override async Task<bool> ValidateAuthenticationStateAsync(
            AuthenticationState authenticationState,
            CancellationToken cancellationToken)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await ValidateSecurityStampAsync(
                context, authenticationState.User, cancellationToken);
        }

        private async Task<bool> ValidateSecurityStampAsync(
            ApplicationDbContext context,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(userIdValue, out var userId) || userId <= 0)
                return false;

            // Revalidation runs in a fresh background scope where the tenant context has not
            // been initialized. Resolve without the tenant filter and validate its claim below.
            var user = await context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
            if (user is null || !user.IsActive)
                return false;

            if (!user.IsPlatformUser)
            {
                var tenantClaim = principal.FindFirstValue(CurrentTenant.TenantIdClaim);
                if (!long.TryParse(tenantClaim, out var tenantId) || tenantId != user.TenantId)
                    return false;

                var tenantIsActive = await context.Tenants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == user.TenantId && x.IsActive, cancellationToken);
                if (!tenantIsActive)
                    return false;
            }

            var principalStamp = principal.FindFirstValue(
                options.Value.ClaimsIdentity.SecurityStampClaimType);
            return string.Equals(principalStamp, user.SecurityStamp, StringComparison.Ordinal);
        }
    }
}
