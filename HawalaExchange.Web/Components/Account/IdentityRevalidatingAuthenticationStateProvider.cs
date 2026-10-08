using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace HawalaExchange.Web.Components.Account
{
    // Revalidates the connected user's security stamp without depending on a
    // tenant-scoped query filter that is unavailable in the background scope.
    internal sealed class IdentityRevalidatingAuthenticationStateProvider(
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopeFactory)
        : RevalidatingServerAuthenticationStateProvider(loggerFactory)
    {
        protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

        protected override async Task<bool> ValidateAuthenticationStateAsync(
            AuthenticationState authenticationState,
            CancellationToken cancellationToken)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sessions = scope.ServiceProvider.GetRequiredService<TenantSessionValidator>();
            return await sessions.ValidateAsync(authenticationState.User, cancellationToken) is not null;
        }
    }
}
