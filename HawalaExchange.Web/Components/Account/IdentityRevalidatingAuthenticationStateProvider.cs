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
        protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

        protected override async Task<bool> ValidateAuthenticationStateAsync(
            AuthenticationState authenticationState,
            CancellationToken cancellationToken)
        {
            // This token belongs to a superseded/disposed revalidation loop. Its
            // cancellation must not sign out the current circuit or query SQL.
            if (cancellationToken.IsCancellationRequested)
                return authenticationState.User.Identity?.IsAuthenticated == true;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sessions = scope.ServiceProvider.GetRequiredService<TenantSessionValidator>();
                return await sessions.ValidateAsync(authenticationState.User, cancellationToken) is not null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // EF/SqlClient can throw with a different cancellation token.
                // Keep the existing state while the owning loop terminates;
                // independent timeouts and database failures still propagate.
                return authenticationState.User.Identity?.IsAuthenticated == true;
            }
        }
    }
}
