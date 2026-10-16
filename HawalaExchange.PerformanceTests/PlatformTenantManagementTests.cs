using System.Security.Claims;
using System.Text.Json;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Security;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class PlatformTenantManagementTests(SqlServerPerformanceFixture fixture)
{
    private static async Task Role(ApplicationDbContext db,ApplicationUser user,string name)
    {
        var r=await db.Roles.SingleOrDefaultAsync(x=>x.Name==name);
        if(r is null){r=new IdentityRole<long>(name){NormalizedName=name.ToUpperInvariant()};db.Roles.Add(r);await db.SaveChangesAsync();}
        else if(r.NormalizedName!=name.ToUpperInvariant()){r.NormalizedName=name.ToUpperInvariant();await db.SaveChangesAsync();}
        db.UserRoles.Add(new IdentityUserRole<long>{UserId=user.Id,RoleId=r.Id});await db.SaveChangesAsync();
    }
    private async Task<(long Tenant,long Admin,long Cashier,long Branch,ApplicationUser Actor)> Seed()
    {
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();
        var t=new Tenant{Name="SaaS test "+Guid.NewGuid().ToString("N"),IsActive=true};db.Tenants.Add(t);await db.SaveChangesAsync();
        var actor=new ApplicationUser{TenantId=1,IsPlatformUser=true,FullName="Platform Test",LocalUserName=Guid.NewGuid().ToString("N"),UserName=Guid.NewGuid().ToString("N"),SecurityStamp=Guid.NewGuid().ToString("N")};
        db.Users.Add(actor);await db.SaveChangesAsync();await Role(db,actor,PlatformRoles.Owner);
        using var scope=db.UseTenantScope(t.Id);
        var b=new Branch{Name="Main",Code="MAIN"};db.Branches.Add(b);await db.SaveChangesAsync();
        var admin=new ApplicationUser{TenantId=t.Id,BranchId=b.Id,IsActive=true,FullName="Admin Test",LocalUserName=Guid.NewGuid().ToString("N"),UserName=Guid.NewGuid().ToString("N"),SecurityStamp=Guid.NewGuid().ToString("N"),ConcurrencyStamp=Guid.NewGuid().ToString("N"),LockoutEnabled=true};
        var cashier=new ApplicationUser{TenantId=t.Id,BranchId=b.Id,IsActive=true,FullName="Cashier Test",LocalUserName=Guid.NewGuid().ToString("N"),UserName=Guid.NewGuid().ToString("N"),SecurityStamp=Guid.NewGuid().ToString("N"),ConcurrencyStamp=Guid.NewGuid().ToString("N"),LockoutEnabled=true};
        db.Users.AddRange(admin,cashier);await db.SaveChangesAsync();await Role(db,admin,"Admin");await Role(db,cashier,"Cashier");
        return(t.Id,admin.Id,cashier.Id,b.Id,actor);
    }
    private static ClaimsPrincipal Principal(ApplicationUser actor)=>new(new ClaimsIdentity(new[]{
        new Claim(ClaimTypes.NameIdentifier,actor.Id.ToString()),new Claim("platform_user",actor.IsPlatformUser?"true":"false"),
        new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType,actor.SecurityStamp!)},"Test"));
    private ServiceProvider Services(ApplicationUser actor,TestCurrentTenant? current=null)
    {
        using var db=fixture.CreateContext();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        var services=new ServiceCollection();services.AddLogging();services.AddDataProtection();services.AddSingleton(options);
        services.AddSingleton<ICurrentTenant>(current??new TestCurrentTenant());
        services.AddScoped<IDbContextFactory<ApplicationDbContext>,TenantDbContextFactory>();services.AddScoped(sp=>sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
        services.AddIdentityCore<ApplicationUser>(o=>o.User.AllowedUserNameCharacters=null).AddRoles<IdentityRole<long>>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        services.AddScoped<AuthenticationStateProvider>(_=>new FixedAuth(Principal(actor)));
        services.AddScoped<ImpersonationSessionStore>();services.AddSingleton<UserPermissionChangeBus>();services.AddSingleton(Mock.Of<IWebHostEnvironment>());
        services.AddScoped<PlatformTenantManagement>();return services.BuildServiceProvider();
    }
    private sealed class FixedAuth(ClaimsPrincipal user):AuthenticationStateProvider
    {public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(user));}

    [Fact] public async Task Defaults_do_not_change_existing_permissions_and_existing_user_data()
    {
        var s=await Seed();using var provider=Services(s.Actor);await using var scope=provider.CreateAsyncScope();var service=scope.ServiceProvider.GetRequiredService<PlatformTenantManagement>();
        var before=await service.GetAsync(s.Tenant);Assert.False(before.Controls.ReadOnly);Assert.Null(before.Controls.MaxUsers);Assert.Empty(TenantOperationalAccess.Disabled(before.Controls));
        Assert.True(before.Users.Single(x=>x.Id==s.Admin).Permissions.Contains("Users.ManagePermissions"));
        Assert.Equal(PermissionCatalog.Keys.Count,TenantOperationalAccess.Apply(PermissionCatalog.Defaults(["Admin"]),before.Controls).Count);
        await using var db=fixture.CreateContext();Assert.True(await db.Users.AnyAsync(x=>x.Id==fixture.UserId && x.IsActive));
    }
    [Fact] public async Task Tenant_user_and_revoked_platform_actor_cannot_manage_tenants()
    {
        var s=await Seed();await using var db=fixture.CreateContext();var tenantUser=await db.Users.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Admin);
        using(var p=Services(tenantUser)){await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>p.GetRequiredService<PlatformTenantManagement>().GetAsync(s.Tenant));}
        using var provider=Services(s.Actor);await db.Users.IgnoreQueryFilters().Where(x=>x.Id==s.Actor.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SecurityStamp,"revoked"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>provider.GetRequiredService<PlatformTenantManagement>().SaveControlsAsync(s.Tenant,new()));
    }
    [Fact] public async Task Last_admin_is_protected_and_promotion_preserves_previous_admin()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UserActionAsync(s.Tenant,s.Admin,"Disable"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UserActionAsync(s.Tenant,s.Admin,"CopyPermissions",copyFrom:s.Cashier));
        await service.UserActionAsync(s.Tenant,s.Cashier,"PromoteAdmin");
        var promoted=await service.GetAsync(s.Tenant);Assert.Equal(2,promoted.Users.Count(x=>x.Role=="Admin" && x.Active));
        await service.UserActionAsync(s.Tenant,s.Admin,"Disable");var updated=await service.GetAsync(s.Tenant);
        Assert.False(updated.Users.Single(x=>x.Id==s.Admin).Active);Assert.True(updated.Users.Single(x=>x.Id==s.Cashier).Active);
    }
    [Fact] public async Task User_actions_cannot_target_another_tenant_or_platform_user()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UserActionAsync(s.Tenant,fixture.UserId,"Disable"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UserActionAsync(s.Tenant,s.Actor.Id,"ResetPassword","ValidPass123!"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UserActionAsync(s.Tenant,s.Cashier,"CopyPermissions",copyFrom:fixture.UserId));
    }
    [Fact] public async Task Copy_reset_unlock_and_force_logout_work_without_elevating_platform_access()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await service.UserActionAsync(s.Tenant,s.Cashier,"CopyPermissions",copyFrom:s.Admin);
        var data=await service.GetAsync(s.Tenant);Assert.True(data.Users.Single(x=>x.Id==s.Cashier).Permissions.Contains("Users.ManagePermissions"));
        Assert.Equal("Cashier",data.Users.Single(x=>x.Id==s.Cashier).Role);
        await service.UserActionAsync(s.Tenant,s.Cashier,"ResetPermissions");
        data=await service.GetAsync(s.Tenant);Assert.False(data.Users.Single(x=>x.Id==s.Cashier).Permissions.Contains("Users.ManagePermissions"));
        await using var db=fixture.CreateContext();var old=await db.Users.IgnoreQueryFilters().Where(x=>x.Id==s.Cashier).Select(x=>x.SecurityStamp).SingleAsync();
        await db.Users.IgnoreQueryFilters().Where(x=>x.Id==s.Cashier).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.LockoutEnd,DateTimeOffset.UtcNow.AddHours(1)).SetProperty(u=>u.AccessFailedCount,5));
        await service.UserActionAsync(s.Tenant,s.Cashier,"Unlock");await service.UserActionAsync(s.Tenant,s.Cashier,"SignOut");
        var user=await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==s.Cashier);Assert.Null(user.LockoutEnd);Assert.Equal(0,user.AccessFailedCount);Assert.NotEqual(old,user.SecurityStamp);Assert.False(user.IsPlatformUser);
    }
    [Fact] public async Task Create_user_respects_capacity_and_role_allowlist_and_does_not_change_current_users()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        var edit=new PlatformUserEdit{Name="New",UserName="new-"+Guid.NewGuid().ToString("N"),Email=Guid.NewGuid()+"@example.test",BranchId=s.Branch,Role=PlatformRoles.Owner,Password="ValidPass123!"};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveUserAsync(s.Tenant,edit));edit.Role="Cashier";
        await service.SaveControlsAsync(s.Tenant,new(){MaxUsers=2});await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveUserAsync(s.Tenant,edit));
        await service.SaveControlsAsync(s.Tenant,new(){MaxUsers=3});await service.SaveUserAsync(s.Tenant,edit);
        var data=await service.GetAsync(s.Tenant);Assert.Equal(3,data.Users.Count);Assert.All(data.Users,u=>Assert.True(u.Active));Assert.Equal("Admin",data.Users.Single(x=>x.Id==s.Admin).Role);
    }
    [Fact] public async Task Password_reset_uses_identity_validation_and_revokes_old_stamp()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UserActionAsync(s.Tenant,s.Cashier,"ResetPassword","x"));
        await using var db=fixture.CreateContext();var before=await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==s.Cashier);
        await service.UserActionAsync(s.Tenant,s.Cashier,"ResetPassword","ValidNewPass123!");
        var after=await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==s.Cashier);Assert.NotEqual(before.SecurityStamp,after.SecurityStamp);
        Assert.NotEqual(PasswordVerificationResult.Failed,new PasswordHasher<ApplicationUser>().VerifyHashedPassword(after,after.PasswordHash!,"ValidNewPass123!"));
        var audit=await db.PlatformAuditLogs.Where(x=>x.TenantId==s.Tenant && x.Action=="TenantUser.ResetPassword").SingleAsync();Assert.DoesNotContain("ValidNewPass",audit.Details??"");
    }
    [Fact] public async Task Readonly_and_feature_flags_are_tenant_scoped_and_enforced_at_database_boundary()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await service.SaveControlsAsync(s.Tenant,new(){ReadOnly=true,DisabledFeaturesJson=JsonSerializer.Serialize(new[]{"Exports","Commissions","BulkImports"})});
        await using var db=fixture.CreateContext();using var tenantScope=db.UseTenantScope(s.Tenant);
        var settings=await TenantOperationalAccess.ReadAsync(db,s.Tenant);var keys=TenantOperationalAccess.Apply(PermissionCatalog.Defaults(["Admin"]),settings);
        Assert.Contains("Customers.View",keys);Assert.DoesNotContain("Customers.Create",keys);Assert.DoesNotContain("Hawalas.Export",keys);Assert.DoesNotContain("IncomingCommission.View",keys);Assert.DoesNotContain("BulkImports.View",keys);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.EnsureCurrentTenantWriteAccessAsync());
        db.Branches.Add(new Branch{Name="Blocked",Code="BLOCK"});await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        Assert.False((await TenantOperationalAccess.ReadAsync(db,1)).ReadOnly);
        await service.SaveControlsAsync(s.Tenant,new());Assert.Empty(TenantOperationalAccess.Disabled(await TenantOperationalAccess.ReadAsync(db,s.Tenant)));
    }
    [Fact] public async Task Readonly_allows_login_bookkeeping_but_not_profile_mutation()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await service.SaveControlsAsync(s.Tenant,new(){ReadOnly=true});
        await using var db=fixture.CreateContext();using var tenantScope=db.UseTenantScope(s.Tenant);
        var user=await db.Users.SingleAsync(x=>x.Id==s.Cashier);user.LastLoginAt=DateTime.UtcNow;user.AccessFailedCount=1;
        using(db.BypassSubscriptionEnforcement()){await db.SaveChangesAsync();}
        user.FullName="Not allowed";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact] public async Task Revoked_live_circuit_stamp_cannot_save_even_with_an_open_context()
    {
        var s=await Seed();await using var setup=fixture.CreateContext();var stamp=await setup.Users.IgnoreQueryFilters().Where(x=>x.Id==s.Cashier).Select(x=>x.SecurityStamp).SingleAsync();
        using var provider=Services(s.Actor,new TestCurrentTenant{TenantId=s.Tenant,UserId=s.Cashier,SessionSecurityStamp=stamp});
        await using var scope=provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();using var bypass=db.BypassSubscriptionEnforcement();
        db.Branches.Add(new Branch{Name="Revoked",Code="REVOKED"});
        await setup.Users.IgnoreQueryFilters().Where(x=>x.Id==s.Cashier).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SecurityStamp,"changed"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>db.SaveChangesAsync());
    }
    [Fact] public async Task Global_search_can_find_tenant_by_user_contact_and_invalid_logo_is_rejected()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        var before=await service.GetAsync(s.Tenant);var user=before.Users.Single(x=>x.Id==s.Cashier);
        Assert.Contains(await service.SearchAsync(user.UserName),x=>x.Id==s.Tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveLogoAsync(s.Tenant,"<svg>unsafe</svg>"u8.ToArray()));
    }
    [Fact] public async Task Concurrent_user_creation_cannot_exceed_the_tenant_limit()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await service.SaveControlsAsync(s.Tenant,new(){MaxUsers=3});
        async Task<bool> Create()
        {
            try{await service.SaveUserAsync(s.Tenant,new(){Name="Concurrent",UserName="parallel-"+Guid.NewGuid().ToString("N"),Email=Guid.NewGuid()+"@example.test",BranchId=s.Branch,Password="ValidPass123!"});return true;}
            catch(InvalidOperationException){return false;}
        }
        var results=await Task.WhenAll(Create(),Create());Assert.Single(results,x=>x);Assert.Equal(3,(await service.GetAsync(s.Tenant)).Users.Count(x=>x.Active));
    }
    [Fact] public async Task Editing_last_admin_to_cashier_is_rolled_back_and_stale_edits_are_rejected()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();var data=await service.GetAsync(s.Tenant);var u=data.Users.Single(x=>x.Id==s.Admin);
        var edit=new PlatformUserEdit{Id=u.Id,Name=u.Name,UserName=u.UserName,Email="admin@example.test",BranchId=u.BranchId,Role="Cashier",ConcurrencyStamp=u.ConcurrencyStamp,Permissions=PermissionCatalog.Defaults(["Cashier"])};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveUserAsync(s.Tenant,edit));Assert.Equal("Admin",(await service.GetAsync(s.Tenant)).Users.Single(x=>x.Id==s.Admin).Role);
        edit.Role="Admin";edit.Permissions=PermissionCatalog.Defaults(["Admin"]);edit.ConcurrencyStamp="stale";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveUserAsync(s.Tenant,edit));
    }
    [Fact] public async Task Settings_edit_cannot_reference_currency_of_another_tenant()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveSettingsAsync(s.Tenant,new CompanySetting{CompanyName="Safe",DefaultProfitCurrencyId=2}));
        await service.SaveSettingsAsync(s.Tenant,new CompanySetting{CompanyName="Updated",PhoneNumber="123",UsePersianCalendar=false});
        var data=await service.GetAsync(s.Tenant);Assert.Equal("Updated",data.Settings!.CompanyName);Assert.False(data.Settings.UsePersianCalendar);
    }
    [Fact] public async Task Retry_queues_the_same_job_once_and_never_reposts_a_posted_batch()
    {
        var s=await Seed();using var provider=Services(s.Actor);var service=provider.GetRequiredService<PlatformTenantManagement>();
        await using var db=fixture.CreateContext();using var scope=db.UseTenantScope(s.Tenant);using var bypass=db.BypassSubscriptionEnforcement();
        var plan=await db.SubscriptionPlans.FirstAsync();db.TenantSubscriptions.Add(new TenantSubscription{TenantId=s.Tenant,PlanId=plan.Id,Status=SubscriptionStatus.Active,StartAt=DateTime.UtcNow.AddDays(-1),EndAt=DateTime.UtcNow.AddDays(30)});
        var correspondent=new Correspondent{Code="TEST",Name="Retry"};db.Correspondents.Add(correspondent);await db.SaveChangesAsync();
        var batch=new HawalaImportBatch{CorrespondentId=correspondent.Id,FileName="retry.xlsx",FileHash=Guid.NewGuid().ToString("N"),CreatedBy=s.Admin};db.HawalaImportBatches.Add(batch);await db.SaveChangesAsync();
        var job=new HawalaImportJob{BatchId=batch.Id,RequestedBy=s.Admin,Status="Failed",RequestJson=JsonSerializer.Serialize(new HawalaExchange.Application.DTOs.ConfirmHawalaImportDto{BatchId=batch.Id})};db.HawalaImportJobs.Add(job);await db.SaveChangesAsync();
        await service.RetryImportAsync(s.Tenant,job.Id);await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RetryImportAsync(s.Tenant,job.Id));
        Assert.Equal(1,await db.HawalaImportJobs.CountAsync(x=>x.BatchId==batch.Id));Assert.Equal("Queued",await db.HawalaImportJobs.AsNoTracking().Where(x=>x.Id==job.Id).Select(x=>x.Status).SingleAsync());
        await db.HawalaImportBatches.Where(x=>x.Id==batch.Id).ExecuteUpdateAsync(x=>x.SetProperty(b=>b.Status,"Posted"));await db.HawalaImportJobs.Where(x=>x.Id==job.Id).ExecuteUpdateAsync(x=>x.SetProperty(j=>j.Status,"Failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RetryImportAsync(s.Tenant,job.Id));Assert.Empty(await db.Hawalas.ToListAsync());
    }
    [Fact] public void Explicit_timezone_changes_timestamp_display_but_not_financial_dates_or_source_values()
    {
        var formatter=new HawalaExchange.Application.Helpers.DisplayDateFormatter();formatter.SetCalendar(false);
        var date=new DateTime(2026,10,15,22,0,0,DateTimeKind.Unspecified);var original=formatter.Date(date);
        formatter.SetTimeZone("Asia/Kabul");Assert.Equal("2026/10/16 02:30",formatter.DateTime(date,true));Assert.Equal(original,formatter.Date(date));Assert.Equal(22,date.Hour);
    }
    [Fact] public async Task Deactivated_tenant_cannot_use_old_circuit_permissions_or_post_new_records()
    {
        var s=await Seed();var current=new TestCurrentTenant{TenantId=s.Tenant,UserId=s.Cashier};using var provider=Services(s.Actor,current);
        using var permissions=new UserPermissionService(provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),current,new(),Microsoft.Extensions.Logging.Abstractions.NullLogger<UserPermissionService>.Instance);
        await permissions.RefreshAsync(true);Assert.True(permissions.Can("Hawalas.Create"));
        await using var db=fixture.CreateContext();await db.Tenants.Where(x=>x.Id==s.Tenant).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.IsActive,false));
        await permissions.RefreshAsync(true);Assert.Empty(permissions.CurrentPermissions);
        using var tenantScope=db.UseTenantScope(s.Tenant);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>db.EnsureCurrentTenantWriteAccessAsync());Assert.Contains("غیرفعال",error.Message);
    }
}
