using System.Text.Json;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class TenantControlSettings
{
    public bool ReadOnly { get; set; }
    public int? MaxUsers { get; set; }
    public string DisabledFeaturesJson { get; set; } = "[]";
    public string TimeZoneId { get; set; } = "Asia/Kabul";
}

public static class TenantOperationalAccess
{
    public static readonly IReadOnlyDictionary<string,string> Features = new Dictionary<string,string>
    {
        ["BulkImports"]="آپلود گروهی", ["Commissions"]="محاسبه کمیشن", ["Exports"]="خروجی، چاپ و اشتراک",
        ["Documents"]="اسناد", ["Settlement"]="تبدیل به ارز توافقی", ["HawalaExecution"]="اجرای حواله"
    };
    public static async Task<TenantControlSettings> ReadAsync(ApplicationDbContext db,long tenantId,CancellationToken ct=default) =>
        await db.Database.SqlQuery<TenantControlSettings>($"SELECT ReadOnly,MaxUsers,DisabledFeaturesJson,TimeZoneId FROM dbo.TenantOperationalControls WHERE TenantId={tenantId}")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new();

    public static HashSet<string> Disabled(TenantControlSettings settings) =>
        JsonSerializer.Deserialize<HashSet<string>>(settings.DisabledFeaturesJson) ?? [];

    public static HashSet<string> Apply(HashSet<string> keys,TenantControlSettings settings)
    {
        var disabled=Disabled(settings);
        return keys.Where(key=>
            (!settings.ReadOnly || key.EndsWith(".View") || key.EndsWith(".Export")) &&
            !(disabled.Contains("Exports") && key.EndsWith(".Export")) &&
            !disabled.Contains(key.Split('.')[0]) &&
            !(disabled.Contains("Commissions") && key.Split('.')[0] is "IncomingCommission" or "DestinationCommission" or "ForwardingCommission"))
            .ToHashSet(StringComparer.Ordinal);
    }
    public static async Task EnsureWritableAsync(ApplicationDbContext db,long tenantId,CancellationToken ct=default)
    {
        if(!await db.Tenants.AsNoTracking().AnyAsync(x=>x.Id==tenantId && x.IsActive && !x.IsArchived,ct).ConfigureAwait(false))
            throw new InvalidOperationException("صرافی غیرفعال یا آرشیف شده است.");
        if((await ReadAsync(db,tenantId,ct).ConfigureAwait(false)).ReadOnly)
            throw new InvalidOperationException("عملیات این صرافی موقتاً قفل است؛ مشاهده و گزارش‌گیری مجاز است.");
    }
    public static async Task EnsureUserCapacityAsync(ApplicationDbContext db,long tenantId,CancellationToken ct=default)
    {
        var settings=await ReadAsync(db,tenantId,ct);
        var plan=await db.TenantSubscriptions.Where(x=>x.TenantId==tenantId).OrderByDescending(x=>x.StartAt).ThenByDescending(x=>x.Id)
            .Select(x=>(int?)x.Plan.MaxUsers).FirstOrDefaultAsync(ct);
        var limits=new[]{settings.MaxUsers,plan}.Where(x=>x>0).Select(x=>x!.Value).ToArray();
        if(limits.Length==0)return;
        var count=await db.Users.IgnoreQueryFilters().CountAsync(x=>x.TenantId==tenantId && !x.IsPlatformUser && x.IsActive,ct);
        if(count>=limits.Min())throw new InvalidOperationException($"سقف کاربران فعال این صرافی ({limits.Min()}) تکمیل شده است.");
    }
}
