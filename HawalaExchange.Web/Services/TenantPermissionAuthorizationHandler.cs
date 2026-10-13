using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace HawalaExchange.Web.Services;

public sealed class TenantPermissionRequirement : IAuthorizationRequirement;
public sealed class TenantPermissionAuthorizationHandler(IUserPermissionService permissions,NavigationManager navigation,
    IHttpContextAccessor accessor) : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,TenantPermissionRequirement requirement)
    {
        if(context.User.Identity?.IsAuthenticated!=true)return;
        string path;
        try { path=navigation.ToBaseRelativePath(navigation.Uri); }
        catch(InvalidOperationException) { path=accessor.HttpContext?.Request.Path.Value ?? ""; }
        var key=PermissionCatalog.PagePermission(path);
        if(key is null)return;
        await permissions.RefreshAsync();
        if(permissions.Can(key))context.Succeed(requirement);
    }
}
