using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class AccountStatementService(ApplicationDbContext context, bool ownsContext = false) : IAccountStatementService, IAsyncDisposable
{
    public ValueTask DisposeAsync() => ownsContext ? context.DisposeAsync() : ValueTask.CompletedTask;
    public async Task<AccountStatementResult> GetAsync(AccountStatementFilter filter, CancellationToken cancellationToken = default)
    {
        if (context.CurrentTenantId <= 0) throw new InvalidOperationException("صرافی جاری مشخص نیست.");
        if (filter.From.Date > filter.To.Date || filter.To.Year >= 9999 || filter.MinAmount < 0 || filter.MaxAmount < 0 || filter.MinAmount > filter.MaxAmount)
            throw new InvalidOperationException("بازه تاریخ یا مبلغ معتبر نیست.");
        using var scope = context.UseTenantScope(context.CurrentTenantId);
        var account = await context.Accounts.AsNoTracking().Where(x => x.Id == filter.AccountId &&
            (x.AccountType == "Customer" || x.AccountType == "Correspondent")).Select(x => new { x.AccountName, x.AccountCode }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("حساب مشتری یا نمایندگی در این صرافی یافت نشد.");
        var start = filter.From.Date.ToUniversalTime(); var end = filter.To.Date.AddDays(1).ToUniversalTime();
        // Recognized commission affects operational balance on the period's closing
        // boundary, not on its older posting date. This preserves opening + movement = closing.
        var query = context.Database.SqlQuery<AccountStatementEntry>($"""
            SELECT e.Id,
                CASE WHEN a.CorrespondentId IS NOT NULL AND t.TransactionType IN
                    (N'PeriodicCorrespondentCommission', N'PeriodicOutgoingCommission', N'PeriodicForwardingCommission', N'PeriodicCorrespondentCommissionReversal')
                    AND p.PeriodTo > e.CreatedAt THEN p.PeriodTo ELSE e.CreatedAt END AS [Date],
                COALESCE(t.TransactionNo, CONVERT(nvarchar(30), h.Number), CONVERT(nvarchar(30), e.Id)) AS Document,
                COALESCE(e.Description, N'') AS Description, c.Code AS Currency,
                e.TalabKar AS Credit, e.BadehKar AS Debit,
                CAST(CASE WHEN a.CorrespondentId IS NOT NULL AND t.TransactionType IN
                    (N'PeriodicCorrespondentCommission', N'PeriodicOutgoingCommission', N'PeriodicForwardingCommission', N'PeriodicCorrespondentCommissionReversal')
                    AND (p.Id IS NULL OR p.PeriodTo >= {end}) THEN 1 ELSE 0 END AS bit) AS Pending,
                CAST(0 AS decimal(18,4)) AS Balance
            FROM dbo.LedgerEntries e
            JOIN dbo.Accounts a ON a.Id = e.AccountId AND a.TenantId = e.TenantId
            JOIN dbo.Currencies c ON c.Id = e.CurrencyId AND c.TenantId = e.TenantId
            LEFT JOIN dbo.Transactions t ON t.Id = e.TransactionId AND t.TenantId = e.TenantId
            LEFT JOIN dbo.Hawalas h ON h.Id = e.HawalaId AND h.TenantId = e.TenantId
            LEFT JOIN dbo.CorrespondentCommissionRecognitions r ON r.AccountId = e.AccountId AND r.TransactionId = e.TransactionId AND r.TenantId = e.TenantId
            LEFT JOIN dbo.CorrespondentAccountPeriods p ON p.Id = r.PeriodId AND p.TenantId = r.TenantId
            WHERE e.TenantId = {context.CurrentTenantId} AND e.AccountId = {filter.AccountId} AND e.CreatedAt < {end}
            """);
        if (filter.Currency != "") query = query.Where(x => x.Currency == filter.Currency);
        var opening = await query.Where(x => !x.Pending && x.Date < start).GroupBy(x => x.Currency)
            .Select(g => new { Currency = g.Key, Amount = g.Sum(x => x.Credit - x.Debit) }).ToListAsync(cancellationToken);
        var asOf = end.AddTicks(-1);
        var pendingQuery = context.Database.SqlQuery<PendingRow>($"""
            SELECT c.Code AS Currency, p.PendingCommissionCredit AS Credit, p.PendingCommissionDebit AS Debit
            FROM dbo.ufn_PendingCorrespondentCommissions_v1({context.CurrentTenantId}, {filter.AccountId}, {asOf}) p
            JOIN dbo.Currencies c ON c.Id = p.CurrencyId AND c.TenantId = {context.CurrentTenantId}
            """);
        if (filter.Currency != "") pendingQuery = pendingQuery.Where(x => x.Currency == filter.Currency);
        var pending = await pendingQuery.ToListAsync(cancellationToken);
        var entries = await query.Where(x => !x.Pending && x.Date >= start && x.Date < end).OrderBy(x => x.Date).ThenBy(x => x.Id).Take(20001).ToListAsync(cancellationToken);
        if (entries.Count > 20000) throw new InvalidOperationException("بیش از 20000 ثبت یافت شد؛ بازه را محدود کنید. صورت‌حساب ناقص صادر نمی‌شود.");
        var result = new AccountStatementResult { AccountName = account.AccountName, AccountCode = account.AccountCode };
        foreach (var currency in opening.Select(x => x.Currency).Concat(pending.Select(x => x.Currency)).Concat(entries.Select(x => x.Currency)).Distinct().Order())
        {
            var rows = entries.Where(x => x.Currency == currency).ToList();
            var summary = new AccountStatementSummary { Currency = currency, Opening = opening.FirstOrDefault(x => x.Currency == currency)?.Amount ?? 0,
                Credit = rows.Sum(x => x.Credit), Debit = rows.Sum(x => x.Debit), PendingCredit = pending.FirstOrDefault(x => x.Currency == currency)?.Credit ?? 0,
                PendingDebit = pending.FirstOrDefault(x => x.Currency == currency)?.Debit ?? 0 };
            var running = summary.Opening;
            foreach (var row in rows) { running += row.Credit - row.Debit; row.Balance = running; }
            result.Summaries.Add(summary);
        }
        // Detail filters do not redefine accounting balances. Keep the full-period
        // summaries and running balances, even when some detail lines are hidden.
        var search = filter.Search.Trim();
        result.Entries = entries.Where(x => (search == "" || x.Document.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (filter.Direction == "" || (filter.Direction == "Credit" ? x.Credit > 0 : x.Debit > 0)) &&
            (!filter.MinAmount.HasValue || Math.Max(x.Credit, x.Debit) >= filter.MinAmount) &&
            (!filter.MaxAmount.HasValue || Math.Max(x.Credit, x.Debit) <= filter.MaxAmount)).ToList();
        return result;
    }
    public sealed class PendingRow { public string Currency { get; set; } = ""; public decimal Credit { get; set; } public decimal Debit { get; set; } }
}
