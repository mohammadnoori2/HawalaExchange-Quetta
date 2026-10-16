using System.Security.Claims;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HawalaExchange.Web.Controllers;

[Route("impersonation")]
public sealed class ImpersonationController(ImpersonationSessionStore sessions,SignInManager<ApplicationUser> signIn,
    ISubscriptionAccessService subscription) : Controller
{
    [HttpPost("start"),Authorize(Policy="PlatformImpersonate"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Start([FromForm] long tenantId,[FromForm] long userId,[FromForm] string reason,CancellationToken ct)
    {
        if(ImpersonationIdentity.IsActive(User))return Forbid();
        try
        {
            await sessions.RequireActorAsync(User,ct);
            await subscription.EnsureCanSignInAsync(tenantId,ct);
            var session=await sessions.StartAsync(User,tenantId,userId,reason,ct);
            var original=await signIn.CreateUserPrincipalAsync(session.Actor);
            ((ClaimsIdentity)original.Identity!).AddClaim(new Claim(ImpersonationSessionStore.ReturnSessionClaim,session.Id.ToString()));
            var target=await signIn.CreateUserPrincipalAsync(session.Target);
            var identity=(ClaimsIdentity)target.Identity!;
            identity.AddClaim(new Claim(ImpersonationIdentity.SessionClaim,session.Id.ToString()));
            identity.AddClaim(new Claim(ImpersonationIdentity.ActorClaim,session.Actor.Id.ToString()));
            identity.AddClaim(new Claim(ImpersonationIdentity.ActorNameClaim,session.Actor.FullName));
            identity.AddClaim(new Claim(ImpersonationIdentity.ExpiryClaim,session.ExpiresAt.ToUnixTimeSeconds().ToString()));
            var properties=new AuthenticationProperties {IsPersistent=false,AllowRefresh=false,ExpiresUtc=session.ExpiresAt};
            await HttpContext.SignInAsync(ImpersonationIdentity.ReturnScheme,original,properties);
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme,target,properties);
            return LocalRedirect("/");
        }
        catch(UnauthorizedAccessException) { return Forbid(); }
        catch(InvalidOperationException error) { return BadRequest(error.Message); }
    }

    [HttpPost("stop"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        var original=await HttpContext.AuthenticateAsync(ImpersonationIdentity.ReturnScheme);
        if(!original.Succeeded || original.Principal is null)return Forbid();
        try
        {
            var actor=await sessions.RequireActorAsync(original.Principal,ct);
            await sessions.EndAsync(original.Principal,"ReturnedToPlatform",ct);
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            await HttpContext.SignOutAsync(ImpersonationIdentity.ReturnScheme);
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme,await signIn.CreateUserPrincipalAsync(actor),
                new AuthenticationProperties {IsPersistent=false,AllowRefresh=true});
            return LocalRedirect("/tenants");
        }
        catch(UnauthorizedAccessException)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            await HttpContext.SignOutAsync(ImpersonationIdentity.ReturnScheme);
            return LocalRedirect("/Account/Login");
        }
    }
}
