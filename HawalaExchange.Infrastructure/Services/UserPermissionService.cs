using System.Text.Json;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Security;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.Infrastructure.Services;

public sealed class UserPermissionService(IDbContextFactory<ApplicationDbContext> factory, ICurrentTenant tenant,
    UserPermissionChangeBus bus, ILogger<UserPermissionService> logger) : IUserPermissionService, IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource stop = new();
    private HashSet<string> current = new(StringComparer.Ordinal);
    private DateTime checkedAt;
    private bool watching;
    public event Action? Changed;
    public bool IsPlatformUser { get; private set; }
    public IReadOnlySet<string> CurrentPermissions => current;
    public bool Can(string permission) => current.Contains(permission);

    internal static async Task<HashSet<string>> ReadAsync(ApplicationDbContext db,long id,CancellationToken ct,bool includeInactive=false)
    {
        var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(user is null || (!includeInactive && !user.IsActive) || user.IsPlatformUser)return [];
        var profiles=await db.Database.SqlQuery<string>($"SELECT PermissionsJson AS [Value] FROM dbo.UserPermissionProfiles WHERE TenantId={db.CurrentTenantId} AND UserId={id}").ToListAsync(ct);
        if(profiles.Count>0)return PermissionCatalog.Normalize(JsonSerializer.Deserialize<string[]>(profiles[0]) ?? []);
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==id select r.Name!).ToListAsync(ct);
        return PermissionCatalog.Defaults(roles);
    }
    public async Task RefreshAsync(bool force=false,CancellationToken cancellationToken=default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if(!force && DateTime.UtcNow-checkedAt<TimeSpan.FromSeconds(5))return;
            await using var db=await factory.CreateDbContextAsync(cancellationToken);
            var next=tenant.UserId>0 && tenant.TenantId>0 ? await ReadAsync(db,tenant.UserId,cancellationToken):[];
            if(tenant.TenantId>0)
                next=TenantOperationalAccess.Apply(next,await TenantOperationalAccess.ReadAsync(db,tenant.TenantId,cancellationToken));
            if(tenant.TenantId>0 && !await db.Tenants.AsNoTracking().AnyAsync(x=>x.Id==tenant.TenantId && x.IsActive && !x.IsArchived,cancellationToken))next=[];
            if(tenant.SessionSecurityStamp is string stamp && !await db.Users.AsNoTracking()
                .AnyAsync(x=>x.Id==tenant.UserId && x.IsActive && x.SecurityStamp==stamp,cancellationToken))next=[];
            if (tenant.ImpersonationSessionId is Guid session &&
                (tenant.OriginalActorUserId is not long actor || !await ImpersonationSessionAccess.IsValidAsync(db,session,actor,tenant.UserId,tenant.TenantId,cancellationToken)))
                next=[];
            var changed=!current.SetEquals(next);
            current=next; checkedAt=DateTime.UtcNow;
            if(changed)Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public async Task RequireAsync(string permission,CancellationToken cancellationToken=default)
    {
        await RefreshAsync(true,cancellationToken);
        if(!Can(permission))throw new UnauthorizedAccessException("شما صلاحیت انجام این عملیات را ندارید.");
    }
    public async Task<HashSet<string>> GetForUserAsync(long userId,CancellationToken cancellationToken=default)
    {
        await RequireAsync("Users.View",cancellationToken);
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        return await ReadAsync(db,userId,cancellationToken,true);
    }
    public async Task ValidateGrantAsync(IEnumerable<string> permissions,CancellationToken cancellationToken=default)
    {
        await RequireAsync("Users.ManagePermissions",cancellationToken);
        if(PermissionCatalog.Normalize(permissions).Any(x=>!Can(x)))throw new UnauthorizedAccessException("نمی‌توانید صلاحیتی بیشتر از صلاحیت خود به کاربر بدهید.");
    }
    internal async Task EnsureAdministratorAsync(ApplicationDbContext db,long target,HashSet<string>? replacement,CancellationToken ct)
    {
        var active=await db.Users.AsNoTracking().Where(x=>x.IsActive && !x.IsPlatformUser).Select(x=>x.Id).ToListAsync(ct);
        foreach(var id in active)
        {
            var keys=id==target ? replacement ?? [] : await ReadAsync(db,id,ct);
            if(keys.Contains("Users.ManagePermissions"))return;
        }
        throw new InvalidOperationException("حداقل یک کاربر فعال با صلاحیت مدیریت دسترسی باید باقی بماند.");
    }
    public async Task SaveForUserAsync(long userId,IEnumerable<string> permissions,CancellationToken cancellationToken=default)
    {
        var keys=PermissionCatalog.Normalize(permissions);
        await ValidateGrantAsync(keys,cancellationToken);
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,cancellationToken);
        await LockAsync(db,cancellationToken);
        await RequireWithinTransactionAsync(db,"Users.ManagePermissions",keys,cancellationToken);
        var existing=await ReadAsync(db,userId,cancellationToken,true);
        var actor=await ReadAsync(db,tenant.UserId,cancellationToken);
        if(existing.Any(x=>!actor.Contains(x)))throw new UnauthorizedAccessException("تغییر کاربری با صلاحیت بیشتر از شما مجاز نیست.");
        await SaveWithinTransactionAsync(db,userId,keys,cancellationToken);
        await tx.CommitAsync(cancellationToken);
        Notify(userId);
        if(userId==tenant.UserId)await RefreshAsync(true,cancellationToken);
    }
    internal async Task LockAsync(ApplicationDbContext db,CancellationToken ct)
    {
        var resource=$"UserPermissions:{tenant.TenantId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r<0 THROW 51000, 'Permission update lock failed', 1;",ct);
    }
    internal void Notify(long userId)=>bus.Publish(tenant.TenantId,userId);
    internal async Task RequireWithinTransactionAsync(ApplicationDbContext db,string permission,HashSet<string>? grant,CancellationToken ct,bool requireManage=true)
    {
        var actor=await ReadAsync(db,tenant.UserId,ct);
        if(!actor.Contains(permission) || grant is not null && (requireManage && !actor.Contains("Users.ManagePermissions") || grant.Any(x=>!actor.Contains(x))))
            throw new UnauthorizedAccessException("صلاحیت این عملیات یا تغییر دسترسی را ندارید.");
    }
    internal async Task SaveWithinTransactionAsync(ApplicationDbContext db,long userId,HashSet<string> keys,CancellationToken cancellationToken)
    {
        if(!await db.Users.AnyAsync(x=>x.Id==userId && !x.IsPlatformUser,cancellationToken))throw new InvalidOperationException("کاربر صرافی جاری یافت نشد.");
        await EnsureAdministratorAsync(db,userId,keys,cancellationToken);
        var json=JsonSerializer.Serialize(keys.Order());
        var old=await db.Database.SqlQuery<string>($"SELECT PermissionsJson AS [Value] FROM dbo.UserPermissionProfiles WHERE TenantId={tenant.TenantId} AND UserId={userId}").ToListAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.UserPermissionProfiles SET PermissionsJson={json}, ModifiedAt=SYSUTCDATETIME(), ModifiedBy={tenant.UserId} WHERE TenantId={tenant.TenantId} AND UserId={userId}; IF @@ROWCOUNT=0 INSERT dbo.UserPermissionProfiles(TenantId,UserId,PermissionsJson,ModifiedAt,ModifiedBy) VALUES({tenant.TenantId},{userId},{json},SYSUTCDATETIME(),{tenant.UserId});",cancellationToken);
        db.AuditLogs.Add(new HawalaExchange.Domain.Entities.AuditLog { TenantId=tenant.TenantId,UserId=tenant.UserId,ProcessId=Guid.NewGuid(),Action="PermissionsChanged",TableName="UserPermissionProfiles",RecordId=userId,OldValue=old.FirstOrDefault(),NewValue=json });
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task EnsureUserCanBeDisabledAsync(long userId,CancellationToken cancellationToken=default)
    {
        await RequireAsync("Users.Delete",cancellationToken);
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        await EnsureAdministratorAsync(db,userId,null,cancellationToken);
    }
    public void StartWatching()
    {
        if(watching)return;
        watching=true; bus.Changed+=OnChanged; _=WatchAsync();
    }
    private void OnChanged(long tenantId,long userId)
    { if(tenantId==tenant.TenantId && userId==tenant.UserId)_=SafeRefreshAsync(); }
    private async Task SafeRefreshAsync()
    {
        try { await RefreshAsync(true,stop.Token); }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception ex) { current=[]; Changed?.Invoke(); logger.LogWarning(ex,"Permission refresh failed; access denied"); }
    }
    private async Task WatchAsync()
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        try { while(await timer.WaitForNextTickAsync(stop.Token))await SafeRefreshAsync(); }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
    }
    public void Dispose() { bus.Changed-=OnChanged; stop.Cancel(); }
}
