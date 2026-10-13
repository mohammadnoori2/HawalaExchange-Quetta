using System.Text.Json;
using HawalaExchange.Application.Security;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class UserPermissionDatabaseTests(SqlServerPerformanceFixture fixture)
{
    private async Task<long> User(string role)
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var user=new ApplicationUser {TenantId=1,LocalUserName="acl-"+Guid.NewGuid().ToString("N"),UserName=Guid.NewGuid().ToString("N"),FullName="Permission Test",IsActive=true,BranchId=1};
        db.Users.Add(user); await db.SaveChangesAsync();
        var identityRole=await db.Roles.SingleOrDefaultAsync(x=>x.Name==role);
        if(identityRole is null) { identityRole=new IdentityRole<long>(role); db.Roles.Add(identityRole); await db.SaveChangesAsync(); }
        db.UserRoles.Add(new IdentityUserRole<long>{UserId=user.Id,RoleId=identityRole.Id}); await db.SaveChangesAsync(); return user.Id;
    }
    private UserPermissionService Service(long id,UserPermissionChangeBus? bus=null)
    {
        var factory=new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(x=>x.CreateDbContextAsync(It.IsAny<CancellationToken>())).Returns(()=>
        { var db=fixture.CreateContext(); db.BypassSubscriptionEnforcement(); return Task.FromResult(db); });
        return new(factory.Object,new TestCurrentTenant {UserId=id},bus ?? new(),NullLogger<UserPermissionService>.Instance);
    }
    [Fact] public async Task Explicit_empty_profile_denies_admin_and_does_not_fall_back_to_role()
    {
        var admin=await User("Admin"); using var service=Service(admin);
        await service.RefreshAsync(true); Assert.True(service.Can("Users.ManagePermissions"));
        await using var db=fixture.CreateContext();
        var json=JsonSerializer.Serialize(Array.Empty<string>());
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.UserPermissionProfiles(TenantId,UserId,PermissionsJson,ModifiedAt,ModifiedBy) VALUES(1,{admin},{json},SYSUTCDATETIME(),{fixture.UserId})");
        await service.RefreshAsync(true); Assert.Empty(service.CurrentPermissions);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.RequireAsync("Customers.View"));
    }
    [Fact] public async Task Saved_profile_is_audited_and_other_active_service_refreshes()
    {
        var admin=await User("Admin"); var target=await User("Cashier"); var bus=new UserPermissionChangeBus();
        using var manager=Service(admin,bus); using var active=Service(target,bus);
        await active.RefreshAsync(true); Assert.True(active.Can("Hawalas.Create"));
        await manager.SaveForUserAsync(target,["Customers.View"]);
        await active.RefreshAsync(true); Assert.True(active.Can("Customers.View")); Assert.False(active.Can("Hawalas.Create"));
        await using var db=fixture.CreateContext(); Assert.True(await db.AuditLogs.AnyAsync(x=>x.RecordId==target && x.TableName=="UserPermissionProfiles"));
    }
    [Fact] public async Task Limited_user_cannot_grant_permissions_or_read_another_tenant_user()
    {
        var cashier=await User("Cashier"); using var limited=Service(cashier);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>limited.SaveForUserAsync(cashier,PermissionCatalog.Keys));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>limited.GetForUserAsync(fixture.UserId));
    }
    [Fact] public async Task Last_active_permission_administrator_cannot_revoke_own_administration_right()
    {
        var admin=await User("Admin"); using var service=Service(admin);
        await using var db=fixture.CreateContext();
        var active=await db.Users.Where(x=>x.IsActive && x.Id!=admin).Select(x=>x.Id).ToListAsync();
        try
        {
            foreach(var id in active)await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Users SET IsActive=0 WHERE TenantId=1 AND Id={id}");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveForUserAsync(admin,["Dashboard.View"]));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EnsureUserCanBeDisabledAsync(admin));
            await service.RefreshAsync(true); Assert.True(service.Can("Users.ManagePermissions"));
        }
        finally
        {
            foreach(var id in active)await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Users SET IsActive=1 WHERE TenantId=1 AND Id={id}");
        }
    }
}
