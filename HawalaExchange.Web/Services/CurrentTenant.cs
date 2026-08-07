using System.Security.Claims;
using HawalaExchange.Application.Interfaces;
using Microsoft.AspNetCore.Components.Authorization;

namespace HawalaExchange.Web.Services;

public sealed class CurrentTenant : ICurrentTenant
{
    public const string TenantIdClaim = "tenant_id";
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly AuthenticationStateProvider authenticationStateProvider;
    private long tenantId;
    private long userId;

    public CurrentTenant(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authenticationStateProvider)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.authenticationStateProvider = authenticationStateProvider;
    }

    public long TenantId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(TenantIdClaim);
            if (long.TryParse(value, out var parsed) && parsed > 0)
                tenantId = parsed;

            if (tenantId == 0)
            {
                try
                {
                    var state = authenticationStateProvider
                        .GetAuthenticationStateAsync()
                        .GetAwaiter()
                        .GetResult();
                    value = state.User.FindFirstValue(TenantIdClaim);
                    if (long.TryParse(value, out parsed) && parsed > 0)
                        tenantId = parsed;
                }
                catch (InvalidOperationException)
                {
                    // Authentication state is not initialized in startup scopes.
                }
            }

            return tenantId;
        }
    }

    public bool HasTenant => TenantId > 0;

    public long UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (long.TryParse(value, out var parsed) && parsed > 0)
                userId = parsed;

            if (userId == 0)
            {
                try
                {
                    var state = authenticationStateProvider
                        .GetAuthenticationStateAsync()
                        .GetAwaiter()
                        .GetResult();
                    value = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (long.TryParse(value, out parsed) && parsed > 0)
                        userId = parsed;
                }
                catch (InvalidOperationException)
                {
                    // Authentication state is not initialized in startup scopes.
                }
            }

            return userId;
        }
    }

    public void SetTenant(long value)
    {
        if (value > 0)
            tenantId = value;
    }

    public void SetUser(long value)
    {
        if (value > 0)
            userId = value;
    }
}
