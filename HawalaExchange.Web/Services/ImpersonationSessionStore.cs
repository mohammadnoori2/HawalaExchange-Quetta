using System.Security.Claims;
using System.Text.Json;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Services;

public sealed record ImpersonationTarget(long Id,string Name,string UserName);
public sealed record StartedImpersonation(Guid Id,ApplicationUser Actor,ApplicationUser Target,DateTimeOffset ExpiresAt);

public sealed class ImpersonationSessionStore(IDbContextFactory<ApplicationDbContext> factory,IOptions<IdentityOptions> options)
{
    public const string ReturnSessionClaim="impersonation_return_session";
    public async Task<ApplicationUser> RequireActorAsync(ClaimsPrincipal principal,CancellationToken ct=default)
    {
        if(principal.Identity?.IsAuthenticated!=true || ImpersonationIdentity.IsActive(principal) ||
            principal.FindFirstValue("platform_user")!="true" || !long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier),out var id))
            throw new UnauthorizedAccessException("ورود به‌جای کاربر فقط برای مدیر اصلی پلتفرم مجاز است.");
        await using var db=await factory.CreateDbContextAsync(ct);
        var actor=await db.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && x.IsActive && x.IsPlatformUser,ct);
        if(actor is null || string.IsNullOrEmpty(actor.SecurityStamp) || actor.SecurityStamp!=principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType))
            throw new UnauthorizedAccessException("نشست مدیر معتبر نیست.");
        if(!await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id
                   where ur.UserId==id && PlatformRoles.Privileged.Contains(role.Name!) select ur.UserId).AnyAsync(ct))
            throw new UnauthorizedAccessException("صلاحیت ورود به‌جای کاربر را ندارید.");
        return actor;
    }
    public async Task<IReadOnlyList<ImpersonationTarget>> GetTargetsAsync(ClaimsPrincipal actor,long tenantId,CancellationToken ct=default)
    {
        await RequireActorAsync(actor,ct);
        await using var db=await factory.CreateDbContextAsync(ct);
        if(!await db.Tenants.IgnoreQueryFilters().AnyAsync(x=>x.Id==tenantId && x.IsActive && !x.IsArchived,ct))
            throw new InvalidOperationException("صرافی فعال نیست.");
        return await db.Users.IgnoreQueryFilters().AsNoTracking().Where(x=>x.TenantId==tenantId && x.IsActive && !x.IsPlatformUser)
            .OrderBy(x=>x.FullName).Select(x=>new ImpersonationTarget(x.Id,x.FullName,x.LocalUserName)).ToListAsync(ct);
    }
    public async Task<StartedImpersonation> StartAsync(ClaimsPrincipal principal,long tenantId,long targetId,string reason,CancellationToken ct=default)
    {
        var actor=await RequireActorAsync(principal,ct);
        reason=reason?.Trim() ?? "";
        if(reason.Length is <3 or >500)throw new InvalidOperationException("دلیل ورود باید بین 3 و 500 حرف باشد.");
        await using var db=await factory.CreateDbContextAsync(ct);
        var target=await db.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==targetId && x.TenantId==tenantId && x.IsActive && !x.IsPlatformUser,ct)
            ?? throw new InvalidOperationException("کاربر فعال متعلق به این صرافی یافت نشد.");
        if(string.IsNullOrEmpty(target.SecurityStamp) || !await db.Tenants.IgnoreQueryFilters().AnyAsync(x=>x.Id==tenantId && x.IsActive && !x.IsArchived,ct))
            throw new InvalidOperationException("صرافی یا کاربر برای ورود معتبر نیست.");
        var session=new StartedImpersonation(Guid.NewGuid(),actor,target,DateTimeOffset.UtcNow.AddMinutes(30));
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.ImpersonationSessions(Id,ActorUserId,TargetUserId,TenantId,ActorStamp,TargetStamp,Reason,StartedAt,ExpiresAt)
            VALUES({session.Id},{actor.Id},{target.Id},{tenantId},{actor.SecurityStamp},{target.SecurityStamp},{reason},SYSUTCDATETIME(),{session.ExpiresAt.UtcDateTime});
            """,ct);
        db.PlatformAuditLogs.Add(new PlatformAuditLog {ActorUserId=actor.Id,TenantId=tenantId,Action="Impersonation.Start",EntityName="Users",EntityId=target.Id,
            Details=JsonSerializer.Serialize(new{SessionId=session.Id,Actor=actor.FullName,Target=target.FullName,Reason=reason,session.ExpiresAt})});
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return session;
    }
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal,CancellationToken ct=default)
    {
        if(!Guid.TryParse(principal.FindFirstValue(ImpersonationIdentity.SessionClaim),out var session) ||
            !long.TryParse(principal.FindFirstValue(ImpersonationIdentity.ActorClaim),out var actor) ||
            !long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier),out var target) ||
            !long.TryParse(principal.FindFirstValue(CurrentTenant.TenantIdClaim),out var tenant))return false;
        await using var db=await factory.CreateDbContextAsync(ct);
        return await ImpersonationSessionAccess.IsValidAsync(db,session,actor,target,tenant,ct);
    }
    public async Task EndAsync(ClaimsPrincipal returnPrincipal,string outcome,CancellationToken ct=default)
    {
        var actor=await RequireActorAsync(returnPrincipal,ct);
        if(!Guid.TryParse(returnPrincipal.FindFirstValue(ReturnSessionClaim),out var id))throw new UnauthorizedAccessException();
        await using var db=await factory.CreateDbContextAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var session=await db.Database.SqlQuery<EndSessionRow>($"SELECT TenantId,TargetUserId FROM dbo.ImpersonationSessions WHERE Id={id} AND ActorUserId={actor.Id} AND EndedAt IS NULL AND ExpiresAt>SYSUTCDATETIME()").SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException("نشست ورود به‌جای کاربر منقضی یا بسته شده است.");
        var changed=await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ImpersonationSessions SET EndedAt=SYSUTCDATETIME() WHERE Id={id} AND ActorUserId={actor.Id} AND EndedAt IS NULL",ct);
        if(changed!=1)throw new UnauthorizedAccessException();
        db.PlatformAuditLogs.Add(new PlatformAuditLog {ActorUserId=actor.Id,TenantId=session.TenantId,Action="Impersonation.End",EntityName="Users",EntityId=session.TargetUserId,
            Details=JsonSerializer.Serialize(new{SessionId=id,Outcome=outcome})});
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public sealed class EndSessionRow { public long TenantId {get;set;} public long TargetUserId {get;set;} }
}
