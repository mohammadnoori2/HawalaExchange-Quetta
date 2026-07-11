using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class JournalService : IJournalService
{
    private readonly ApplicationDbContext _context;

    public JournalService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DailyJournalDto> GetDailyJournalAsync(DateTime journalDate)
    {
        var localStart = journalDate.Date;
        var localEnd = localStart.AddDays(1);

        var utcStart = localStart.ToUniversalTime();
        var utcEnd = localEnd.ToUniversalTime();

        var ledgerEntries = await _context.LedgerEntries
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Where(x => x.CreatedAt >= utcStart && x.CreatedAt < utcEnd)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var entries = ledgerEntries.Select(x => new JournalEntryDto
        {
            Id = x.Id,
            CreatedAt = x.CreatedAt,
            SourceType = GetSourceType(x),
            SourceId = GetSourceId(x),

            AccountId = x.AccountId,
            AccountCode = x.Account?.AccountCode ?? "",
            AccountName = x.Account?.AccountName ?? "",

            CurrencyId = x.CurrencyId,
            CurrencyCode = x.Currency?.Code ?? "",

            TalabKar = x.TalabKar,
            BadehKar = x.BadehKar,
            Description = x.Description
        }).ToList();

        var summaries = entries
            .GroupBy(x => new { x.CurrencyId, x.CurrencyCode })
            .Select(g => new DailyJournalCurrencySummaryDto
            {
                CurrencyId = g.Key.CurrencyId,
                CurrencyCode = g.Key.CurrencyCode,
                EntriesCount = g.Count(),
                TotalTalabKar = g.Sum(x => x.TalabKar),
                TotalBadehKar = g.Sum(x => x.BadehKar)
            })
            .OrderBy(x => x.CurrencyCode)
            .ToList();

        return new DailyJournalDto
        {
            JournalDate = journalDate.Date,
            Entries = entries,
            CurrencySummaries = summaries
        };
    }

    private static string GetSourceType(HawalaExchange.Domain.Entities.LedgerEntry entry)
    {
        if (entry.HawalaId.HasValue)
            return "حواله";

        if (entry.CapitalInvestmentId.HasValue)
            return "ثبت سرمایه";

        if (entry.ExpenseId.HasValue)
            return "مصرف";

        if (entry.AccountMoneyOperationId.HasValue)
            return "واریز / برداشت";

        if (entry.MoneyExchangeOperationId.HasValue)
            return "تبدیل پول";

        if (entry.TransferId.HasValue)
            return "انتقال";

        if (entry.TransactionId.HasValue)
            return "تراکنش";

        return "ثبت دستی";
    }

    private static long? GetSourceId(HawalaExchange.Domain.Entities.LedgerEntry entry)
    {
        if (entry.HawalaId.HasValue)
            return entry.HawalaId;

        if (entry.CapitalInvestmentId.HasValue)
            return entry.CapitalInvestmentId;

        if (entry.ExpenseId.HasValue)
            return entry.ExpenseId;

        if (entry.AccountMoneyOperationId.HasValue)
            return entry.AccountMoneyOperationId;

        if (entry.MoneyExchangeOperationId.HasValue)
            return entry.MoneyExchangeOperationId;

        if (entry.TransferId.HasValue)
            return entry.TransferId;

        if (entry.TransactionId.HasValue)
            return entry.TransactionId;

        return null;
    }
}