using System.Security.Claims;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Services;

public sealed record PlatformActivityChoice(long Id, string Name, bool Archived);
public sealed record PlatformTenantActivity(long TenantId, string Name, bool Archived, long Transactions,
    long Hawalas, long Changes, long ActiveUsers, DateTime? LastOperationAt);
public sealed record PlatformActivityType(string Code, string Label, long Count);
public sealed record PlatformDailyActivity(DateTime Day, long Transactions, long Hawalas, long Changes, long ActiveUsers)
{
    public IReadOnlyList<PlatformActivityType> Types { get; init; } = [];
}
public sealed record PlatformActivityReport(IReadOnlyList<PlatformActivityChoice> Choices,
    IReadOnlyList<PlatformTenantActivity> Tenants, IReadOnlyList<PlatformDailyActivity> Days)
{
    public IReadOnlyList<PlatformActivityType> Types { get; init; } = [];
    public long Transactions => Tenants.Sum(x => x.Transactions);
    public long Hawalas => Tenants.Sum(x => x.Hawalas);
    public long Changes => Tenants.Sum(x => x.Changes);
    public long ActiveUsers => Tenants.Sum(x => x.ActiveUsers);
    public int ActiveTenants => Tenants.Count(x => x.Transactions > 0 || x.Changes > 0);
}

/// <summary>Read-only platform usage counts. No financial details, browsing telemetry or new audit events.</summary>
public sealed class PlatformActivityService(IDbContextFactory<ApplicationDbContext> factory,
    AuthenticationStateProvider authentication, IOptions<IdentityOptions> identityOptions)
{
    public async Task<PlatformActivityReport> GetAsync(DateTime fromDate, DateTime toDate, long? tenantId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var db = await factory.CreateDbContextAsync(ct);
        // Recheck the live identity and database role on every read, including an existing Blazor circuit.
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true || ImpersonationIdentity.IsActive(principal) ||
            !principal.HasClaim("platform_user", "true") ||
            !long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("مشاهده فعالیت صرافی‌ها فقط برای کاربران پلتفرم مجاز است.");
        var actor = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive && x.IsPlatformUser)
            .Select(x => new { x.SecurityStamp }).SingleOrDefaultAsync(ct);
        if (actor is null || string.IsNullOrWhiteSpace(actor.SecurityStamp) ||
            actor.SecurityStamp != principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType) ||
            !await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id
                    where ur.UserId == userId && PlatformRoles.All.Contains(role.Name!) select ur.UserId).AnyAsync(ct))
            throw new UnauthorizedAccessException("نشست یا صلاحیت کاربر پلتفرم معتبر نیست.");

        var from = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Utc);
        var last = DateTime.SpecifyKind(toDate.Date, DateTimeKind.Utc);
        if (last < from || (last - from).TotalDays >= 366 || last == DateTime.MaxValue.Date || tenantId is <= 0)
            throw new ArgumentException("بازه تاریخ باید معتبر و حداکثر 366 روز باشد.");
        var until = last.AddDays(1); // Inclusive selected end date, exclusive SQL boundary.
        var choices = await db.Tenants.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new PlatformActivityChoice(x.Id, x.Name, x.IsArchived)).ToListAsync(ct);
        if (tenantId.HasValue && !choices.Any(x => x.Id == tenantId))
            throw new ArgumentException("صرافی انتخاب‌شده موجود نیست.");

        // Each query aggregates on SQL Server. Explicit tenant predicates replace ignored tenant filters.
        // There is no query per tenant/day and no loading of audit payloads, amounts or customer data.
        var tenants = db.Tenants.AsNoTracking().Where(x => !tenantId.HasValue || x.Id == tenantId.Value);
        var transactions = db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt < until && tenants.Any(t => t.Id == x.TenantId));
        var cash = db.AccountMoneyOperations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedAt >= from && x.CreatedAt < until && tenants.Any(t => t.Id == x.TenantId));
        var exchanges = db.MoneyExchangeOperations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedAt >= from && x.CreatedAt < until && tenants.Any(t => t.Id == x.TenantId));
        var expenses = db.Expenses.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedAt >= from && x.CreatedAt < until && tenants.Any(t => t.Id == x.TenantId));
        var capital = db.CapitalInvestments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedAt >= from && x.CreatedAt < until && tenants.Any(t => t.Id == x.TenantId));
        // These operation tables do not create a Transactions row; concatenate once, never sum audit events into them.
        var financial = transactions.Select(x => new { x.TenantId, x.CreatedAt, x.CreatedBy, Type = x.TransactionType })
            .Concat(cash.Select(x => new { x.TenantId, x.CreatedAt, x.CreatedBy, Type = x.OperationType == "Deposit" ? "Deposit" : x.OperationType == "Withdraw" ? "Withdraw" : "Other" }))
            .Concat(exchanges.Select(x => new { x.TenantId, x.CreatedAt, x.CreatedBy, Type = "Exchange" }))
            .Concat(expenses.Select(x => new { x.TenantId, x.CreatedAt, x.CreatedBy, Type = "Expense" }))
            .Concat(capital.Select(x => new { x.TenantId, x.CreatedAt, x.CreatedBy, Type = x.IsWithdrawal ? "OwnerWithdrawal" : "CapitalInvestment" }));
        var audits = db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt < until && tenants.Any(t => t.Id == x.TenantId));
        // Keep overview results O(tenants * types + days * types), never tenants * days * types.
        var transactionGroups = await financial.GroupBy(x => new { x.TenantId, x.Type })
            .Select(g => new { g.Key.TenantId, g.Key.Type, Count = g.LongCount(),
                Last = g.Max(x => x.CreatedAt) }).ToListAsync(ct);
        var transactionDays = await financial.GroupBy(x => new { Day = x.CreatedAt.Date, x.Type })
            .Select(g => new { g.Key.Day, g.Key.Type, Count = g.LongCount() }).ToListAsync(ct);
        var auditGroups = await audits.GroupBy(x => x.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.LongCount(), Last = g.Max(x => x.CreatedAt) }).ToListAsync(ct);
        var auditDays = await audits.GroupBy(x => x.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.LongCount() }).ToListAsync(ct);

        // Count a user once in the whole period, not once per day. Ignore platform and system actors.
        var operations = financial.Select(x => new { x.TenantId, UserId = x.CreatedBy, Day = x.CreatedAt.Date }).Distinct()
            .Union(audits.Where(x => x.UserId.HasValue)
                .Select(x => new { x.TenantId, UserId = x.UserId!.Value, Day = x.CreatedAt.Date }));
        var userOperations = operations.Where(x => db.Users.IgnoreQueryFilters()
            .Any(u => u.Id == x.UserId && u.TenantId == x.TenantId && !u.IsPlatformUser));
        var users = await userOperations.Select(x => new { x.TenantId, x.UserId }).Distinct()
            .GroupBy(x => x.TenantId).Select(g => new { TenantId = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var userDays = await userOperations.GroupBy(x => x.Day)
            .Select(g => new { Day = g.Key, Count = g.LongCount() }).ToListAsync(ct);

        var transactionTotals = transactionGroups.ToLookup(x => x.TenantId);
        var auditTotals = auditGroups.ToLookup(x => x.TenantId);
        var userTotals = users.ToDictionary(x => x.TenantId, x => x.Count);
        var rows = choices.Where(x => !tenantId.HasValue || x.Id == tenantId.Value).Select(t =>
            new PlatformTenantActivity(t.Id, t.Name, t.Archived,
                transactionTotals[t.Id].Sum(x => x.Count), transactionTotals[t.Id].Where(x => IsHawala(x.Type)).Sum(x => x.Count),
                auditTotals[t.Id].Sum(x => x.Count), userTotals.GetValueOrDefault(t.Id),
                transactionTotals[t.Id].Select(x => (DateTime?)x.Last).Concat(auditTotals[t.Id].Select(x => (DateTime?)x.Last)).Max()))
            .OrderByDescending(x => x.Transactions).ThenByDescending(x => x.Changes).ThenBy(x => x.Name).ThenBy(x => x.TenantId).ToList();
        var txByDay = transactionDays.ToLookup(x => x.Day);
        var auditByDay = auditDays.ToLookup(x => x.Day);
        var usersByDay = userDays.ToLookup(x => x.Day);
        var days = Enumerable.Range(0, (last - from).Days + 1).Select(offset => from.AddDays(offset))
            .Select(day => new PlatformDailyActivity(DateTime.SpecifyKind(day, DateTimeKind.Unspecified),
                txByDay[day].Sum(x => x.Count), txByDay[day].Where(x => IsHawala(x.Type)).Sum(x => x.Count),
                auditByDay[day].Sum(x => x.Count), usersByDay[day].Sum(x => x.Count))
                { Types = Breakdown(txByDay[day].Select(x => (x.Type, x.Count))) }).ToList();
        return new PlatformActivityReport(choices, rows, days)
            { Types = Breakdown(transactionGroups.Select(x => (x.Type, x.Count))) };
    }

    private static bool IsHawala(string type) => type is "HawalaSend" or "HawalaReceive";
    private static readonly (string Code, string Label)[] Categories = [
        ("HawalaReceive", "حواله دریافتی"), ("HawalaSend", "حواله ارسالی"), ("Deposit", "رسید"),
        ("Withdraw", "برد"), ("Exchange", "تبادله اسعار"), ("Expense", "مصارف"), ("Transfer", "انتقال"),
        ("CapitalInvestment", "ثبت سرمایه"), ("OwnerWithdrawal", "برداشت مالک"),
        ("OpeningBalance", "مانده افتتاحیه"), ("Adjustment", "تعدیل حساب"), ("Other", "سایر معاملات")];
    private static IReadOnlyList<PlatformActivityType> Breakdown(IEnumerable<(string Type, long Count)> values)
    {
        var totals = values.GroupBy(x => Categories.Any(c => c.Code == x.Type) ? x.Type : "Other")
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        return Categories.Select(c => new PlatformActivityType(c.Code, c.Label, totals.GetValueOrDefault(c.Code))).ToArray();
    }
}
