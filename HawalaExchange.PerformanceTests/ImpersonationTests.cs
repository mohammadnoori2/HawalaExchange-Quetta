using System.Security.Claims;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class ImpersonationTests(SqlServerPerformanceFixture fixture)
{
    private static readonly IdentityOptions IdentityOptions=new();
    private async Task<ApplicationUser> User(bool platform,string role)
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var user=new ApplicationUser {TenantId=1,IsPlatformUser=platform,IsActive=true,BranchId=platform?null:1,FullName="Impersonation Test",LocalUserName=Guid.NewGuid().ToString("N"),UserName=Guid.NewGuid().ToString("N"),SecurityStamp=Guid.NewGuid().ToString("N")};
        db.Users.Add(user); await db.SaveChangesAsync();
        var r=await db.Roles.SingleOrDefaultAsync(x=>x.Name==role);
        if(r is null){r=new IdentityRole<long>(role){NormalizedName=role.ToUpperInvariant()};db.Roles.Add(r);await db.SaveChangesAsync();}
        db.UserRoles.Add(new IdentityUserRole<long>{UserId=user.Id,RoleId=r.Id});await db.SaveChangesAsync();return user;
    }
    private static ClaimsPrincipal Principal(ApplicationUser user)=>new(new ClaimsIdentity(new[]
    {new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim("platform_user",user.IsPlatformUser?"true":"false"),
     new Claim(CurrentTenant.TenantIdClaim,user.TenantId.ToString()),new Claim(IdentityOptions.ClaimsIdentity.SecurityStampClaimType,user.SecurityStamp!)},"Test"));
    private static ClaimsPrincipal Impersonated(StartedImpersonation session)
    {
        var p=Principal(session.Target);var identity=(ClaimsIdentity)p.Identity!;
        identity.AddClaim(new Claim(ImpersonationIdentity.SessionClaim,session.Id.ToString()));
        identity.AddClaim(new Claim(ImpersonationIdentity.ActorClaim,session.Actor.Id.ToString()));
        identity.AddClaim(new Claim(ImpersonationIdentity.ActorNameClaim,session.Actor.FullName));
        identity.AddClaim(new Claim(ImpersonationIdentity.ExpiryClaim,session.ExpiresAt.ToUnixTimeSeconds().ToString()));return p;
    }
    private TenantDbContextFactory Factory(TestCurrentTenant? identity=null)
    {
        using var db=fixture.CreateContext();
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options,
            identity ?? new TestCurrentTenant {UserId=fixture.UserId});
    }
    private ImpersonationSessionStore Store()=>new(Factory(),Options.Create(IdentityOptions));

    [Theory]
    [InlineData(false,"Admin")]
    [InlineData(false,"PlatformOwner")]
    [InlineData(true,"PlatformSupport")]
    [InlineData(true,"PlatformAdmin")]
    public async Task Non_privileged_and_tenant_users_cannot_start_even_with_forged_role_claims(bool platform,string role)
    {
        var actor=await User(platform,role);var target=await User(false,"Cashier");var p=Principal(actor);
        ((ClaimsIdentity)p.Identity!).AddClaim(new Claim(ClaimTypes.Role,PlatformRoles.Owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Store().StartAsync(p,1,target.Id,"Test support"));
    }
    [Fact] public async Task Start_is_tenant_scoped_requires_reason_and_disallows_platform_targets_and_nesting()
    {
        var actor=await User(true,PlatformRoles.Owner);var target=await User(false,"Cashier");var store=Store();var p=Principal(actor);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.StartAsync(p,99999,target.Id,"Valid reason"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.StartAsync(p,1,actor.Id,"Valid reason"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.StartAsync(p,1,target.Id,""));
        var started=await store.StartAsync(p,1,target.Id,"Investigate customer issue");
        Assert.True(await store.ValidateAsync(Impersonated(started)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.StartAsync(Impersonated(started),1,target.Id,"Nested support"));
        await using var db=fixture.CreateContext();
        Assert.True(await db.PlatformAuditLogs.AnyAsync(x=>x.ActorUserId==actor.Id && x.EntityId==target.Id && x.Action=="Impersonation.Start"));
    }
    [Theory]
    [InlineData("expiry")]
    [InlineData("actor-disabled")]
    [InlineData("target-disabled")]
    [InlineData("actor-password")]
    [InlineData("target-password")]
    [InlineData("actor-role")]
    public async Task Active_session_is_revoked_when_either_identity_or_platform_authority_changes(string change)
    {
        var actor=await User(true,PlatformRoles.Owner);var target=await User(false,"Cashier");var store=Store();
        var session=await store.StartAsync(Principal(actor),1,target.Id,"Test revocation");var p=Impersonated(session);
        await using var db=fixture.CreateContext();
        switch(change)
        {
            case "expiry": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ImpersonationSessions SET ExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id={session.Id}");break;
            case "actor-disabled": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Users SET IsActive=0 WHERE Id={actor.Id}");break;
            case "target-disabled": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Users SET IsActive=0 WHERE Id={target.Id}");break;
            case "actor-password": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Users SET SecurityStamp=N'changed' WHERE Id={actor.Id}");break;
            case "target-password": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Users SET SecurityStamp=N'changed' WHERE Id={target.Id}");break;
            case "actor-role": await db.UserRoles.Where(x=>x.UserId==actor.Id).ExecuteDeleteAsync();break;
        }
        Assert.False(await store.ValidateAsync(p));
    }
    [Fact] public async Task Effective_permissions_remain_the_targets_and_ending_blocks_existing_circuits_and_is_audited()
    {
        var actor=await User(true,PlatformRoles.Owner);var target=await User(false,"Supervisor");var store=Store();
        var session=await store.StartAsync(Principal(actor),1,target.Id,"Read-only target test");
        var identity=new TestCurrentTenant {UserId=target.Id,TenantId=1,ImpersonationSessionId=session.Id,OriginalActorUserId=actor.Id};
        using var permissions=new UserPermissionService(Factory(identity),identity,new(),NullLogger<UserPermissionService>.Instance);
        await permissions.RequireAsync("Hawalas.View");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>permissions.RequireAsync("Hawalas.Create"));
        var original=Principal(actor);((ClaimsIdentity)original.Identity!).AddClaim(new Claim(ImpersonationSessionStore.ReturnSessionClaim,session.Id.ToString()));
        await store.EndAsync(original,"ReturnedToPlatform");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>permissions.RequireAsync("Hawalas.View"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.EndAsync(original,"Replay"));
        await using var db=fixture.CreateContext();
        Assert.True(await db.PlatformAuditLogs.AnyAsync(x=>x.ActorUserId==actor.Id && x.EntityId==target.Id && x.Action=="Impersonation.End"));
    }
    [Fact] public async Task EF_writes_are_attributed_and_denied_after_session_expiration()
    {
        var actor=await User(true,PlatformRoles.Owner);var target=await User(false,"Admin");var store=Store();
        var session=await store.StartAsync(Principal(actor),1,target.Id,"Attribution test");
        var identity=new TestCurrentTenant {UserId=target.Id,TenantId=1,ImpersonationSessionId=session.Id,OriginalActorUserId=actor.Id};
        await using var db=Factory(identity).CreateDbContext();using var bypass=db.BypassSubscriptionEnforcement();
        db.AuditLogs.Add(new AuditLog {Action="SUPPORT_TEST",TableName="Users",RecordId=target.Id,UserId=target.Id,ProcessId=Guid.NewGuid(),NewValue="Original description"});
        await db.SaveChangesAsync();
        var audit=await db.AuditLogs.AsNoTracking().SingleAsync(x=>x.Action=="SUPPORT_TEST" && x.RecordId==target.Id);
        Assert.Contains(session.Id.ToString(),audit.NewValue);Assert.Contains("__OriginalPlatformActorUserId",audit.NewValue);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ImpersonationSessions SET ExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id={session.Id}");
        db.AuditLogs.Add(new AuditLog {Action="DENIED",TableName="Users",RecordId=target.Id,UserId=target.Id,ProcessId=Guid.NewGuid()});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>db.SaveChangesAsync());
    }
    [Fact] public async Task Identity_refresh_copies_impersonation_marker_and_cannot_extend_or_persist_the_cookie()
    {
        var target=new ApplicationUser {Id=10,TenantId=1,IsPlatformUser=false,SecurityStamp="target"};
        var actor=new ApplicationUser {Id=20,TenantId=1,IsPlatformUser=true,SecurityStamp="actor"};
        var session=new StartedImpersonation(Guid.NewGuid(),actor,target,DateTimeOffset.UtcNow.AddMinutes(30));
        var http=new DefaultHttpContext {User=Impersonated(session)};
        var scheme=new AuthenticationScheme(IdentityConstants.ApplicationScheme,null,typeof(CookieAuthenticationHandler));
        var refreshed=new CookieSigningInContext(http,scheme,new CookieAuthenticationOptions(),Principal(target),new AuthenticationProperties {IsPersistent=true,ExpiresUtc=DateTimeOffset.UtcNow.AddDays(30)},new CookieOptions());
        await ImpersonationIdentity.SigningInAsync(refreshed);
        Assert.Equal(session.Id.ToString(),refreshed.Principal!.FindFirstValue(ImpersonationIdentity.SessionClaim));
        Assert.False(refreshed.Properties.IsPersistent);Assert.False(refreshed.Properties.AllowRefresh);
        Assert.True(refreshed.Properties.ExpiresUtc<=session.ExpiresAt);
        var returned=new CookieSigningInContext(http,scheme,new CookieAuthenticationOptions(),Principal(actor),new AuthenticationProperties(),new CookieOptions());
        await ImpersonationIdentity.SigningInAsync(returned);Assert.False(ImpersonationIdentity.IsActive(returned.Principal));
    }
}
