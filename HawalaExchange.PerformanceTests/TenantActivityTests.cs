using HawalaExchange.Infrastructure.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class TenantActivityTimestampTests
{
    [Fact] public void Missing_activity_stays_unknown()=>Assert.Null(TenantActivityReader.Latest(null,DateTime.MinValue,null));
    [Fact] public void Latest_existing_event_wins_regardless_of_source()
    {
        var now=new DateTime(2026,10,16,12,0,0,DateTimeKind.Utc);
        Assert.Equal(now,TenantActivityReader.Latest(now.AddDays(-5),now.AddHours(-2),now,null));
        Assert.Equal(now,TenantActivityReader.Latest(now,null,now.AddDays(-1)));
    }
}

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class TenantActivityDatabaseTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Activity_is_tenant_scoped_excludes_platform_logins_and_is_read_only()
    {
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();
        var first=new Tenant{Name="Activity test "+Guid.NewGuid().ToString("N")};
        var second=new Tenant{Name="Unrelated activity "+Guid.NewGuid().ToString("N")};
        var empty=new Tenant{Name="No activity "+Guid.NewGuid().ToString("N")};
        db.Tenants.AddRange(first,second,empty);await db.SaveChangesAsync();
        var expected=new DateTime(2026,10,15,10,30,0,DateTimeKind.Utc);
        using(db.UseTenantScope(first.Id))
        {
            var branch=new Branch{Name="Activity branch",Code="ACTIVITY"};db.Branches.Add(branch);await db.SaveChangesAsync();
            db.Users.Add(new ApplicationUser{TenantId=first.Id,BranchId=branch.Id,UserName=Guid.NewGuid().ToString("N"),LocalUserName=Guid.NewGuid().ToString("N"),FullName="Exchange user",LastLoginAt=expected});
            db.Users.Add(new ApplicationUser{TenantId=first.Id,UserName=Guid.NewGuid().ToString("N"),LocalUserName=Guid.NewGuid().ToString("N"),FullName="Platform actor",IsPlatformUser=true,LastLoginAt=expected.AddDays(5)});
            await db.SaveChangesAsync();
        }
        using(db.UseTenantScope(second.Id))
        {
            var branch=new Branch{Name="Other branch",Code="ACTIVITY"};db.Branches.Add(branch);await db.SaveChangesAsync();
            db.Users.Add(new ApplicationUser{TenantId=second.Id,BranchId=branch.Id,UserName=Guid.NewGuid().ToString("N"),LocalUserName=Guid.NewGuid().ToString("N"),FullName="Other exchange",LastLoginAt=expected.AddDays(10)});
            await db.SaveChangesAsync();
        }
        // Remove only setup-generated audits in this disposable test database.
        await db.AuditLogs.IgnoreQueryFilters().Where(x=>x.TenantId==first.Id||x.TenantId==second.Id||x.TenantId==empty.Id).ExecuteDeleteAsync();
        var before=await db.Tenants.AsNoTracking().Where(x=>x.Id==first.Id).Select(x=>x.LastActivityAt).SingleAsync();
        var rows=await TenantActivityReader.ReadAsync(db,[first.Id,empty.Id]);
        Assert.Equal(expected,rows[first.Id]);Assert.Null(rows[empty.Id]);Assert.False(rows.ContainsKey(second.Id));
        Assert.Equal(before,await db.Tenants.AsNoTracking().Where(x=>x.Id==first.Id).Select(x=>x.LastActivityAt).SingleAsync());
        var listed=(await new SaasAdministrationService(db).GetTenantsAsync()).Single(x=>x.Id==first.Id);
        Assert.Equal(expected,listed.LastActivityAt);
        using(db.UseTenantScope(first.Id))
        {
            db.AuditLogs.Add(new AuditLog{TenantId=first.Id,Action="Update",TableName="ActivityTest",RecordId=1,ProcessId=Guid.NewGuid(),CreatedAt=expected.AddHours(1)});
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected.AddHours(1),(await TenantActivityReader.ReadAsync(db,[first.Id]))[first.Id]);
        await db.Tenants.Where(x=>x.Id==first.Id).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.LastActivityAt,expected.AddHours(2)));
        Assert.Equal(expected.AddHours(2),(await TenantActivityReader.ReadAsync(db,[first.Id]))[first.Id]);
    }
}
