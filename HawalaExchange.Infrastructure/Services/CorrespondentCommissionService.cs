using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class CorrespondentCommissionService(ApplicationDbContext context)
    : ICorrespondentCommissionService
{
    public async Task<CorrespondentCommissionPreviewDto> PreviewAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken = default) =>
        await CalculateAsync(request, requireAllRates: false, cancellationToken);

    public async Task<CorrespondentCommissionBatchDto> PostAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var preview = await CalculateAsync(request, requireAllRates: true, cancellationToken);
        if (preview.HawalaCount == 0)
            throw new InvalidOperationException("حواله محاسبه‌نشده‌ای در این دوره وجود ندارد.");
        if (preview.TotalCommissionUsd <= 0)
            throw new InvalidOperationException("کمیشن نهایی پس از گردکردن کمتر از یک دالر است و قابل ثبت نیست.");

        var correspondent = await GetPeriodicCorrespondentAsync(request.CorrespondentId, cancellationToken);
        var usd = await context.Currencies.SingleOrDefaultAsync(x => x.Code == "USD" && x.IsActive, cancellationToken)
                  ?? throw new InvalidOperationException("ارز فعال USD در سیستم یافت نشد.");
        var correspondentAccount = await context.Accounts.SingleOrDefaultAsync(
            x => x.CorrespondentId == correspondent.Id && !x.IsArchived, cancellationToken)
            ?? throw new InvalidOperationException("حساب فعال نمایندگی یافت نشد.");
        var incomeAccount = await GetOrCreateCommissionIncomeAccountAsync(cancellationToken);
        var transaction = new Transaction
        {
            TransactionNo = await GenerateTransactionNumberAsync("PC", cancellationToken),
            TransactionType = "PeriodicCorrespondentCommission",
            BranchId = await context.GetDefaultBranchIdAsync(cancellationToken),
            Status = "Paid",
            Remarks = $"کمیشن دوره‌ای نمایندگی {correspondent.Name} از {request.PeriodFrom:yyyy-MM-dd} تا {request.PeriodTo:yyyy-MM-dd}",
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);

        var batch = new CorrespondentCommissionBatch
        {
            CorrespondentId = correspondent.Id,
            PeriodFrom = request.PeriodFrom.Date,
            PeriodTo = request.PeriodTo.Date,
            CommissionPerLakhAfn = request.CommissionPerLakhAfn,
            UsdToAfnRate = request.UsdToAfnRate,
            TotalBaseAfn = preview.TotalBaseAfn,
            TotalCommissionAfn = preview.TotalCommissionAfn,
            TotalCommissionUsd = preview.TotalCommissionUsd,
            PostingTransactionId = transaction.Id,
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow,
            Status = "Posted"
        };
        foreach (var item in preview.Items)
        {
            batch.Items.Add(new CorrespondentCommissionBatchItem
            {
                HawalaId = item.HawalaId,
                SourceCurrencyId = item.CurrencyId,
                SourceAmount = item.SourceAmount,
                SourceToAfnRate = item.SourceToAfnRate,
                AfnEquivalent = item.AfnEquivalent,
                CommissionAfn = item.CommissionAfn,
                IsActive = true
            });
        }
        context.CorrespondentCommissionBatches.Add(batch);

        var description = $"کمیشن دوره‌ای نمایندگی {correspondent.Name}، {preview.HawalaCount} حواله";
        context.LedgerEntries.AddRange(
            NewEntry(transaction.Id, correspondentAccount.Id, usd.Id, 0, preview.TotalCommissionUsd, description),
            NewEntry(transaction.Id, incomeAccount.Id, usd.Id, preview.TotalCommissionUsd, 0, description));
        await context.SaveChangesAsync(cancellationToken);

        context.AuditLogs.Add(new AuditLog
        {
            UserId = context.RequireCurrentUserId(), Action = "POST_PERIODIC_COMMISSION",
            TableName = "CorrespondentCommissionBatches", RecordId = batch.Id,
            NewValue = $"کمیشن {preview.HawalaCount} حواله به مبلغ {preview.TotalCommissionUsd} USD ثبت شد.",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return Map(batch, correspondent.Name);
    }

    public async Task<IReadOnlyList<CorrespondentCommissionBatchDto>> GetHistoryAsync(
        long correspondentId,
        CancellationToken cancellationToken = default) =>
        await context.CorrespondentCommissionBatches.AsNoTracking()
            .Where(x => x.CorrespondentId == correspondentId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CorrespondentCommissionBatchDto
            {
                Id = x.Id, CorrespondentId = x.CorrespondentId, CorrespondentName = x.Correspondent.Name,
                PeriodFrom = x.PeriodFrom, PeriodTo = x.PeriodTo, HawalaCount = x.Items.Count,
                CommissionPerLakhAfn = x.CommissionPerLakhAfn, UsdToAfnRate = x.UsdToAfnRate,
                TotalBaseAfn = x.TotalBaseAfn, TotalCommissionAfn = x.TotalCommissionAfn,
                TotalCommissionUsd = x.TotalCommissionUsd, Status = x.Status, CreatedAt = x.CreatedAt
            }).ToListAsync(cancellationToken);

    public async Task ReverseAsync(long batchId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("دلیل برگشت الزامی است.");
        if (reason.Trim().Length > 500)
            throw new InvalidOperationException("دلیل برگشت نمی‌تواند بیشتر از ۵۰۰ حرف باشد.");
        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var batch = await context.CorrespondentCommissionBatches
            .Include(x => x.Correspondent).Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new KeyNotFoundException("محاسبه کمیشن یافت نشد.");
        if (batch.Status != "Posted")
            throw new InvalidOperationException("این محاسبه قبلاً برگشت داده شده است.");

        var originalEntries = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == batch.PostingTransactionId).ToListAsync(cancellationToken);
        if (originalEntries.Count == 0)
            throw new InvalidOperationException("سند حسابداری اصلی یافت نشد.");
        var reversal = new Transaction
        {
            TransactionNo = await GenerateTransactionNumberAsync("PCR", cancellationToken),
            TransactionType = "PeriodicCorrespondentCommissionReversal",
            BranchId = await context.GetDefaultBranchIdAsync(cancellationToken), Status = "Paid",
            Remarks = $"برگشت کمیشن دوره‌ای نمایندگی {batch.Correspondent.Name}: {reason.Trim()}",
            CreatedBy = context.RequireCurrentUserId(), CreatedAt = DateTime.UtcNow,
            ReversedTransactionId = batch.PostingTransactionId
        };
        context.Transactions.Add(reversal);
        await context.SaveChangesAsync(cancellationToken);
        foreach (var entry in originalEntries)
            context.LedgerEntries.Add(NewEntry(reversal.Id, entry.AccountId, entry.CurrencyId,
                entry.BadehKar, entry.TalabKar, $"برگشت: {entry.Description}"));

        batch.Status = "Reversed";
        batch.ReversalTransactionId = reversal.Id;
        batch.ReversedBy = context.RequireCurrentUserId();
        batch.ReversedAt = DateTime.UtcNow;
        batch.ReversalReason = reason.Trim();
        foreach (var item in batch.Items) item.IsActive = false;
        var originalTransaction = await context.Transactions.FindAsync([batch.PostingTransactionId], cancellationToken);
        if (originalTransaction != null)
        {
            originalTransaction.Status = "Cancel";
            originalTransaction.CancelledBy = context.RequireCurrentUserId();
            originalTransaction.CancelledAt = DateTime.UtcNow;
            originalTransaction.CancelReason = reason.Trim();
        }
        context.AuditLogs.Add(new AuditLog
        {
            UserId = context.RequireCurrentUserId(), Action = "REVERSE_PERIODIC_COMMISSION",
            TableName = "CorrespondentCommissionBatches", RecordId = batch.Id,
            OldValue = $"{batch.TotalCommissionUsd} USD",
            NewValue = $"محاسبه کمیشن برگشت داده شد: {reason.Trim()}", CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    private async Task<CorrespondentCommissionPreviewDto> CalculateAsync(
        CorrespondentCommissionPreviewRequestDto request,
        bool requireAllRates,
        CancellationToken cancellationToken)
    {
        if (request.PeriodTo.Date < request.PeriodFrom.Date)
            throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ آغاز باشد.");
        if (request.CommissionPerLakhAfn <= 0)
            throw new InvalidOperationException("کمیشن هر لک باید بزرگ‌تر از صفر باشد.");
        if (request.UsdToAfnRate <= 0 && requireAllRates)
            throw new InvalidOperationException("نرخ تبدیل USD به AFN الزامی است.");

        var correspondent = await GetPeriodicCorrespondentAsync(request.CorrespondentId, cancellationToken);
        var endExclusive = request.PeriodTo.Date.AddDays(1);
        var hawalas = await context.Hawalas.AsNoTracking()
            .Where(x => x.CorrespondentId == request.CorrespondentId && x.HawalaType == "HawalaReceive" &&
                        x.Status != "Cancel" && x.CreatedAt >= request.PeriodFrom.Date && x.CreatedAt < endExclusive &&
                        (!x.CommissionAmount.HasValue || x.CommissionAmount == 0) &&
                        !context.CorrespondentCommissionBatchItems.Any(i => i.HawalaId == x.Id && i.IsActive))
            .Select(x => new { x.Id, x.Number, x.CreatedAt, x.FromCurrencyId, CurrencyCode = x.FromCurrency!.Code, x.FromAmount })
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Number).ToListAsync(cancellationToken);

        var suppliedRates = request.Rates.Where(x => x.CurrencyId > 0)
            .GroupBy(x => x.CurrencyId).ToDictionary(x => x.Key, x => x.Last().SourceToAfnRate);
        var afnId = await context.Currencies.Where(x => x.Code == "AFN" && x.IsActive)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var rates = hawalas.GroupBy(x => new { x.FromCurrencyId, x.CurrencyCode })
            .Select(x => new CorrespondentCommissionRateDto
            {
                CurrencyId = x.Key.FromCurrencyId, CurrencyCode = x.Key.CurrencyCode,
                TotalAmount = x.Sum(y => y.FromAmount),
                SourceToAfnRate = x.Key.FromCurrencyId == afnId
                    ? 1
                    : Math.Max(0, suppliedRates.GetValueOrDefault(x.Key.FromCurrencyId))
            }).ToList();
        if (requireAllRates && rates.Any(x => x.SourceToAfnRate <= 0))
            throw new InvalidOperationException($"نرخ تبدیل به افغانی برای {string.Join("، ", rates.Where(x => x.SourceToAfnRate <= 0).Select(x => x.CurrencyCode))} وارد نشده است.");

        var rateMap = rates.ToDictionary(x => x.CurrencyId, x => x.SourceToAfnRate);
        var items = hawalas.Select(x =>
        {
            var rate = rateMap[x.FromCurrencyId];
            var afn = x.FromAmount * rate;
            return new CorrespondentCommissionItemDto
            {
                HawalaId = x.Id, HawalaNumber = x.Number, HawalaDate = x.CreatedAt,
                CurrencyId = x.FromCurrencyId, CurrencyCode = x.CurrencyCode, SourceAmount = x.FromAmount, SourceToAfnRate = rate,
                AfnEquivalent = afn,
                CommissionAfn = afn / 100_000m * request.CommissionPerLakhAfn
            };
        }).ToList();
        var totalBase = items.Sum(x => x.AfnEquivalent);
        var totalCommissionAfn = RoundWhole(totalBase / 100_000m * request.CommissionPerLakhAfn);
        var totalCommissionUsd = request.UsdToAfnRate > 0
            ? RoundWhole(totalCommissionAfn / request.UsdToAfnRate) : 0;

        return new CorrespondentCommissionPreviewDto
        {
            CorrespondentId = correspondent.Id, CorrespondentName = correspondent.Name,
            PeriodFrom = request.PeriodFrom.Date, PeriodTo = request.PeriodTo.Date,
            HawalaCount = items.Count, Rates = rates, Items = items,
            TotalBaseAfn = totalBase, TotalCommissionAfn = totalCommissionAfn,
            TotalCommissionUsd = totalCommissionUsd
        };
    }

    private async Task<Correspondent> GetPeriodicCorrespondentAsync(long id, CancellationToken cancellationToken)
    {
        var correspondent = await context.Correspondents.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, cancellationToken)
            ?? throw new KeyNotFoundException("نمایندگی فعال یافت نشد.");
        if (correspondent.CommissionMethod != "PeriodicPerLakh")
            throw new InvalidOperationException("روش کمیشن این نمایندگی دوره‌ای بر اساس هر لک نیست.");
        return correspondent;
    }

    private async Task<Account> GetOrCreateCommissionIncomeAccountAsync(CancellationToken cancellationToken)
    {
        const string code = "3001";
        var account = await context.Accounts.SingleOrDefaultAsync(x => x.AccountCode == code, cancellationToken);
        if (account != null)
        {
            if (account.IsArchived || account.AccountType != "Income")
                throw new InvalidOperationException("حساب 3001 باید یک حساب درآمد فعال باشد.");
            return account;
        }
        account = new Account { AccountCode = code, AccountName = "کارمزد حواله", AccountType = "Income", CreatedAt = DateTime.UtcNow };
        context.Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        return account;
    }

    private static LedgerEntry NewEntry(long transactionId, long accountId, long currencyId,
        decimal talabKar, decimal badehKar, string description) => new()
        { TransactionId = transactionId, AccountId = accountId, CurrencyId = currencyId,
          TalabKar = talabKar, BadehKar = badehKar, Description = description, CreatedAt = DateTime.UtcNow };

    private async Task<string> GenerateTransactionNumberAsync(string prefix, CancellationToken cancellationToken)
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        var start = $"{prefix}-{date}-";
        var last = await context.Transactions.Where(x => x.TransactionNo.StartsWith(start))
            .OrderByDescending(x => x.TransactionNo).Select(x => x.TransactionNo).FirstOrDefaultAsync(cancellationToken);
        var next = last != null && int.TryParse(last[(last.LastIndexOf('-') + 1)..], out var number) ? number + 1 : 1;
        return $"{start}{next:D4}";
    }

    private static decimal RoundWhole(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    private static CorrespondentCommissionBatchDto Map(CorrespondentCommissionBatch x, string name) => new()
    {
        Id = x.Id, CorrespondentId = x.CorrespondentId, CorrespondentName = name,
        PeriodFrom = x.PeriodFrom, PeriodTo = x.PeriodTo, HawalaCount = x.Items.Count,
        CommissionPerLakhAfn = x.CommissionPerLakhAfn, UsdToAfnRate = x.UsdToAfnRate,
        TotalBaseAfn = x.TotalBaseAfn, TotalCommissionAfn = x.TotalCommissionAfn,
        TotalCommissionUsd = x.TotalCommissionUsd, Status = x.Status, CreatedAt = x.CreatedAt
    };
}
