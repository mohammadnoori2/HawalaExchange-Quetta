using HawalaExchange.Application.Interfaces;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Application.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Web.Services;

/// <summary>One preference load per tenant and circuit/request, with an independent read-only context.</summary>
public sealed class AppDateDisplay(DbContextOptions<ApplicationDbContext> options, ICurrentTenant currentTenant)
    : DisplayDateFormatter
{
    private Task? initialization;
    private long loadedTenantId = -1;

    public Task EnsureLoadedAsync()
    {
        var tenantId = currentTenant.TenantId;
        if (loadedTenantId != tenantId)
        {
            loadedTenantId = tenantId;
            initialization = LoadAsync(tenantId);
        }
        return initialization ?? Task.CompletedTask;
    }

    public Task ReloadAsync()
    {
        loadedTenantId = currentTenant.TenantId;
        initialization = LoadAsync(loadedTenantId);
        return initialization;
    }

    private async Task LoadAsync(long tenantId)
    {
        SetCalendar(true);
        SetTimeZone(null);
        if (tenantId <= 0) return;
        await using var context = new ApplicationDbContext(options, currentTenant);
        var preference = await context.CompanySettings.AsNoTracking()
            .Where(x => x.TenantId == tenantId).Select(x => (bool?)x.UsePersianCalendar).FirstOrDefaultAsync();
        SetCalendar(preference ?? true);
        var zone=await context.Database.SqlQuery<string>($"SELECT TimeZoneId AS [Value] FROM dbo.TenantOperationalControls WHERE TenantId={tenantId}").SingleOrDefaultAsync();
        if(zone is not null)SetTimeZone(zone);
    }
}
