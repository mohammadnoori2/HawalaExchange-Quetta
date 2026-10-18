using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

/// <summary>Reads existing activity timestamps; does not manufacture activity by viewing the platform.</summary>
public static class TenantActivityReader
{
    public static async Task<Dictionary<long, DateTime?>> ReadAsync(ApplicationDbContext db,
        IEnumerable<long> tenantIds, CancellationToken cancellationToken = default)
    {
        var ids = tenantIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        // One SQL request for the whole list. Every ignored filter is replaced by
        // an explicit tenant predicate; platform logins are not exchange activity.
        var rows = await db.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                Recorded = t.LastActivityAt,
                Login = db.Users.IgnoreQueryFilters().Where(u => u.TenantId == t.Id && !u.IsPlatformUser).Max(u => u.LastLoginAt),
                Transaction = db.Transactions.IgnoreQueryFilters().Where(x => x.TenantId == t.Id).Max(x => (DateTime?)x.CreatedAt),
                Audit = db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == t.Id).Max(x => (DateTime?)x.CreatedAt)
            }).ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.Id, x => Latest(x.Recorded, x.Login, x.Transaction, x.Audit));
    }

    public static DateTime? Latest(params DateTime?[] values) => values.Where(x => x.HasValue && x.Value != DateTime.MinValue).Max();
}
