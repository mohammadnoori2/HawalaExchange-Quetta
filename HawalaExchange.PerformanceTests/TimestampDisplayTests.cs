using HawalaExchange.Application.Helpers;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class TimestampDisplayTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public async Task Missing_timezone_displays_utc_timestamps_in_Kabul_before_and_after_loading(DateTimeKind kind)
    {
        var instant=new DateTime(2026,10,25,5,1,59,DateTimeKind.Utc);
        var value=kind==DateTimeKind.Local?instant.ToLocalTime():DateTime.SpecifyKind(instant,kind);
        var display=new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,new TestCurrentTenant{TenantId=0});
        display.SetCalendar(false);
        Assert.Equal("2026/10/25 09:31:59",display.DateTime(value,true,true));
        await display.EnsureLoadedAsync();display.SetCalendar(false);
        Assert.Equal("2026/10/25 09:31:59",display.DateTime(value,true,true));
        Assert.Equal(kind,value.Kind);
    }

    [Fact]
    public async Task Kabul_midnight_changes_timestamp_day_but_never_financial_date_only_values()
    {
        var display=new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,new TestCurrentTenant{TenantId=0});
        await display.EnsureLoadedAsync();display.SetCalendar(false);
        var stored=new DateTime(2026,10,25,22,0,0,DateTimeKind.Unspecified);
        Assert.Equal("2026/10/26 02:30",display.DateTime(stored,true));
        Assert.Equal("2026/10/26",display.TimestampDate(stored));
        Assert.Equal("2026/10/25",display.Date(stored));
        Assert.Equal("2026/10/25",display.Date(new DateOnly(2026,10,25)));
        display.SetCalendar(true);
        Assert.Equal("1405/08/04 02:30 AM",display.DateTime(stored));
        Assert.Equal("1405/08/04",display.TimestampDate(stored));
        Assert.Equal(22,stored.Hour);
        Assert.Equal("—",display.DateTime(null));
        Assert.Equal("missing",display.DateTime(DateTime.MinValue,emptyText:"missing"));
        Assert.Equal("—",display.TimestampDate(null));
        Assert.Equal("missing",display.TimestampDate(DateTime.MinValue,"missing"));
    }

    [Theory]
    [InlineData("Asia/Karachi","2026/10/25 10:01")]
    [InlineData("UTC","2026/10/25 05:01")]
    public void Explicit_tenant_timezone_overrides_Kabul_without_double_converting_local_values(string zone,string expected)
    {
        var display=new DisplayDateFormatter();display.SetCalendar(false);display.SetTimeZone(zone);
        var instant=new DateTime(2026,10,25,5,1,0,DateTimeKind.Utc);
        Assert.Equal(expected,display.DateTime(instant,true));
        Assert.Equal(expected,display.DateTime(instant.ToLocalTime(),true));
    }
}

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class TimestampPreferenceLoadingTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Missing_operational_controls_use_Kabul_without_writing_a_preference_or_hawala()
    {
        await using var db=fixture.CreateContext();
        var tenant=new HawalaExchange.Domain.Entities.Tenant {Name="Timestamp display test "+Guid.NewGuid().ToString("N"),IsActive=true};
        db.Tenants.Add(tenant);await db.SaveChangesAsync();
        var countBefore=await db.Hawalas.IgnoreQueryFilters().CountAsync();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        var display=new AppDateDisplay(options,new TestCurrentTenant{TenantId=tenant.Id});
        await display.EnsureLoadedAsync();display.SetCalendar(false);
        Assert.Equal("2026/10/25 09:31",display.DateTime(new DateTime(2026,10,25,5,1,0),true));
        await display.ReloadAsync();display.SetCalendar(false);
        Assert.Equal("2026/10/25 09:31",display.DateTime(new DateTime(2026,10,25,5,1,0),true));
        Assert.Equal(0,await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.TenantOperationalControls WHERE TenantId={tenant.Id}").SingleAsync());
        Assert.Equal(countBefore,await db.Hawalas.IgnoreQueryFilters().CountAsync());
        // A saved preference must win, including after refresh; blank legacy values use Kabul.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.TenantOperationalControls(TenantId,ReadOnly,MaxUsers,DisabledFeaturesJson,TimeZoneId) VALUES({tenant.Id},0,1000,N'[]',N'UTC')");
        await display.ReloadAsync();display.SetCalendar(false);
        Assert.Equal("2026/10/25 05:01",display.DateTime(new DateTime(2026,10,25,5,1,0),true));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.TenantOperationalControls SET TimeZoneId=N' ' WHERE TenantId={tenant.Id}");
        await display.ReloadAsync();display.SetCalendar(false);
        Assert.Equal("2026/10/25 09:31",display.DateTime(new DateTime(2026,10,25,5,1,0),true));
    }
}
