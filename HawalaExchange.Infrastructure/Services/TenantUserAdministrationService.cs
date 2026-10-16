using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Security;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class TenantUserAdministrationService(ApplicationDbContext db,UserManager<ApplicationUser> users,
    UserPermissionService permissions,ISubscriptionAccessService subscription)
{
    private static void Check(IdentityResult result)
    { if(!result.Succeeded)throw new InvalidOperationException(string.Join("؛ ",result.Errors.Select(x=>x.Description))); }
    private async Task EnsureTargetAuthorityAsync(long id)
    {
        var existing=await permissions.GetForUserAsync(id);
        if(existing.Any(x=>!permissions.Can(x)))throw new UnauthorizedAccessException("تغییر کاربری که صلاحیت بیشتری از شما دارد مجاز نیست.");
    }
    private async Task EnsureTargetAuthorityWithinAsync(long id)
    {
        var actor=await UserPermissionService.ReadAsync(db,db.CurrentUserId,default);
        var target=await UserPermissionService.ReadAsync(db,id,default,true);
        if(target.Any(x=>!actor.Contains(x)))throw new UnauthorizedAccessException("صلاحیت تغییر این کاربر را ندارید.");
    }
    public async Task SaveAsync(long? id,CreateUserDto dto,IEnumerable<string> selected)
    {
        await permissions.RequireAsync(id.HasValue ? "Users.Edit":"Users.Create");
        if(id.HasValue)await EnsureTargetAuthorityAsync(id.Value);
        if(dto.Role is not ("Admin" or "Manager" or "Cashier" or "Supervisor"))throw new InvalidOperationException("نقش معتبر نیست.");
        var keys=PermissionCatalog.Normalize(selected);
        var old=id.HasValue ? await permissions.GetForUserAsync(id.Value):null;
        var changing=old is null || !old.SetEquals(keys);
        if(changing && id.HasValue)await permissions.ValidateGrantAsync(keys);
        if(!id.HasValue && keys.Any(x=>!permissions.Can(x)))throw new UnauthorizedAccessException("نمی‌توانید صلاحیتی بیشتر از صلاحیت خود بدهید.");
        if(!await db.Branches.AnyAsync(x=>x.Id==dto.BranchId))throw new InvalidOperationException("شعبه معتبر نیست.");
        if(!id.HasValue)await subscription.EnsureUserCapacityAsync(db.CurrentTenantId);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
        await permissions.LockAsync(db,default);
        var hadAdmin=await HasActiveAdminRoleAsync(db);
        if(!id.HasValue)await TenantOperationalAccess.EnsureUserCapacityAsync(db,db.CurrentTenantId);
        var latest=id.HasValue ? await UserPermissionService.ReadAsync(db,id.Value,default,true):null;
        var mustGrant=latest is null || !latest.SetEquals(keys);
        await permissions.RequireWithinTransactionAsync(db,id.HasValue?"Users.Edit":"Users.Create",mustGrant?keys:null,default,requireManage:id.HasValue);
        if(latest is not null)
        {
            var actor=await UserPermissionService.ReadAsync(db,db.CurrentUserId,default);
            if(latest.Any(x=>!actor.Contains(x)))throw new UnauthorizedAccessException("صلاحیت تغییر این کاربر را ندارید.");
        }
        ApplicationUser user;
        if(id.HasValue)
        {
            user=await db.Users.SingleOrDefaultAsync(x=>x.Id==id.Value && !x.IsPlatformUser) ?? throw new InvalidOperationException("کاربر یافت نشد.");
            // Freeze the effective profile before changing its role; role changes must not grant rights implicitly.
            await permissions.SaveWithinTransactionAsync(db,user.Id,keys,default);
            user.FullName=dto.FullName; user.BranchId=dto.BranchId; user.Email=dto.Email.Trim();
            Check(await users.UpdateAsync(user));
            Check(await users.RemoveFromRolesAsync(user,await users.GetRolesAsync(user)));
            Check(await users.AddToRoleAsync(user,dto.Role));
        }
        else
        {
            user=new ApplicationUser { TenantId=db.CurrentTenantId,LocalUserName=dto.UserName.Trim(),UserName=$"{db.CurrentTenantId}:{dto.UserName.Trim()}",FullName=dto.FullName,Email=dto.Email.Trim(),BranchId=dto.BranchId,IsActive=true,CreatedAt=DateTime.UtcNow };
            Check(await users.CreateAsync(user,dto.Password)); Check(await users.AddToRoleAsync(user,dto.Role));
            await permissions.SaveWithinTransactionAsync(db,user.Id,keys,default);
        }
        if(hadAdmin)await EnsureActiveAdminRoleAsync(db);
        await tx.CommitAsync(); permissions.Notify(user.Id);
        }
        catch { await tx.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
    }
    public async Task SetActiveAsync(long id,bool active)
    {
        await permissions.RequireAsync(active?"Users.Edit":"Users.Delete");
        await EnsureTargetAuthorityAsync(id);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
        await permissions.LockAsync(db,default);
        var hadAdmin=await HasActiveAdminRoleAsync(db);
        await permissions.RequireWithinTransactionAsync(db,active?"Users.Edit":"Users.Delete",null,default);
        await EnsureTargetAuthorityWithinAsync(id);
        var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==id && !x.IsPlatformUser) ?? throw new InvalidOperationException("کاربر یافت نشد.");
        if(!active)await permissions.EnsureAdministratorAsync(db,id,null,default);
        if(active && !user.IsActive)await TenantOperationalAccess.EnsureUserCapacityAsync(db,db.CurrentTenantId);
        user.IsActive=active; Check(await users.UpdateAsync(user));
        if(hadAdmin)await EnsureActiveAdminRoleAsync(db);
        await tx.CommitAsync(); permissions.Notify(id);
        }
        catch { await tx.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
    }
    public async Task ResetPasswordAsync(long id,string password)
    {
        await permissions.RequireAsync("Users.Edit");
        await EnsureTargetAuthorityAsync(id);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
        await permissions.LockAsync(db,default);
        await permissions.RequireWithinTransactionAsync(db,"Users.Edit",null,default);
        await EnsureTargetAuthorityWithinAsync(id);
        var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==id && !x.IsPlatformUser) ?? throw new InvalidOperationException("کاربر یافت نشد.");
        Check(await users.ResetPasswordAsync(user,await users.GeneratePasswordResetTokenAsync(user),password));
        await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
    }
    private static async Task EnsureActiveAdminRoleAsync(ApplicationDbContext db)
    {
        if(!await HasActiveAdminRoleAsync(db))
            throw new InvalidOperationException("حداقل یک ادمین فعال باید باقی بماند؛ ابتدا ادمین جایگزین تعیین کنید.");
    }
    private static Task<bool> HasActiveAdminRoleAsync(ApplicationDbContext db) =>
        (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
         where u.IsActive && r.Name=="Admin" select u.Id).AnyAsync();
}
