using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace HawalaExchange.Web.Services;

public sealed class TenantCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    CurrentTenant currentTenant) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(
        Circuit circuit,
        CancellationToken cancellationToken)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var value = state.User.FindFirstValue(CurrentTenant.TenantIdClaim);
        if (long.TryParse(value, out var tenantId))
            currentTenant.SetTenant(tenantId);

        value = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(value, out var userId))
            currentTenant.SetUser(userId);

        await base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }
}
