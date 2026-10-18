using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using HawalaExchange.Application.Security;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Web.Services;

public sealed record PlatformTenantSearch(long Id,string Name,string? Email,string? Phone,int Users,DateTime? LastActivityAt,bool Active);
public sealed record PlatformTenantUser(long Id,string Name,string UserName,string? Email,string? Phone,bool Active,
    DateTime? LastLoginAt,DateTimeOffset? LockedUntil,string Role,long BranchId,string ConcurrencyStamp,HashSet<string> Permissions);
public sealed record PlatformChoice(long Id,string Name);
public sealed record PlatformImportJob(long Id,long BatchId,string FileName,string Status,int Progress,string Message,string? Error,int Attempts,DateTime CreatedAt);
public sealed record PlatformSubscriptionHistory(long Id,string Plan,string Status,DateTime StartAt,DateTime EndAt,DateTime? GraceEnd,decimal Price,string Currency,string? Note,string? SuspensionReason);
public sealed record PlatformInvoiceHistory(long Id,string Number,string Status,decimal Total,decimal Paid,decimal Discount,string Currency,DateTime DueAt);
public sealed record PlatformPaymentHistory(long Id,string Status,decimal Amount,string Currency,DateTime? PaidAt,string? Method,string? Reference,long? InvoiceId);
public sealed record PlatformTenantOverview(Tenant Tenant,TenantControlSettings Controls,CompanySetting? Settings,
    IReadOnlyList<PlatformTenantUser> Users,IReadOnlyList<PlatformChoice> Branches,IReadOnlyList<PlatformChoice> Currencies,
    IReadOnlyList<PlatformImportJob> Jobs,IReadOnlyList<PlatformSubscriptionHistory> Subscriptions,IReadOnlyList<PlatformInvoiceHistory> Invoices,IReadOnlyList<PlatformPaymentHistory> Payments);
public sealed class PlatformUserEdit
{
    public long? Id {get;set;}
    [Required,StringLength(200)] public string Name {get;set;}="";
    [Required,StringLength(100)] public string UserName {get;set;}="";
    [EmailAddress,StringLength(256)] public string Email {get;set;}="";
    [StringLength(50)] public string? Phone {get;set;}
    public long BranchId {get;set;}
    public string Role {get;set;}="Cashier";
    public string Password {get;set;}="";
    public string ConcurrencyStamp {get;set;}="";
    public HashSet<string> Permissions {get;set;}=PermissionCatalog.Defaults(["Cashier"]);
}

/// <summary>All calls authenticate the live platform actor; the selected tenant is never inferred from a target user id.</summary>
public sealed class PlatformTenantManagement(ImpersonationSessionStore actors,AuthenticationStateProvider authentication,
    IDbContextFactory<ApplicationDbContext> factory,IServiceScopeFactory scopes,UserPermissionChangeBus changes,IWebHostEnvironment environment)
{
    private async Task<ApplicationUser> ActorAsync(CancellationToken ct) =>
        await actors.RequireActorAsync((await authentication.GetAuthenticationStateAsync()).User,ct);
    private static void Check(IdentityResult result)
    { if(!result.Succeeded)throw new InvalidOperationException(string.Join("؛ ",result.Errors.Select(x=>x.Description))); }
    private static async Task RequireTenantAsync(ApplicationDbContext db,long tenantId,CancellationToken ct)
    {
        if(!await db.Tenants.AnyAsync(x=>x.Id==tenantId && !x.IsArchived,ct))throw new InvalidOperationException("صرافی یافت نشد یا آرشیف شده است.");
    }
    private static Task LockUsersAsync(ApplicationDbContext db,long tenantId,CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sp_getapplock @Resource={"UserPermissions:"+tenantId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r<0 THROW 51000,'User administration is busy',1;",ct);
    private static async Task<HashSet<string>> ReadPermissionsAsync(ApplicationDbContext db,long tenantId,long userId,IEnumerable<string> roles,CancellationToken ct)
    {
        var json=await db.Database.SqlQuery<string>($"SELECT PermissionsJson AS [Value] FROM dbo.UserPermissionProfiles WHERE TenantId={tenantId} AND UserId={userId}").SingleOrDefaultAsync(ct);
        return json is null?PermissionCatalog.Defaults(roles):PermissionCatalog.Normalize(JsonSerializer.Deserialize<string[]>(json)??[]);
    }
    private static Task WritePermissionsAsync(ApplicationDbContext db,long tenantId,long userId,long actorId,HashSet<string> keys,CancellationToken ct)
    {
        var json=JsonSerializer.Serialize(keys.Order());
        return db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.UserPermissionProfiles SET PermissionsJson={json}, ModifiedAt=SYSUTCDATETIME(),ModifiedBy={actorId} WHERE TenantId={tenantId} AND UserId={userId}; IF @@ROWCOUNT=0 INSERT dbo.UserPermissionProfiles(TenantId,UserId,PermissionsJson,ModifiedAt,ModifiedBy) VALUES({tenantId},{userId},{json},SYSUTCDATETIME(),{actorId});",ct);
    }
    private static void Audit(ApplicationDbContext db,long actor,long tenant,string action,long? id=null) =>
        db.PlatformAuditLogs.Add(new PlatformAuditLog{ActorUserId=actor,TenantId=tenant,Action=action,EntityName="TenantManagement",EntityId=id});

    public async Task<IReadOnlyList<PlatformTenantSearch>> SearchAsync(string? search,CancellationToken ct=default)
    {
        await ActorAsync(ct);
        await using var db=await factory.CreateDbContextAsync(ct);
        var q=db.Tenants.AsNoTracking().Where(x=>!x.IsArchived);
        var term=search?.Trim()??"";
        if(term.Length>200)term=term[..200];
        if(term.Length>0)q=q.Where(t=>t.Name.Contains(term) || t.Id.ToString()==term ||
            (t.ContactEmail!=null && t.ContactEmail.Contains(term)) || (t.ContactPhone!=null && t.ContactPhone.Contains(term)) ||
            db.Users.IgnoreQueryFilters().Any(u=>u.TenantId==t.Id && !u.IsPlatformUser && (u.FullName.Contains(term)||u.LocalUserName.Contains(term)||
                (u.Email!=null && u.Email.Contains(term))||(u.PhoneNumber!=null && u.PhoneNumber.Contains(term)))));
        var rows = await q.OrderBy(x=>x.Name).Take(100).Select(x=>new PlatformTenantSearch(x.Id,x.Name,x.ContactEmail,x.ContactPhone,
            db.Users.IgnoreQueryFilters().Count(u=>u.TenantId==x.Id && !u.IsPlatformUser && u.IsActive),x.LastActivityAt,x.IsActive)).ToListAsync(ct);
        var activity=await TenantActivityReader.ReadAsync(db,rows.Select(x=>x.Id),ct);
        return rows.Select(x=>x with {LastActivityAt=activity.GetValueOrDefault(x.Id)}).ToList();
    }
    public async Task<PlatformTenantOverview> GetAsync(long tenantId,CancellationToken ct=default)
    {
        await ActorAsync(ct);
        await using var db=await factory.CreateDbContextAsync(ct); using var tenantScope=db.UseTenantScope(tenantId);
        await RequireTenantAsync(db,tenantId,ct);
        var tenant=await db.Tenants.AsNoTracking().SingleAsync(x=>x.Id==tenantId,ct);
        tenant.LastActivityAt=(await TenantActivityReader.ReadAsync(db,[tenantId],ct)).GetValueOrDefault(tenantId);
        var users=await db.Users.AsNoTracking().OrderBy(x=>x.FullName).ToListAsync(ct);
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
            join u in db.Users on ur.UserId equals u.Id select new {u.Id,Name=r.Name!}).ToListAsync(ct);
        var list=new List<PlatformTenantUser>();
        foreach(var u in users)
        {
            var names=roles.Where(x=>x.Id==u.Id).Select(x=>x.Name).ToArray();
            list.Add(new(u.Id,u.FullName,u.LocalUserName,u.Email,u.PhoneNumber,u.IsActive,u.LastLoginAt,u.LockoutEnd,
                names.FirstOrDefault(x=>x=="Admin")??names.FirstOrDefault()??"",u.BranchId??0,u.ConcurrencyStamp??"",
                await ReadPermissionsAsync(db,tenantId,u.Id,names,ct)));
        }
        return new(tenant,await TenantOperationalAccess.ReadAsync(db,tenantId,ct),await db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(ct),list,
            await db.Branches.AsNoTracking().Where(x=>!x.IsArchived).Select(x=>new PlatformChoice(x.Id,x.Name)).ToListAsync(ct),
            await db.Currencies.AsNoTracking().Where(x=>x.IsActive).Select(x=>new PlatformChoice(x.Id,x.Code)).ToListAsync(ct),
            await db.HawalaImportJobs.AsNoTracking().OrderByDescending(x=>x.Id).Take(50).Select(x=>new PlatformImportJob(x.Id,x.BatchId,x.Batch.FileName,x.Status,x.ProgressPercent,x.ProgressMessage,x.ErrorMessage,x.Attempts,x.CreatedAt)).ToListAsync(ct),
            await db.TenantSubscriptions.AsNoTracking().Where(x=>x.TenantId==tenantId).OrderByDescending(x=>x.StartAt).ThenByDescending(x=>x.Id).Take(50)
                .Select(x=>new PlatformSubscriptionHistory(x.Id,x.Plan.Name,x.Status.ToString(),x.StartAt,x.EndAt,x.GracePeriodEndAt,x.AgreedPrice,x.CurrencyCode,x.AdministrativeNote,x.SuspensionReason)).ToListAsync(ct),
            await db.SubscriptionInvoices.AsNoTracking().Where(x=>x.TenantId==tenantId).OrderByDescending(x=>x.Id).Take(50)
                .Select(x=>new PlatformInvoiceHistory(x.Id,x.InvoiceNumber,x.Status.ToString(),x.TotalAmount,x.PaidAmount,x.DiscountAmount,x.CurrencyCode,x.DueAt)).ToListAsync(ct),
            await db.SubscriptionPayments.AsNoTracking().Where(x=>x.Subscription.TenantId==tenantId).OrderByDescending(x=>x.Id).Take(50)
                .Select(x=>new PlatformPaymentHistory(x.Id,x.Status.ToString(),x.Amount,x.CurrencyCode,x.PaidAt,x.PaymentMethod,x.ReferenceNumber,x.InvoiceId)).ToListAsync(ct));
    }

    public async Task SaveUserAsync(long tenantId,PlatformUserEdit edit,CancellationToken ct=default)
    {
        var actor=await ActorAsync(ct);
        Validator.ValidateObject(edit,new ValidationContext(edit),true);
        if(edit.Role is not ("Admin" or "Manager" or "Cashier" or "Supervisor"))throw new InvalidOperationException("نقش صرافی معتبر نیست.");
        var keys=PermissionCatalog.Normalize(edit.Permissions);
        await using var scope=scopes.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        using var tenantScope=db.UseTenantScope(tenantId); using var bypass=db.BypassSubscriptionEnforcement();
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await RequireTenantAsync(db,tenantId,ct); await LockUsersAsync(db,tenantId,ct);
        var hadAdmin=await HasActiveAdminAsync(db,ct);
        if(!await db.Branches.AnyAsync(x=>x.Id==edit.BranchId && !x.IsArchived,ct))throw new InvalidOperationException("شعبه متعلق به این صرافی نیست.");
        var isNew=!edit.Id.HasValue;
        ApplicationUser u;
        if(isNew)
        {
            if(await db.Users.IgnoreQueryFilters().AnyAsync(x=>x.LocalUserName==edit.UserName.Trim(),ct))
                throw new InvalidOperationException("این نام کاربری قبلاً ثبت شده است.");
            await TenantOperationalAccess.EnsureUserCapacityAsync(db,tenantId,ct);
            u=new ApplicationUser{TenantId=tenantId,BranchId=edit.BranchId,LocalUserName=edit.UserName.Trim(),UserName=$"{tenantId}:{edit.UserName.Trim()}",
                FullName=edit.Name.Trim(),Email=edit.Email.Trim(),PhoneNumber=edit.Phone?.Trim(),IsActive=true,LockoutEnabled=true};
            Check(await users.CreateAsync(u,edit.Password));
            Check(await users.AddToRoleAsync(u,edit.Role));
        }
        else
        {
            u=await db.Users.SingleOrDefaultAsync(x=>x.Id==edit.Id,ct)??throw new InvalidOperationException("کاربر متعلق به این صرافی نیست.");
            if(u.ConcurrencyStamp!=edit.ConcurrencyStamp)throw new InvalidOperationException("اطلاعات کاربر تغییر کرده است؛ صفحه را بروزرسانی کنید.");
            u.FullName=edit.Name.Trim(); u.Email=edit.Email.Trim(); u.PhoneNumber=edit.Phone?.Trim(); u.BranchId=edit.BranchId;
            // User names are immutable here to avoid breaking existing login identities.
            Check(await users.UpdateAsync(u));
            Check(await users.RemoveFromRolesAsync(u,await users.GetRolesAsync(u)));
            Check(await users.AddToRoleAsync(u,edit.Role));
        }
        await WritePermissionsAsync(db,tenantId,u.Id,actor.Id,keys,ct);
        await EnsureAdministratorAsync(db,tenantId,hadAdmin,ct);
        Check(await users.UpdateSecurityStampAsync(u));
        Audit(db,actor.Id,tenantId,isNew?"TenantUser.Created":"TenantUser.Updated",u.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); changes.Publish(tenantId,u.Id);
    }
    public async Task UserActionAsync(long tenantId,long userId,string action,string? password=null,long? copyFrom=null,CancellationToken ct=default)
    {
        var actor=await ActorAsync(ct);
        await using var scope=scopes.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        using var tenantScope=db.UseTenantScope(tenantId); using var bypass=db.BypassSubscriptionEnforcement();
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await RequireTenantAsync(db,tenantId,ct); await LockUsersAsync(db,tenantId,ct);
        var hadAdmin=await HasActiveAdminAsync(db,ct);
        var u=await db.Users.SingleOrDefaultAsync(x=>x.Id==userId,ct)??throw new InvalidOperationException("کاربر متعلق به این صرافی نیست.");
        switch(action)
        {
            case "SignOut": Check(await users.UpdateSecurityStampAsync(u)); break;
            case "Unlock": Check(await users.SetLockoutEndDateAsync(u,null)); Check(await users.ResetAccessFailedCountAsync(u)); break;
            case "ResetPassword":
                Check(await users.ResetPasswordAsync(u,await users.GeneratePasswordResetTokenAsync(u),password??"")); break;
            case "Disable": u.IsActive=false; Check(await users.UpdateAsync(u)); break;
            case "Enable":
                if(!u.IsActive)await TenantOperationalAccess.EnsureUserCapacityAsync(db,tenantId,ct);
                u.IsActive=true; Check(await users.UpdateAsync(u)); break;
            case "PromoteAdmin":
                if(!u.IsActive)throw new InvalidOperationException("ابتدا کاربر را فعال کنید.");
                if(!await users.IsInRoleAsync(u,"Admin"))Check(await users.AddToRoleAsync(u,"Admin"));
                await WritePermissionsAsync(db,tenantId,u.Id,actor.Id,PermissionCatalog.Defaults(["Admin"]),ct); break;
            case "ResetPermissions":
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.UserPermissionProfiles WHERE TenantId={tenantId} AND UserId={userId}",ct); break;
            case "CopyPermissions":
                var source=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==copyFrom && x.Id!=userId,ct)
                    ??throw new InvalidOperationException("کاربر مبدا متعلق به این صرافی نیست یا همان کاربر مقصد است.");
                await WritePermissionsAsync(db,tenantId,u.Id,actor.Id,await ReadPermissionsAsync(db,tenantId,source.Id,await users.GetRolesAsync(source),ct),ct); break;
            default: throw new InvalidOperationException("عملیات معتبر نیست.");
        }
        if(action is "Disable" or "CopyPermissions" or "ResetPermissions" or "PromoteAdmin")
            await EnsureAdministratorAsync(db,tenantId,hadAdmin,ct);
        if(action!="SignOut")Check(await users.UpdateSecurityStampAsync(u));
        Audit(db,actor.Id,tenantId,"TenantUser."+action,userId);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); changes.Publish(tenantId,userId);
    }
    private static Task<bool> HasActiveAdminAsync(ApplicationDbContext db,CancellationToken ct) =>
        (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
         where u.IsActive && r.Name=="Admin" select u.Id).AnyAsync(ct);
    private static async Task EnsureAdministratorAsync(ApplicationDbContext db,long tenantId,bool requireAdminRole,CancellationToken ct)
    {
        var active=await db.Users.Where(x=>x.TenantId==tenantId && x.IsActive).ToListAsync(ct);
        var admins=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where r.Name=="Admin" select ur.UserId).ToListAsync(ct);
        if(requireAdminRole && !active.Any(x=>admins.Contains(x.Id)))throw new InvalidOperationException("آخرین ادمین فعال قابل حذف یا غیرفعال‌کردن نیست؛ ابتدا ادمین جایگزین تعیین کنید.");
        foreach(var u in active)
        {
            var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==u.Id select r.Name!).ToListAsync(ct);
            var keys=await ReadPermissionsAsync(db,tenantId,u.Id,roles,ct);
            if(keys.Contains("Users.ManagePermissions"))return;
        }
        throw new InvalidOperationException("حداقل یک کاربر فعال باید صلاحیت مدیریت دسترسی داشته باشد.");
    }
    public async Task SaveControlsAsync(long tenantId,TenantControlSettings controls,CancellationToken ct=default)
    {
        var actor=await ActorAsync(ct);
        if(controls.MaxUsers is <=0)throw new InvalidOperationException("سقف کاربران باید مثبت یا خالی باشد.");
        if(controls.TimeZoneId.Length>100)throw new InvalidOperationException("منطقه زمانی معتبر نیست.");
        try{TimeZoneInfo.FindSystemTimeZoneById(controls.TimeZoneId);}catch(TimeZoneNotFoundException){throw new InvalidOperationException("منطقه زمانی معتبر نیست.");}
        var disabled=TenantOperationalAccess.Disabled(controls);
        if(disabled.Any(x=>!TenantOperationalAccess.Features.ContainsKey(x)))throw new InvalidOperationException("قابلیت معتبر نیست.");
        var json=JsonSerializer.Serialize(disabled.Order());
        await using var db=await factory.CreateDbContextAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await RequireTenantAsync(db,tenantId,ct); await LockUsersAsync(db,tenantId,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.TenantOperationalControls SET ReadOnly={controls.ReadOnly},MaxUsers={controls.MaxUsers},DisabledFeaturesJson={json},TimeZoneId={controls.TimeZoneId},ModifiedAt=SYSUTCDATETIME() WHERE TenantId={tenantId}; IF @@ROWCOUNT=0 INSERT dbo.TenantOperationalControls(TenantId,ReadOnly,MaxUsers,DisabledFeaturesJson,TimeZoneId) VALUES({tenantId},{controls.ReadOnly},{controls.MaxUsers},{json},{controls.TimeZoneId});",ct);
        Audit(db,actor.Id,tenantId,"Tenant.ControlsUpdated"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var ids=await db.Users.IgnoreQueryFilters().Where(x=>x.TenantId==tenantId && !x.IsPlatformUser).Select(x=>x.Id).ToListAsync(ct);
        foreach(var id in ids)changes.Publish(tenantId,id);
    }
    public async Task SaveSettingsAsync(long tenantId,CompanySetting edit,CancellationToken ct=default)
    {
        var actor=await ActorAsync(ct);
        Validator.ValidateObject(edit,new ValidationContext(edit),true);
        if(string.IsNullOrWhiteSpace(edit.CompanyName)||edit.CompanyName.Length>200||edit.PhoneNumber?.Length>50||edit.Address?.Length>500||edit.FooterNote?.Length>1000)
            throw new InvalidOperationException("اطلاعات تنظیمات معتبر نیست.");
        await using var db=await factory.CreateDbContextAsync(ct);using var tenantScope=db.UseTenantScope(tenantId);using var bypass=db.BypassSubscriptionEnforcement();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await RequireTenantAsync(db,tenantId,ct);
        if(edit.DefaultProfitCurrencyId.HasValue && !await db.Currencies.AnyAsync(x=>x.Id==edit.DefaultProfitCurrencyId && x.IsActive,ct))throw new InvalidOperationException("ارز متعلق به این صرافی نیست.");
        var s=await db.CompanySettings.FirstOrDefaultAsync(ct);
        if(s is null){s=new CompanySetting{TenantId=tenantId};db.CompanySettings.Add(s);}
        s.CompanyName=edit.CompanyName.Trim();s.PhoneNumber=edit.PhoneNumber?.Trim();s.Address=edit.Address?.Trim();s.FooterNote=edit.FooterNote;
        s.WhatsAppNumber=edit.WhatsAppNumber?.Trim();s.TelegramUserName=edit.TelegramUserName?.Trim();
        s.UsePersianCalendar=edit.UsePersianCalendar;s.DefaultProfitCurrencyId=edit.DefaultProfitCurrencyId;s.UpdatedAt=DateTime.UtcNow;
        Audit(db,actor.Id,tenantId,"Tenant.SettingsUpdated",s.Id);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task RetryImportAsync(long tenantId,long jobId,CancellationToken ct=default)
    {
        var actor=await ActorAsync(ct);
        await using var db=await factory.CreateDbContextAsync(ct);using var tenantScope=db.UseTenantScope(tenantId);
        await RequireTenantAsync(db,tenantId,ct); await db.EnsureCurrentTenantWriteAccessAsync(ct);
        var job=await db.HawalaImportJobs.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==jobId,ct)??throw new InvalidOperationException("عملیات متعلق به این صرافی نیست.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var batch=await db.HawalaImportBatches.FromSqlInterpolated($"SELECT * FROM dbo.HawalaImportBatches WITH(UPDLOCK,HOLDLOCK) WHERE TenantId={tenantId} AND Id={job.BatchId}").AsNoTracking().SingleAsync(ct);
        job=await db.HawalaImportJobs.AsNoTracking().SingleAsync(x=>x.Id==jobId,ct);
        if(batch.Status!="Preview" || job.Status!="Failed")throw new InvalidOperationException("فقط فایل ناموفق و ثبت‌نشده قابل تلاش مجدد است.");
        HawalaExchange.Application.DTOs.ConfirmHawalaImportDto? request;
        try{request=JsonSerializer.Deserialize<HawalaExchange.Application.DTOs.ConfirmHawalaImportDto>(job.RequestJson);}
        catch(JsonException){throw new InvalidOperationException("تنظیمات فایل قابل خواندن نیست؛ ابتدا پیش‌نمایش را اصلاح کنید.");}
        if(request is null || request.BatchId!=batch.Id || request.UsePreparedLocations && batch.LocationsPreparedAt is null ||
            await db.HawalaImportRows.AnyAsync(x=>x.BatchId==batch.Id && x.ValidationErrors!=null && x.ValidationErrors!="",ct))
            throw new InvalidOperationException("پیش‌نمایش یا تنظیمات فایل نیاز به اصلاح دارد.");
        if(!await db.Users.AnyAsync(x=>x.Id==job.RequestedBy && x.IsActive,ct))throw new InvalidOperationException("کاربر ثبت‌کننده فعال نیست.");
        var roles=await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==job.RequestedBy select r.Name!).ToListAsync(ct);
        var keys=TenantOperationalAccess.Apply(await ReadPermissionsAsync(db,tenantId,job.RequestedBy,roles,ct),await TenantOperationalAccess.ReadAsync(db,tenantId,ct));
        if(!keys.Contains("BulkImports.Create"))throw new InvalidOperationException("کاربر ثبت‌کننده صلاحیت ثبت گروهی ندارد.");
        var changed=await db.HawalaImportJobs.Where(x=>x.Id==jobId && x.Status=="Failed").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Queued").SetProperty(x=>x.ProgressPercent,0)
            .SetProperty(x=>x.ErrorMessage,(string?)null).SetProperty(x=>x.ProgressMessage,"تلاش مجدد توسط پشتیبانی؛ در صف ثبت.").SetProperty(x=>x.StartedAt,(DateTime?)null).SetProperty(x=>x.CompletedAt,(DateTime?)null),ct);
        if(changed!=1)throw new InvalidOperationException("وضعیت عملیات تغییر کرده است.");
        Audit(db,actor.Id,tenantId,"Tenant.ImportRetried",jobId);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task SaveLogoAsync(long tenantId,byte[] bytes,CancellationToken ct=default)
    {
        var actor=await ActorAsync(ct);
        if(bytes.Length is <8 or >2097152)throw new InvalidOperationException("حداکثر اندازه لوگو 2 MB است.");
        var extension=bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})?".png":
            bytes[0]==255 && bytes[1]==216 && bytes[2]==255?".jpg":throw new InvalidOperationException("فقط تصویر PNG و JPEG مجاز است.");
        await using var db=await factory.CreateDbContextAsync(ct);using var tenantScope=db.UseTenantScope(tenantId);using var bypass=db.BypassSubscriptionEnforcement();
        await RequireTenantAsync(db,tenantId,ct);
        var setting=await db.CompanySettings.FirstOrDefaultAsync(ct)??throw new InvalidOperationException("ابتدا تنظیمات صرافی را ذخیره کنید.");
        var directory=Path.Combine(environment.WebRootPath,"uploads","company-logo");Directory.CreateDirectory(directory);
        var name=$"tenant_{tenantId}_{Guid.NewGuid():N}{extension}";var path=Path.Combine(directory,name);
        await File.WriteAllBytesAsync(path,bytes,ct);
        try
        {
            setting.LogoPath=$"/uploads/company-logo/{name}";setting.UpdatedAt=DateTime.UtcNow;
            Audit(db,actor.Id,tenantId,"Tenant.LogoUpdated",setting.Id);await db.SaveChangesAsync(ct);
        }
        catch{File.Delete(path);throw;}
    }
    public async Task<(bool Connected,string Version,string Backup)> HealthAsync(CancellationToken ct=default)
    {
        await ActorAsync(ct);
        await using var db=await factory.CreateDbContextAsync(ct);db.Database.SetCommandTimeout(5);
        var connected=await db.Database.CanConnectAsync(ct);
        var backup="نامشخص؛ دسترسی به تاریخچه بک‌آپ میزبان فراهم نیست.";
        if(connected)
        {
            try
            {
                var at=await db.Database.SqlQuery<DateTime?>($"SELECT MAX(backup_finish_date) AS [Value] FROM msdb.dbo.backupset WHERE database_name=DB_NAME() AND type='D'").SingleAsync(ct);
                backup=at.HasValue?$"آخرین بک‌آپ کامل ثبت‌شده توسط SQL Server: {at:yyyy/MM/dd HH:mm} (زمان سرور)":"در تاریخچه SQL Server بک‌آپ کاملی یافت نشد.";
            }
            catch(Microsoft.Data.SqlClient.SqlException){ }
        }
        return(connected,typeof(PlatformTenantManagement).Assembly.GetName().Version?.ToString()??"نامشخص",backup);
    }
}
