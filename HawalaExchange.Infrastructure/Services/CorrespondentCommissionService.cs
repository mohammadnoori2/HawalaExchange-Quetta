using System.Data;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HawalaExchange.Infrastructure.Services;

public sealed class CorrespondentCommissionService(ApplicationDbContext context)
    : ICorrespondentCommissionService
{
    public async Task<CorrespondentCommissionPreviewDto> PreviewAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        using var budget = CreateCalculationBudget(request, cancellationToken);
        cancellationToken = budget.Token;
        await EnsureCommissionUsdValuationsAsync(request, cancellationToken);
        var preview = await ExecutePreviewAsync(request, cancellationToken);
        var deductions = await GetPendingCancellationDeductionsAsync(
            request.CorrespondentId, request.HawalaType, request.CommissionScope, cancellationToken);
        ApplyDeductionsToPreview(
            preview,
            deductions,
            request.CommissionPerLakhAfn,
            request.HawalaType == "HawalaSend" && request.CommissionScope != "Forwarding",
            request.CommissionScope is "Origin" or "Forwarding" or "Incoming");
        return preview;
    }

    public async Task<CorrespondentCommissionBatchDto> PostAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        using var budget = CreateCalculationBudget(request, cancellationToken);
        cancellationToken = budget.Token;
        if (request.CommissionScope == "Destination" && request.CurrencyRates.Count == 0)
            throw new InvalidOperationException("نرخ کمیشن هر ارز را وارد و پیش‌نمایش را باز‌محاسبه کنید.");
        if (request.CommissionScope is "Origin" or "Forwarding" && request.PaymentLocationRates.Count == 0)
            throw new InvalidOperationException("نرخ کمیشن هر محل پرداخت را وارد و پیش‌نمایش را باز‌محاسبه کنید.");
        var rates = CreateRatesTable(request.Rates);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (request.HawalaType is "HawalaReceive" or "HawalaSend")
        {
            try
            {
                await EnsureCommissionUsdValuationsAsync(request, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent commission run may have valued the same hawalas.
                // Continue with a clean tracker; the procedure's serializable
                // eligibility check decides which run may create the batch.
                context.ChangeTracker.Clear();
            }
        }
        var result = await WithProcedureAsync(
            "Post", request, rates,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("نتیجه ثبت کمیشن از دیتابیس دریافت نشد.");

                return new CorrespondentCommissionBatchDto
                {
                    CommissionScope = request.CommissionScope,
                    AccountingVersion = request.CommissionScope is "Incoming" or "Destination" or "Forwarding" ? 2 : 1,
                    HawalaType = request.HawalaType,
                    Id = reader.GetInt64(0),
                    CorrespondentId = reader.GetInt64(1),
                    CorrespondentName = reader.GetString(2),
                    PeriodFrom = reader.GetDateTime(3),
                    PeriodTo = reader.GetDateTime(4),
                    HawalaCount = reader.GetInt32(5),
                    CommissionPerLakhAfn = reader.GetDecimal(6),
                    UsdToAfnRate = reader.GetDecimal(7),
                    TotalBaseAfn = reader.GetDecimal(8),
                    TotalCommissionAfn = reader.GetDecimal(9),
                    TotalCommissionUsd = reader.GetDecimal(10),
                    Status = reader.GetString(11),
                    CreatedAt = reader.GetDateTime(12)
                };
            }, cancellationToken);

        var deductions = await GetPendingCancellationDeductionsAsync(
            request.CorrespondentId, request.HawalaType, request.CommissionScope, cancellationToken);
        if (deductions.Count > 0)
            await AddDeductionsToPostedBatchAsync(
                result, deductions, request.HawalaType == "HawalaSend" && request.CommissionScope != "Forwarding",
                request.CommissionScope is "Origin" or "Forwarding" or "Incoming", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<List<CorrespondentCommissionBatchItem>> GetPendingCancellationDeductionsAsync(
        long correspondentId,
        string hawalaType,
        string commissionScope,
        CancellationToken cancellationToken)
    {
        var modernAccounting = commissionScope is "Incoming" or "Destination" or "Forwarding";
        var candidates = await context.CorrespondentCommissionBatchItems
            .AsNoTracking()
            .Include(x => x.Hawala)
            .Include(x => x.SourceCurrency)
            .Include(x => x.Batch)
            .Where(x => !x.IsActive && x.Batch.Status == "Posted" &&
                        x.Batch.CorrespondentId == correspondentId &&
                        x.Batch.CommissionScope == commissionScope &&
                        (x.Batch.AccountingVersion >= 2) == modernAccounting &&
                        x.Hawala.HawalaType == hawalaType &&
                        x.Hawala.Status == "Cancel" &&
                        !context.CorrespondentCommissionBatchItems.Any(active =>
                            active.HawalaId == x.HawalaId && active.CommissionScope == x.CommissionScope && active.IsActive))
            .OrderByDescending(x => x.Batch.CreatedAt)
            .ToListAsync(cancellationToken);
        return candidates.GroupBy(x => x.HawalaId).Select(x => x.First()).ToList();
    }

    private static void ApplyDeductionsToPreview(
        CorrespondentCommissionPreviewDto preview,
        IReadOnlyCollection<CorrespondentCommissionBatchItem> deductions,
        decimal commissionPerLakhAfn,
        bool isOutgoing,
        bool isOrigin)
    {
        if (deductions.Count == 0)
            return;

        var originalBase = preview.TotalBaseAfn;
        foreach (var deduction in deductions)
        {
            preview.Items.Add(new CorrespondentCommissionItemDto
            {
                HawalaId = deduction.HawalaId,
                HawalaNumber = deduction.Hawala.Number,
                HawalaDate = deduction.Hawala.CreatedAt,
                ValuationDate = deduction.ValuationDate,
                CurrencyId = deduction.SourceCurrencyId,
                CurrencyCode = deduction.SourceCurrency.Code,
                SourceAmount = -Math.Abs(deduction.SourceAmount),
                SourceToAfnRate = deduction.SourceToAfnRate,
                AfnEquivalent = -Math.Abs(deduction.AfnEquivalent),
                CommissionAfn = -Math.Abs(deduction.CommissionAfn),
                PaymentLocationId = deduction.PaymentLocationId,
                PaymentLocationName = deduction.PaymentLocationName ?? "محل پرداخت نامشخص",
                PerLakhRate = deduction.PerLakhRate ?? commissionPerLakhAfn
            });
        }

        preview.TotalBaseAfn = decimal.Round(
            originalBase - deductions.Sum(x => Math.Abs(x.AfnEquivalent)), isOutgoing ? 2 : 4,
            MidpointRounding.AwayFromZero);
        if (isOutgoing)
        {
            preview.TotalCommissionAfn = decimal.Round(
                preview.Items.Where(x => x.CurrencyCode == "AFN").Sum(x => x.CommissionAfn),
                2, MidpointRounding.AwayFromZero);
            preview.TotalCommissionUsd = decimal.Round(
                preview.Items.Where(x => x.CurrencyCode == "USD").Sum(x => x.CommissionAfn),
                2, MidpointRounding.AwayFromZero);
        }
        else if (isOrigin)
        {
            preview.TotalCommissionAfn = 0;
            preview.TotalCommissionUsd = decimal.Round(
                preview.Items.Sum(x => x.CommissionAfn), 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            preview.TotalCommissionAfn = 0;
            preview.TotalCommissionUsd = decimal.Round(
                preview.TotalBaseAfn / 100000m * commissionPerLakhAfn,
                0, MidpointRounding.AwayFromZero);
        }
        if (isOutgoing || isOrigin)
        {
            var locationRates = preview.PaymentLocationRates.ToDictionary(x => x.PaymentLocationId, x => x.PerLakhRate);
            preview.PaymentLocationRates = preview.Items.Where(x => x.PaymentLocationId.HasValue)
                .GroupBy(x => new { x.PaymentLocationId, x.PaymentLocationName })
                .Select(group => new PaymentLocationCommissionRateDto
                {
                    PaymentLocationId = group.Key.PaymentLocationId!.Value,
                    PaymentLocationName = group.Key.PaymentLocationName,
                    PerLakhRate = locationRates.GetValueOrDefault(group.Key.PaymentLocationId.Value, group.First().PerLakhRate),
                    HawalaCount = group.Count(), TotalAmount = group.Sum(x => x.SourceAmount),
                    TotalDebitUsd = group.Sum(x => x.AfnEquivalent),
                    TotalCommissionAfn = isOutgoing ? group.Where(x => x.CurrencyCode == "AFN").Sum(x => x.CommissionAfn) : 0,
                    TotalCommissionUsd = group.Where(x => !isOutgoing || x.CurrencyCode == "USD").Sum(x => x.CommissionAfn)
                }).ToList();
            var currencyRates = preview.CurrencyRates.ToDictionary(x => x.CurrencyId, x => x.PerLakhRate);
            preview.CurrencyRates = preview.Items.GroupBy(x => new { x.CurrencyId, x.CurrencyCode })
                .Select(group => new CurrencyCommissionRateDto
                {
                    CurrencyId = group.Key.CurrencyId, CurrencyCode = group.Key.CurrencyCode,
                    PerLakhRate = currencyRates.GetValueOrDefault(group.Key.CurrencyId, group.First().PerLakhRate),
                    HawalaCount = group.Count(), TotalAmount = group.Sum(x => x.SourceAmount),
                    CommissionAmount = group.Sum(x => x.CommissionAfn)
                }).ToList();
        }
    }

    private async Task AddDeductionsToPostedBatchAsync(
        CorrespondentCommissionBatchDto result,
        IReadOnlyCollection<CorrespondentCommissionBatchItem> deductions,
        bool isOutgoing,
        bool isOrigin,
        CancellationToken cancellationToken)
    {
        var batch = await context.CorrespondentCommissionBatches
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == result.Id, cancellationToken);
        var originalBase = batch.TotalBaseAfn;
        foreach (var deduction in deductions)
        {
            batch.Items.Add(new CorrespondentCommissionBatchItem
            {
                HawalaId = deduction.HawalaId,
                CommissionScope = deduction.CommissionScope,
                ValuationDate = deduction.ValuationDate,
                SourceCurrencyId = deduction.SourceCurrencyId,
                SourceAmount = -Math.Abs(deduction.SourceAmount),
                SourceToAfnRate = deduction.SourceToAfnRate,
                AfnEquivalent = -Math.Abs(deduction.AfnEquivalent),
                CommissionAfn = -Math.Abs(deduction.CommissionAfn),
                PaymentLocationId = deduction.PaymentLocationId,
                PaymentLocationName = deduction.PaymentLocationName,
                PerLakhRate = deduction.PerLakhRate,
                IsActive = true
            });
        }

        batch.TotalBaseAfn = decimal.Round(
            originalBase - deductions.Sum(x => Math.Abs(x.AfnEquivalent)), isOutgoing ? 2 : 4,
            MidpointRounding.AwayFromZero);
        var currencyCodes = await context.Currencies.AsNoTracking()
            .Where(x => x.Code == "AFN" || x.Code == "USD")
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        if (isOutgoing)
        {
            batch.TotalCommissionAfn = decimal.Round(batch.Items
                .Where(x => currencyCodes.GetValueOrDefault(x.SourceCurrencyId) == "AFN")
                .Sum(x => x.CommissionAfn), 2, MidpointRounding.AwayFromZero);
            batch.TotalCommissionUsd = decimal.Round(batch.Items
                .Where(x => currencyCodes.GetValueOrDefault(x.SourceCurrencyId) == "USD")
                .Sum(x => x.CommissionAfn), 2, MidpointRounding.AwayFromZero);
        }
        else if (isOrigin)
        {
            batch.TotalCommissionAfn = 0;
            batch.TotalCommissionUsd = decimal.Round(batch.Items.Sum(x => x.CommissionAfn), 2,
                MidpointRounding.AwayFromZero);
        }
        else
        {
            batch.TotalCommissionAfn = 0;
            batch.TotalCommissionUsd = decimal.Round(
                batch.TotalBaseAfn / 100000m * batch.CommissionPerLakhAfn,
                0, MidpointRounding.AwayFromZero);
        }
        var postingAmount = isOutgoing
            ? batch.AccountingVersion >= 2 ? Math.Max(batch.TotalCommissionAfn, batch.TotalCommissionUsd) : batch.TotalBaseAfn
            : batch.TotalCommissionUsd;
        if (postingAmount <= 0)
            throw new InvalidOperationException(
                "پس از کسر حواله‌های لغوشده، کمیشن قابل پرداختی باقی نمی‌ماند.");

        if (isOutgoing)
        {
            await RebuildOutgoingLedgerAsync(batch, cancellationToken);
        }
        else
        {
            var ledgerEntries = await context.LedgerEntries
                .Where(x => x.TransactionId == batch.PostingTransactionId)
                .ToListAsync(cancellationToken);
            foreach (var entry in ledgerEntries)
            {
                if (entry.TalabKar > 0) entry.TalabKar = postingAmount;
                if (entry.BadehKar > 0) entry.BadehKar = postingAmount;
            }
        }
        await context.SaveChangesAsync(cancellationToken);

        result.HawalaCount = batch.Items.Count;
        result.TotalBaseAfn = batch.TotalBaseAfn;
        result.TotalCommissionAfn = batch.TotalCommissionAfn;
        result.TotalCommissionUsd = batch.TotalCommissionUsd;
    }

    internal async Task RebuildOutgoingLedgerAsync(
        CorrespondentCommissionBatch batch,
        CancellationToken cancellationToken)
    {
        var currencies = await context.Currencies.AsNoTracking()
            .Where(x => x.Code == "AFN" || x.Code == "USD")
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);
        if (!currencies.TryGetValue("AFN", out var afnId) ||
            !currencies.TryGetValue("USD", out var usdId))
            throw new InvalidOperationException("ارزهای فعال USD و AFN در سیستم یافت نشد.");

        var destinationAccountId = await context.Accounts.AsNoTracking()
            .Where(x => x.CorrespondentId == batch.CorrespondentId && !x.IsArchived)
            .OrderBy(x => x.Id).Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("حساب فعال نمایندگی مقصد یافت نشد.");
        if (batch.AccountingVersion >= 2 && batch.CommissionScope == "Destination")
        {
            var expense = await context.Accounts.SingleOrDefaultAsync(x => x.AccountCode == "5002", cancellationToken)
                ?? throw new InvalidOperationException("حساب هزینه کمیشن پرداختی به نمایندگی‌ها یافت نشد.");
            if (expense.IsArchived || expense.AccountType != "Expense")
                throw new InvalidOperationException("حساب 5002 باید یک حساب هزینه فعال باشد.");
            var previous = await context.LedgerEntries
                .Where(x => x.TransactionId == batch.PostingTransactionId).ToListAsync(cancellationToken);
            context.LedgerEntries.RemoveRange(previous);
            foreach (var (currencyId, amount) in new[] { (afnId, batch.TotalCommissionAfn), (usdId, batch.TotalCommissionUsd) })
            {
                if (amount == 0) continue;
                var destinationDescription = "کمیشن پرداختی به نمایندگی‌ها";
                context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId, destinationAccountId,
                    currencyId, Math.Max(amount, 0), Math.Max(-amount, 0), destinationDescription));
                context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId, expense.Id,
                    currencyId, Math.Max(-amount, 0), Math.Max(amount, 0), destinationDescription));
            }
            return;
        }
        var clearing = await context.Accounts
            .SingleOrDefaultAsync(x => x.AccountCode == "SYS-SETTLEMENT-CLEARING", cancellationToken);
        if (clearing == null)
        {
            clearing = new Account
            {
                AccountCode = "SYS-SETTLEMENT-CLEARING",
                AccountName = "حساب واسط تبدیل ارز نمایندگی‌ها",
                AccountType = "CurrencyConversionClearing",
                CreatedAt = DateTime.UtcNow
            };
            context.Accounts.Add(clearing);
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (clearing.IsArchived || clearing.AccountType != "CurrencyConversionClearing")
            throw new InvalidOperationException("حساب واسط تبدیل ارز فعال و معتبر نیست.");

        var activeBatchItems = batch.Items.Where(x => x.IsActive).ToList();
        var hawalaIds = activeBatchItems.Select(x => x.HawalaId).ToArray();
        var sourceLinks = await context.Hawalas.AsNoTracking()
            .Where(x => hawalaIds.Contains(x.Id))
            .Select(x => new { x.Id, SourceCorrespondentId = x.SourceHawala!.CorrespondentId })
            .ToDictionaryAsync(x => x.Id, x => x.SourceCorrespondentId, cancellationToken);
        var activeItems = activeBatchItems.Select(x => new
        {
            x.SourceCurrencyId,
            x.AfnEquivalent,
            SourceCorrespondentId = sourceLinks.GetValueOrDefault(x.HawalaId)
        }).ToList();
        var sourceIds = activeItems.Where(x => x.SourceCorrespondentId.HasValue)
            .Select(x => x.SourceCorrespondentId!.Value).Distinct().ToArray();
        var sourceAccounts = await context.Accounts.AsNoTracking()
            .Where(x => x.CorrespondentId.HasValue && sourceIds.Contains(x.CorrespondentId.Value) && !x.IsArchived)
            .GroupBy(x => x.CorrespondentId!.Value)
            .Select(x => new { CorrespondentId = x.Key, AccountId = x.Min(a => a.Id) })
            .ToDictionaryAsync(x => x.CorrespondentId, x => x.AccountId, cancellationToken);
        if (sourceIds.Any(id => !sourceAccounts.ContainsKey(id)))
            throw new InvalidOperationException("حساب فعال نمایندگی فرستنده یک یا چند حواله یافت نشد.");
        Account? expenseAccount = null;
        if (activeItems.Any(x => !x.SourceCorrespondentId.HasValue))
        {
            expenseAccount = await context.Accounts
                .SingleOrDefaultAsync(x => x.AccountCode == "5002", cancellationToken);
            if (expenseAccount == null)
            {
                expenseAccount = new Account
                {
                    AccountCode = "5002", AccountName = "هزینه کمیشن حواله‌های ارسالی",
                    AccountType = "Expense", CreatedAt = DateTime.UtcNow
                };
                context.Accounts.Add(expenseAccount);
                await context.SaveChangesAsync(cancellationToken);
            }
            else if (expenseAccount.IsArchived || expenseAccount.AccountType != "Expense")
                throw new InvalidOperationException("حساب 5002 باید یک حساب هزینه فعال باشد.");
        }

        var oldEntries = await context.LedgerEntries
            .Where(x => x.TransactionId == batch.PostingTransactionId)
            .ToListAsync(cancellationToken);
        context.LedgerEntries.RemoveRange(oldEntries);
        var description = $"کمیشن حواله‌های ارسالی نمایندگی، {activeItems.Count} حواله";
        if (batch.TotalCommissionUsd > 0)
            context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId, destinationAccountId,
                usdId, batch.TotalCommissionUsd, 0, description));
        if (batch.TotalCommissionAfn > 0)
        {
            context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId, destinationAccountId,
                afnId, batch.TotalCommissionAfn, 0, description));
            context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId, clearing.Id,
                afnId, 0, batch.TotalCommissionAfn, description));
            var afnUsd = decimal.Round(activeItems
                .Where(x => x.SourceCurrencyId == afnId).Sum(x => x.AfnEquivalent),
                2, MidpointRounding.AwayFromZero);
            if (afnUsd > 0)
                context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId, clearing.Id,
                    usdId, afnUsd, 0, description));
        }
        foreach (var group in activeItems.Where(x => x.SourceCorrespondentId.HasValue)
                     .GroupBy(x => x.SourceCorrespondentId!.Value))
        {
            var amount = decimal.Round(group.Sum(x => x.AfnEquivalent), 2,
                MidpointRounding.AwayFromZero);
            if (amount > 0)
                context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId,
                    sourceAccounts[group.Key], usdId, 0, amount, description));
        }
        var ownOfficeAmount = decimal.Round(activeItems
            .Where(x => !x.SourceCorrespondentId.HasValue).Sum(x => x.AfnEquivalent),
            2, MidpointRounding.AwayFromZero);
        if (ownOfficeAmount > 0 && expenseAccount != null)
            context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId,
                expenseAccount.Id, usdId, 0, ownOfficeAmount,
                "هزینه کمیشن حواله‌های ارسالی از صرافی خود ما"));
    }

    public async Task<IReadOnlyList<CorrespondentCommissionBatchDto>> GetHistoryAsync(
        long correspondentId,
        CancellationToken cancellationToken = default)
    {
        var history = await context.CorrespondentCommissionBatches.AsNoTracking()
            .Where(x => x.CorrespondentId == correspondentId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CorrespondentCommissionBatchDto
            {
                Id = x.Id, CorrespondentId = x.CorrespondentId, CorrespondentName = x.Correspondent.Name,
                CommissionScope = x.CommissionScope,
                AccountingVersion = x.AccountingVersion,
                HawalaType = x.Items.Select(i => i.Hawala.HawalaType).FirstOrDefault() ?? "HawalaReceive",
                PeriodFrom = x.PeriodFrom, PeriodTo = x.PeriodTo, HawalaCount = x.Items.Count,
                CommissionPerLakhAfn = x.CommissionPerLakhAfn, UsdToAfnRate = x.UsdToAfnRate,
                TotalBaseAfn = x.TotalBaseAfn, TotalCommissionAfn = x.TotalCommissionAfn,
                TotalCommissionUsd = x.TotalCommissionUsd, Status = x.Status, CreatedAt = x.CreatedAt
            }).ToListAsync(cancellationToken);

        var originIds = history.Where(x => x.CommissionScope is "Origin" or "Forwarding").Select(x => x.Id).ToArray();
        if (originIds.Length > 0)
        {
            var locations = await context.CorrespondentCommissionBatchItems.AsNoTracking()
                .Where(x => originIds.Contains(x.BatchId))
                .GroupBy(x => new { x.BatchId, x.PaymentLocationId, x.PaymentLocationName, x.PerLakhRate })
                .Select(group => new
                {
                    group.Key.BatchId,
                    PaymentLocationId = group.Key.PaymentLocationId,
                    PaymentLocationName = group.Key.PaymentLocationName,
                    PerLakhRate = group.Key.PerLakhRate,
                    HawalaCount = group.Count(),
                    TotalDebitUsd = group.Sum(x => x.AfnEquivalent),
                    TotalCommissionUsd = group.Sum(x => x.CommissionAfn)
                }).ToListAsync(cancellationToken);
            var byBatch = locations.GroupBy(x => x.BatchId)
                .ToDictionary(x => x.Key, x => x.Select(row => new PaymentLocationCommissionRateDto
                {
                    PaymentLocationId = row.PaymentLocationId ?? 0,
                    PaymentLocationName = row.PaymentLocationName ?? "محل پرداخت نامشخص",
                    PerLakhRate = row.PerLakhRate ?? 0,
                    HawalaCount = row.HawalaCount,
                    TotalDebitUsd = row.TotalDebitUsd,
                    TotalCommissionUsd = row.TotalCommissionUsd
                }).ToList());
            foreach (var batch in history)
                batch.LocationSummaries = byBatch.GetValueOrDefault(batch.Id) ?? [];
        }
        return history;
    }

    public async Task<CorrespondentCommissionBatchDto> GetDetailsAsync(
        long batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await context.CorrespondentCommissionBatches.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Correspondent)
            .Include(x => x.CreatedByUser)
            .Include(x => x.PostingTransaction)
            .Include(x => x.ReversalTransaction)
            .Include(x => x.Items).ThenInclude(x => x.Hawala).ThenInclude(x => x.SourceHawala).ThenInclude(x => x!.Correspondent)
            .Include(x => x.Items).ThenInclude(x => x.SourceCurrency)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new KeyNotFoundException("محاسبه کمیشن یافت نشد.");

        var transactionIds = new[] { batch.PostingTransactionId, batch.ReversalTransactionId ?? 0 }
            .Where(x => x > 0).ToArray();
        var ledgerEntries = await context.LedgerEntries.AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Include(x => x.Transaction)
            .Where(x => x.TransactionId.HasValue && transactionIds.Contains(x.TransactionId.Value))
            .OrderBy(x => x.TransactionId)
            .ThenBy(x => x.Id)
            .Select(x => new LedgerEntryDto
            {
                Id = x.Id,
                TransactionId = x.TransactionId ?? 0,
                TransactionNo = x.Transaction != null ? x.Transaction.TransactionNo : string.Empty,
                TransactionType = x.Transaction != null ? x.Transaction.TransactionType : string.Empty,
                AccountId = x.AccountId,
                AccountCode = x.Account != null ? x.Account.AccountCode : string.Empty,
                AccountName = x.Account != null ? x.Account.AccountName : string.Empty,
                CurrencyId = x.CurrencyId,
                CurrencyCode = x.Currency != null ? x.Currency.Code : string.Empty,
                TalabKar = x.TalabKar,
                BadehKar = x.BadehKar,
                Description = x.Description,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new CorrespondentCommissionBatchDto
        {
            Id = batch.Id,
            CommissionScope = batch.CommissionScope,
            AccountingVersion = batch.AccountingVersion,
            CorrespondentId = batch.CorrespondentId,
            CorrespondentName = batch.Correspondent.Name,
            HawalaType = batch.Items.Select(x => x.Hawala.HawalaType).FirstOrDefault() ?? "HawalaReceive",
            PeriodFrom = batch.PeriodFrom,
            PeriodTo = batch.PeriodTo,
            HawalaCount = batch.Items.Count,
            CommissionPerLakhAfn = batch.CommissionPerLakhAfn,
            UsdToAfnRate = batch.UsdToAfnRate,
            TotalBaseAfn = batch.TotalBaseAfn,
            TotalCommissionAfn = batch.TotalCommissionAfn,
            TotalCommissionUsd = batch.TotalCommissionUsd,
            Status = batch.Status,
            CreatedAt = batch.CreatedAt,
            CreatedByName = batch.CreatedByUser.FullName,
            PostingTransactionNo = batch.PostingTransaction.TransactionNo,
            ReversalTransactionNo = batch.ReversalTransaction?.TransactionNo,
            ReversedAt = batch.ReversedAt,
            ReversalReason = batch.ReversalReason,
            Items = batch.Items.OrderBy(x => x.Hawala.CreatedAt).ThenBy(x => x.Hawala.Number)
                .Select(x => new CorrespondentCommissionItemDto
                {
                    HawalaId = x.HawalaId,
                    HawalaNumber = x.Hawala.Number,
                    HawalaDate = x.Hawala.CreatedAt,
                    ValuationDate = x.ValuationDate,
                    CurrencyId = x.SourceCurrencyId,
                    CurrencyCode = x.SourceCurrency.Code,
                    SourceAmount = x.SourceAmount,
                    SourceToAfnRate = x.SourceToAfnRate,
                    AfnEquivalent = x.AfnEquivalent,
                    CommissionAfn = x.CommissionAfn,
                    PaymentLocationId = x.PaymentLocationId,
                    PaymentLocationName = x.PaymentLocationName ?? "محل پرداخت نامشخص",
                    PerLakhRate = x.PerLakhRate ?? batch.CommissionPerLakhAfn,
                    IsActive = x.IsActive,
                    SourceType = x.Hawala.SourceHawalaId.HasValue ? "Correspondent" : "OwnOffice",
                    SourceName = batch.CommissionScope is "Origin" or "Forwarding"
                        ? batch.Correspondent.Name
                        : x.Hawala.SourceHawala?.Correspondent?.Name ?? "صرافی خود ما"
                }).ToList(),
            LedgerEntries = ledgerEntries
        };
    }

    public async Task ReverseAsync(long batchId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("دلیل برگشت الزامی است.");
        if (reason.Trim().Length > 500)
            throw new InvalidOperationException("دلیل برگشت نمی‌تواند بیشتر از 500 حرف باشد.");
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
            OldValue = $"{batch.TotalCommissionAfn} AFN / {batch.TotalCommissionUsd} USD",
            NewValue = $"محاسبه کمیشن برگشت داده شد: {reason.Trim()}", CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    private async Task<CorrespondentCommissionPreviewDto> ExecutePreviewAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var rates = CreateRatesTable(request.Rates);
        return await WithProcedureAsync(
            "Preview", request, rates,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("نتیجه محاسبه کمیشن از دیتابیس دریافت نشد.");

                var result = new CorrespondentCommissionPreviewDto
                {
                    CorrespondentId = reader.GetInt64(0),
                    CorrespondentName = reader.GetString(1),
                    PeriodFrom = reader.GetDateTime(2),
                    PeriodTo = reader.GetDateTime(3),
                    HawalaCount = reader.GetInt32(4),
                    TotalBaseAfn = reader.GetDecimal(5),
                    TotalCommissionAfn = reader.GetDecimal(6),
                    TotalCommissionUsd = reader.GetDecimal(7)
                };

                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    result.Rates.Add(new CorrespondentCommissionRateDto
                    {
                        CurrencyId = reader.GetInt64(0),
                        CurrencyCode = reader.GetString(1),
                        TotalAmount = reader.GetDecimal(2),
                        SourceToAfnRate = reader.GetDecimal(3)
                    });

                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    result.Items.Add(new CorrespondentCommissionItemDto
                    {
                        HawalaId = reader.GetInt64(0),
                        HawalaNumber = reader.GetInt64(1),
                        HawalaDate = reader.GetDateTime(2),
                        CurrencyId = reader.GetInt64(3),
                        CurrencyCode = reader.GetString(4),
                        SourceAmount = reader.GetDecimal(5),
                        SourceToAfnRate = reader.GetDecimal(6),
                        AfnEquivalent = reader.GetDecimal(7),
                        CommissionAfn = reader.GetDecimal(8),
                        PaymentLocationId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                        PaymentLocationName = reader.IsDBNull(10) ? "محل پرداخت نامشخص" : reader.GetString(10),
                        PerLakhRate = reader.GetDecimal(11),
                        ValuationDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12)
                    });
                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    result.PaymentLocationRates.Add(new PaymentLocationCommissionRateDto
                    {
                        PaymentLocationId = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
                        PaymentLocationName = reader.IsDBNull(1) ? "محل پرداخت نامشخص" : reader.GetString(1),
                        PerLakhRate = reader.GetDecimal(2),
                        HawalaCount = reader.GetInt32(3),
                        TotalAmount = reader.GetDecimal(4),
                        TotalCommissionAfn = reader.GetDecimal(5),
                        TotalCommissionUsd = reader.GetDecimal(6),
                        TotalDebitUsd = reader.GetDecimal(7)
                    });
                result.CurrencyRates = result.Items
                    .GroupBy(x => new { x.CurrencyId, x.CurrencyCode })
                    .Select(group => new CurrencyCommissionRateDto
                    {
                        CurrencyId = group.Key.CurrencyId,
                        CurrencyCode = group.Key.CurrencyCode,
                        TotalAmount = group.Sum(x => x.SourceAmount),
                        HawalaCount = group.Count(),
                        PerLakhRate = group.First().PerLakhRate,
                        CommissionAmount = group.Sum(x => x.CommissionAfn)
                    }).OrderBy(x => x.CurrencyCode).ToList();
                return result;
            }, cancellationToken);
    }

    private async Task<T> WithProcedureAsync<T>(
        string mode,
        CorrespondentCommissionPreviewRequestDto request,
        DataTable rates,
        Func<SqlDataReader, Task<T>> readResult,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_ProcessPeriodicCommission_v1";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = request.CommissionScope is "Incoming" or "Destination" or "Forwarding" ? 25 : 180;
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
            ConfigureCommand(command, mode, request, rates);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await readResult(reader);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private void ConfigureCommand(
        SqlCommand command,
        string mode,
        CorrespondentCommissionPreviewRequestDto request,
        DataTable rates)
    {
        command.Parameters.Add(new SqlParameter("@Mode", SqlDbType.NVarChar, 10) { Value = mode });
        command.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.BigInt) { Value = context.CurrentTenantId });
        command.Parameters.Add(new SqlParameter("@CurrentUserId", SqlDbType.BigInt) { Value = context.RequireCurrentUserId() });
        command.Parameters.Add(new SqlParameter("@CorrespondentId", SqlDbType.BigInt) { Value = request.CorrespondentId });
        command.Parameters.Add(new SqlParameter("@HawalaType", SqlDbType.NVarChar, 20) { Value = request.HawalaType });
        command.Parameters.Add(new SqlParameter("@CommissionScope", SqlDbType.NVarChar, 20) { Value = request.CommissionScope });
        command.Parameters.Add(new SqlParameter("@UtcOffsetMinutes", SqlDbType.Int)
            { Value = (int)TimeZoneInfo.Local.GetUtcOffset(request.PeriodFrom).TotalMinutes });
        command.Parameters.Add(new SqlParameter("@PeriodFrom", SqlDbType.Date) { Value = request.PeriodFrom.Date });
        command.Parameters.Add(new SqlParameter("@PeriodTo", SqlDbType.Date) { Value = request.PeriodTo.Date });
        command.Parameters.Add(new SqlParameter("@FromUtc", SqlDbType.DateTime2) { Value = request.PeriodFrom.Date.ToUniversalTime() });
        command.Parameters.Add(new SqlParameter("@ToUtcExclusive", SqlDbType.DateTime2) { Value = request.PeriodTo.Date.AddDays(1).ToUniversalTime() });
        command.Parameters.Add(new SqlParameter("@CommissionPerLakhAfn", SqlDbType.Decimal)
            { Precision = 18, Scale = 4, Value = request.CommissionPerLakhAfn });
        command.Parameters.Add(new SqlParameter("@UsdToAfnRate", SqlDbType.Decimal)
            { Precision = 18, Scale = 8, Value = request.UsdToAfnRate });
        command.Parameters.Add(new SqlParameter("@Rates", SqlDbType.Structured)
            { TypeName = "dbo.CommissionRateTableType_v1", Value = rates });
        command.Parameters.Add(new SqlParameter("@LocationRatesJson", SqlDbType.NVarChar, -1)
        {
            Value = JsonSerializer.Serialize(request.PaymentLocationRates
                .Where(x => x.PaymentLocationId > 0)
                .Select(x => new { x.PaymentLocationId, x.PerLakhRate }))
        });
        command.Parameters.Add(new SqlParameter("@CurrencyRatesJson", SqlDbType.NVarChar, -1)
        {
            Value = JsonSerializer.Serialize(request.CurrencyRates
                .Where(x => x.CurrencyId > 0)
                .Select(x => new { x.CurrencyId, x.PerLakhRate }))
        });
    }

    private static DataTable CreateRatesTable(IEnumerable<CorrespondentCommissionRateDto> suppliedRates)
    {
        var table = new DataTable();
        table.Columns.Add("CurrencyId", typeof(long));
        table.Columns.Add("SourceToAfnRate", typeof(decimal));
        foreach (var rate in suppliedRates.Where(x => x.CurrencyId > 0)
                     .GroupBy(x => x.CurrencyId).Select(x => x.Last()))
            table.Rows.Add(rate.CurrencyId, rate.SourceToAfnRate);
        return table;
    }

    private static CancellationTokenSource CreateCalculationBudget(
        CorrespondentCommissionPreviewRequestDto request, CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.CommissionScope is "Incoming" or "Destination" or "Forwarding")
            budget.CancelAfter(TimeSpan.FromSeconds(28));
        return budget;
    }

    private static void ValidateRequest(CorrespondentCommissionPreviewRequestDto request)
    {
        if (request.CorrespondentId <= 0)
            throw new InvalidOperationException("نمایندگی معتبر انتخاب نشده است.");
        if (request.PeriodTo.Date < request.PeriodFrom.Date)
            throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ آغاز باشد.");
        if (request.CommissionPerLakhAfn <= 0)
            throw new InvalidOperationException("کمیشن هر لک باید بزرگ‌تر از صفر باشد.");
        if (request.HawalaType is not ("HawalaReceive" or "HawalaSend"))
            throw new InvalidOperationException("نوع حواله برای محاسبه کمیشن معتبر نیست.");
        if (request.CommissionScope is not ("Standard" or "Destination" or "Origin" or "Incoming" or "Forwarding") ||
            (request.CommissionScope == "Origin" && request.HawalaType != "HawalaReceive") ||
            (request.CommissionScope is "Destination" or "Forwarding" && request.HawalaType != "HawalaSend") ||
            (request.CommissionScope == "Incoming" && request.HawalaType != "HawalaReceive"))
            throw new InvalidOperationException("نوع محاسبه کمیشن معتبر نیست.");
        if (request.Rates.Any(x => x.SourceToAfnRate < 0))
            throw new InvalidOperationException("نرخ تبدیل ارز نمی‌تواند منفی باشد.");
        if (request.PaymentLocationRates.Any(x => x.PaymentLocationId <= 0 || x.PerLakhRate <= 0) ||
            request.PaymentLocationRates.Select(x => x.PaymentLocationId).Distinct().Count() !=
            request.PaymentLocationRates.Count)
            throw new InvalidOperationException("نرخ کمیشن محل پرداخت معتبر نیست یا محل تکراری انتخاب شده است.");
        if (request.CurrencyRates.Any(x => x.CurrencyId <= 0 || x.PerLakhRate <= 0) ||
            request.CurrencyRates.Select(x => x.CurrencyId).Distinct().Count() != request.CurrencyRates.Count)
            throw new InvalidOperationException("نرخ کمیشن ارز معتبر نیست یا ارز تکراری انتخاب شده است.");
    }

    private async Task EnsureCommissionUsdValuationsAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        // New calculations read daily correspondent rates in one set-based SQL query.
        // Preview does not mutate shared Hawala valuations or previously posted snapshots.
        if (request.CommissionScope is "Incoming" or "Destination" or "Forwarding")
            return;
        if (request.CommissionScope == "Origin")
        {
            await EnsureOriginUsdValuationsAsync(request, cancellationToken);
            return;
        }

        var localStart = request.PeriodFrom.Date;
        var localEnd = request.PeriodTo.Date.AddDays(1);
        var utcStart = localStart.ToUniversalTime();
        var utcEnd = localEnd.ToUniversalTime();
        var currencies = await context.Currencies.AsNoTracking()
            .Where(x => x.Code == "AFN" || x.Code == "USD")
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);
        if (!currencies.TryGetValue("AFN", out var afnId) ||
            !currencies.TryGetValue("USD", out var usdId))
            throw new InvalidOperationException("ارزهای فعال USD و AFN در سیستم یافت نشد.");

        var hawalas = await context.Hawalas
            .Include(x => x.SourceHawala)
            .Where(x => x.CorrespondentId == request.CorrespondentId &&
                        x.HawalaType == request.HawalaType &&
                        x.Status != "Cancel" &&
                        x.CreatedAt >= utcStart && x.CreatedAt < utcEnd &&
                        (request.HawalaType == "HawalaReceive"
                            ? x.CommissionAmount == null || x.CommissionAmount == 0
                            : x.AgentCommissionAmount == null &&
                              x.SourceHawala != null && x.SourceHawala.CorrespondentId != null) &&
                        (request.CommissionScope != "Origin" ||
                         context.Hawalas.Any(generated => generated.SourceHawalaId == x.Id &&
                                                      generated.HawalaType == "HawalaSend" &&
                                                      generated.Status != "Cancel")) &&
                        (request.HawalaType == "HawalaReceive"
                            ? x.FromCurrencyId == afnId || x.FromCurrencyId == usdId
                            : x.ToCurrencyId == afnId || x.ToCurrencyId == usdId) &&
                        !context.CorrespondentCommissionBatchItems.Any(item =>
                            item.HawalaId == x.Id && item.IsActive))
            .ToListAsync(cancellationToken);
        if (hawalas.Count == 0)
            return;

        var rateDates = hawalas.Where(x =>
                (request.HawalaType == "HawalaReceive" ? x.FromCurrencyId : x.ToCurrencyId) == afnId &&
                request.CommissionScope != "Origin" &&
                (request.HawalaType == "HawalaReceive" || x.SourceHawala?.CorrespondentId is null))
            .Select(x => x.CreatedAt.ToLocalTime().Date)
            .Distinct()
            .ToArray();
        var rates = await context.DailyCommissionRates.AsNoTracking()
            .Where(x => rateDates.Contains(x.RateDate))
            .ToDictionaryAsync(x => x.RateDate, x => x.UsdToAfnRate, cancellationToken);
        var missingDates = rateDates.Where(date => !rates.ContainsKey(date)).OrderBy(x => x).ToList();
        if (missingDates.Count > 0)
            throw new InvalidOperationException(
                $"نرخ پایان روز برای تاریخ‌های زیر ثبت نشده است: {string.Join("، ", missingDates.Select(x => x.ToString("yyyy-MM-dd")))}");

        var sourcedHawalas = hawalas.Where(x =>
            (request.HawalaType == "HawalaSend" && x.ToCurrencyId == afnId &&
             x.SourceHawala?.CorrespondentId is not null) ||
            (request.CommissionScope == "Origin" && x.FromCurrencyId == afnId && x.CorrespondentId.HasValue)).ToList();
        var sourceIds = sourcedHawalas.Select(x => request.CommissionScope == "Origin"
                ? x.CorrespondentId!.Value : x.SourceHawala!.CorrespondentId!.Value)
            .Distinct().ToArray();
        var sourceDates = sourcedHawalas.Select(x => x.CreatedAt.ToLocalTime().Date)
            .Distinct().ToArray();
        var sourceRates = sourceIds.Length == 0
            ? new Dictionary<(long CorrespondentId, DateTime Date), decimal>()
            : (await context.CorrespondentDailyCommissionRates.AsNoTracking()
                .Where(x => sourceIds.Contains(x.CorrespondentId) && sourceDates.Contains(x.RateDate))
                .ToListAsync(cancellationToken))
                .ToDictionary(x => (x.CorrespondentId, x.RateDate), x => x.UsdToAfnRate);
        var missingSource = sourcedHawalas.FirstOrDefault(x => !sourceRates.ContainsKey(
            (request.CommissionScope == "Origin" ? x.CorrespondentId!.Value : x.SourceHawala!.CorrespondentId!.Value,
             x.CreatedAt.ToLocalTime().Date)));
        if (missingSource is not null)
        {
            var sourceId = request.CommissionScope == "Origin"
                ? missingSource.CorrespondentId!.Value : missingSource.SourceHawala!.CorrespondentId!.Value;
            var sourceName = await context.Correspondents.AsNoTracking()
                .Where(x => x.Id == sourceId).Select(x => x.Name)
                .SingleAsync(cancellationToken);
            throw new InvalidOperationException(
                $"نرخ روز {missingSource.CreatedAt.ToLocalTime():yyyy-MM-dd} برای نمایندگی مبدأ «{sourceName}» ثبت نشده است.");
        }

        var valuedAt = DateTime.UtcNow;
        foreach (var hawala in hawalas)
        {
            var valuationDate = hawala.CreatedAt.ToLocalTime().Date;
            var currencyId = request.HawalaType == "HawalaReceive"
                ? hawala.FromCurrencyId
                : hawala.ToCurrencyId;
            var amount = request.HawalaType == "HawalaReceive"
                ? hawala.FromAmount
                : hawala.ToAmount ?? hawala.FromAmount;
            var rate = currencyId != afnId ? (decimal?)null
                : request.CommissionScope == "Origin" && hawala.CorrespondentId is long originId
                    ? sourceRates[(originId, valuationDate)]
                    : request.HawalaType == "HawalaSend" && hawala.SourceHawala?.CorrespondentId is long sourceId
                    ? sourceRates[(sourceId, valuationDate)]
                    : rates[valuationDate];
            hawala.CommissionBaseUsdAmount = currencyId == usdId
                ? decimal.Round(amount, 8, MidpointRounding.AwayFromZero)
                : decimal.Round(amount / rate!.Value, 8, MidpointRounding.AwayFromZero);
            hawala.CommissionUsdToAfnRate = rate;
            hawala.CommissionValuationDate = valuationDate;
            hawala.CommissionValuedAt = valuedAt;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureOriginUsdValuationsAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var utcStart = request.PeriodFrom.Date.ToUniversalTime();
        var utcEnd = request.PeriodTo.Date.AddDays(1).ToUniversalTime();
        var currencies = await context.Currencies.AsNoTracking()
            .Where(x => x.Code == "AFN" || x.Code == "USD")
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);
        if (!currencies.TryGetValue("AFN", out var afnId) ||
            !currencies.TryGetValue("USD", out var usdId))
            throw new InvalidOperationException("ارزهای فعال USD و AFN در سیستم یافت نشد.");

        // Only transfer the columns needed for valuation. Loading tracked Hawala entities
        // and saving every row made each preview and post repeat hundreds of UPDATEs.
        var hawalas = await context.Hawalas.AsNoTracking()
            .Where(x => x.CorrespondentId == request.CorrespondentId &&
                        x.HawalaType == "HawalaReceive" && x.Status != "Cancel" &&
                        x.CreatedAt >= utcStart && x.CreatedAt < utcEnd &&
                        (x.CommissionAmount == null || x.CommissionAmount == 0) &&
                        (x.FromCurrencyId == afnId || x.FromCurrencyId == usdId) &&
                        context.Hawalas.Any(generated => generated.SourceHawalaId == x.Id &&
                                                         generated.HawalaType == "HawalaSend" &&
                                                         generated.Status != "Cancel") &&
                        !context.CorrespondentCommissionBatchItems.Any(item =>
                            item.HawalaId == x.Id && item.IsActive))
            .Select(x => new
            {
                x.Id, x.CreatedAt, x.FromCurrencyId, x.FromAmount,
                x.CommissionBaseUsdAmount, x.CommissionUsdToAfnRate,
                x.CommissionValuationDate
            })
            .ToListAsync(cancellationToken);
        if (hawalas.Count == 0)
            return;

        var rateDates = hawalas.Where(x => x.FromCurrencyId == afnId)
            .Select(x => x.CreatedAt.ToLocalTime().Date).Distinct().ToArray();
        var rates = rateDates.Length == 0
            ? new Dictionary<DateTime, decimal>()
            : await context.CorrespondentDailyCommissionRates.AsNoTracking()
                .Where(x => x.CorrespondentId == request.CorrespondentId &&
                            rateDates.Contains(x.RateDate))
                .ToDictionaryAsync(x => x.RateDate, x => x.UsdToAfnRate, cancellationToken);
        var missingDate = rateDates.OrderBy(x => x).FirstOrDefault(x =>
            !rates.TryGetValue(x, out var rate) || rate <= 0);
        if (missingDate != default)
            throw new InvalidOperationException(
                $"نرخ روز {missingDate:yyyy-MM-dd} برای نمایندگی مبدأ ثبت نشده است.");

        var pending = new List<OriginValuation>();
        foreach (var hawala in hawalas)
        {
            var valuationDate = hawala.CreatedAt.ToLocalTime().Date;
            decimal? rate = hawala.FromCurrencyId == afnId ? rates[valuationDate] : null;
            var baseUsd = hawala.FromCurrencyId == usdId
                ? decimal.Round(hawala.FromAmount, 8, MidpointRounding.AwayFromZero)
                : decimal.Round(hawala.FromAmount / rate!.Value, 8,
                    MidpointRounding.AwayFromZero);
            if (hawala.CommissionBaseUsdAmount == baseUsd &&
                hawala.CommissionUsdToAfnRate == rate &&
                hawala.CommissionValuationDate?.Date == valuationDate)
                continue;

            pending.Add(new OriginValuation(hawala.Id, baseUsd, rate, valuationDate));
        }

        const string sql = """
            UPDATE h
            SET h.[CommissionBaseUsdAmount] = v.[BaseUsd],
                h.[CommissionUsdToAfnRate] = v.[Rate],
                h.[CommissionValuationDate] = v.[ValuationDate],
                h.[CommissionValuedAt] = SYSUTCDATETIME()
            FROM [dbo].[Hawalas] h
            INNER JOIN OPENJSON(@valuations)
                WITH ([Id] bigint '$.Id', [BaseUsd] decimal(18,8) '$.BaseUsd',
                      [Rate] decimal(18,8) '$.Rate',
                      [ValuationDate] date '$.ValuationDate') v ON v.[Id] = h.[Id]
            WHERE h.[TenantId] = @tenantId;
            """;
        foreach (var chunk in pending.Chunk(1000))
        {
            await context.Database.ExecuteSqlRawAsync(sql,
                [new SqlParameter("@valuations", SqlDbType.NVarChar, -1)
                    { Value = JsonSerializer.Serialize(chunk) },
                 new SqlParameter("@tenantId", SqlDbType.BigInt)
                    { Value = context.CurrentTenantId }], cancellationToken);
        }
    }

    private sealed record OriginValuation(
        long Id, decimal BaseUsd, decimal? Rate, DateTime ValuationDate);

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

}
