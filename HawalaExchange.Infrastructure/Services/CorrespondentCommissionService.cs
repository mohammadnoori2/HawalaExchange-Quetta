using System.Data;
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
        await EnsureCommissionUsdValuationsAsync(request, cancellationToken);
        var preview = await ExecutePreviewAsync(request, cancellationToken);
        var deductions = await GetPendingCancellationDeductionsAsync(
            request.CorrespondentId, request.HawalaType, cancellationToken);
        ApplyDeductionsToPreview(
            preview,
            deductions,
            request.CommissionPerLakhAfn,
            request.HawalaType == "HawalaSend");
        return preview;
    }

    public async Task<CorrespondentCommissionBatchDto> PostAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
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
            request.CorrespondentId, request.HawalaType, cancellationToken);
        if (deductions.Count > 0)
            await AddDeductionsToPostedBatchAsync(
                result, deductions, request.HawalaType == "HawalaSend", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<List<CorrespondentCommissionBatchItem>> GetPendingCancellationDeductionsAsync(
        long correspondentId,
        string hawalaType,
        CancellationToken cancellationToken)
    {
        var candidates = await context.CorrespondentCommissionBatchItems
            .AsNoTracking()
            .Include(x => x.Hawala)
            .Include(x => x.SourceCurrency)
            .Include(x => x.Batch)
            .Where(x => !x.IsActive && x.Batch.Status == "Posted" &&
                        x.Batch.CorrespondentId == correspondentId &&
                        x.Hawala.HawalaType == hawalaType &&
                        x.Hawala.Status == "Cancel" &&
                        !context.CorrespondentCommissionBatchItems.Any(active =>
                            active.HawalaId == x.HawalaId && active.IsActive))
            .OrderByDescending(x => x.Batch.CreatedAt)
            .ToListAsync(cancellationToken);
        return candidates.GroupBy(x => x.HawalaId).Select(x => x.First()).ToList();
    }

    private static void ApplyDeductionsToPreview(
        CorrespondentCommissionPreviewDto preview,
        IReadOnlyCollection<CorrespondentCommissionBatchItem> deductions,
        decimal commissionPerLakhAfn,
        bool isOutgoing)
    {
        foreach (var deduction in deductions)
        {
            preview.Items.Add(new CorrespondentCommissionItemDto
            {
                HawalaId = deduction.HawalaId,
                HawalaNumber = deduction.Hawala.Number,
                HawalaDate = deduction.Hawala.CreatedAt,
                CurrencyId = deduction.SourceCurrencyId,
                CurrencyCode = deduction.SourceCurrency.Code,
                SourceAmount = -Math.Abs(deduction.SourceAmount),
                SourceToAfnRate = deduction.SourceToAfnRate,
                AfnEquivalent = -Math.Abs(deduction.AfnEquivalent),
                CommissionAfn = -Math.Abs(deduction.CommissionAfn)
            });
        }

        preview.TotalBaseAfn = decimal.Round(
            preview.Items.Sum(x => x.AfnEquivalent), isOutgoing ? 0 : 4,
            MidpointRounding.AwayFromZero);
        if (isOutgoing)
        {
            preview.TotalCommissionAfn = decimal.Round(
                preview.Items.Where(x => x.CurrencyCode == "AFN").Sum(x => x.CommissionAfn),
                0, MidpointRounding.AwayFromZero);
            preview.TotalCommissionUsd = decimal.Round(
                preview.Items.Where(x => x.CurrencyCode == "USD").Sum(x => x.CommissionAfn),
                0, MidpointRounding.AwayFromZero);
        }
        else
        {
            preview.TotalCommissionAfn = 0;
            preview.TotalCommissionUsd = decimal.Round(
                preview.TotalBaseAfn / 100000m * commissionPerLakhAfn,
                0, MidpointRounding.AwayFromZero);
        }
    }

    private async Task AddDeductionsToPostedBatchAsync(
        CorrespondentCommissionBatchDto result,
        IReadOnlyCollection<CorrespondentCommissionBatchItem> deductions,
        bool isOutgoing,
        CancellationToken cancellationToken)
    {
        var batch = await context.CorrespondentCommissionBatches
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == result.Id, cancellationToken);
        foreach (var deduction in deductions)
        {
            batch.Items.Add(new CorrespondentCommissionBatchItem
            {
                HawalaId = deduction.HawalaId,
                SourceCurrencyId = deduction.SourceCurrencyId,
                SourceAmount = -Math.Abs(deduction.SourceAmount),
                SourceToAfnRate = deduction.SourceToAfnRate,
                AfnEquivalent = -Math.Abs(deduction.AfnEquivalent),
                CommissionAfn = -Math.Abs(deduction.CommissionAfn),
                IsActive = true
            });
        }

        batch.TotalBaseAfn = decimal.Round(
            batch.Items.Sum(x => x.AfnEquivalent), isOutgoing ? 0 : 4,
            MidpointRounding.AwayFromZero);
        var currencyCodes = await context.Currencies.AsNoTracking()
            .Where(x => x.Code == "AFN" || x.Code == "USD")
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        if (isOutgoing)
        {
            batch.TotalCommissionAfn = decimal.Round(batch.Items
                .Where(x => currencyCodes.GetValueOrDefault(x.SourceCurrencyId) == "AFN")
                .Sum(x => x.CommissionAfn), 0, MidpointRounding.AwayFromZero);
            batch.TotalCommissionUsd = decimal.Round(batch.Items
                .Where(x => currencyCodes.GetValueOrDefault(x.SourceCurrencyId) == "USD")
                .Sum(x => x.CommissionAfn), 0, MidpointRounding.AwayFromZero);
        }
        else
        {
            batch.TotalCommissionAfn = 0;
            batch.TotalCommissionUsd = decimal.Round(
                batch.TotalBaseAfn / 100000m * batch.CommissionPerLakhAfn,
                0, MidpointRounding.AwayFromZero);
        }
        var postingAmount = isOutgoing ? batch.TotalBaseAfn : batch.TotalCommissionUsd;
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

    private async Task RebuildOutgoingLedgerAsync(
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
        if (activeItems.Any(x => !x.SourceCorrespondentId.HasValue))
            throw new InvalidOperationException("نمایندگی فرستنده یک یا چند حواله ارسالی یافت نشد.");

        var sourceIds = activeItems.Select(x => x.SourceCorrespondentId!.Value).Distinct().ToArray();
        var sourceAccounts = await context.Accounts.AsNoTracking()
            .Where(x => x.CorrespondentId.HasValue && sourceIds.Contains(x.CorrespondentId.Value) && !x.IsArchived)
            .GroupBy(x => x.CorrespondentId!.Value)
            .Select(x => new { CorrespondentId = x.Key, AccountId = x.Min(a => a.Id) })
            .ToDictionaryAsync(x => x.CorrespondentId, x => x.AccountId, cancellationToken);
        if (sourceIds.Any(id => !sourceAccounts.ContainsKey(id)))
            throw new InvalidOperationException("حساب فعال نمایندگی فرستنده یک یا چند حواله یافت نشد.");

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
                0, MidpointRounding.AwayFromZero);
            if (afnUsd > 0)
                context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId, clearing.Id,
                    usdId, afnUsd, 0, description));
        }
        foreach (var group in activeItems.GroupBy(x => x.SourceCorrespondentId!.Value))
        {
            var amount = decimal.Round(group.Sum(x => x.AfnEquivalent), 0,
                MidpointRounding.AwayFromZero);
            if (amount > 0)
                context.LedgerEntries.Add(NewEntry(batch.PostingTransactionId,
                    sourceAccounts[group.Key], usdId, 0, amount, description));
        }
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
                HawalaType = x.Items.Select(i => i.Hawala.HawalaType).FirstOrDefault() ?? "HawalaReceive",
                PeriodFrom = x.PeriodFrom, PeriodTo = x.PeriodTo, HawalaCount = x.Items.Count,
                CommissionPerLakhAfn = x.CommissionPerLakhAfn, UsdToAfnRate = x.UsdToAfnRate,
                TotalBaseAfn = x.TotalBaseAfn, TotalCommissionAfn = x.TotalCommissionAfn,
                TotalCommissionUsd = x.TotalCommissionUsd, Status = x.Status, CreatedAt = x.CreatedAt
            }).ToListAsync(cancellationToken);

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
            .Include(x => x.Items).ThenInclude(x => x.Hawala)
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
                    CurrencyId = x.SourceCurrencyId,
                    CurrencyCode = x.SourceCurrency.Code,
                    SourceAmount = x.SourceAmount,
                    SourceToAfnRate = x.SourceToAfnRate,
                    AfnEquivalent = x.AfnEquivalent,
                    CommissionAfn = x.CommissionAfn,
                    IsActive = x.IsActive
                }).ToList(),
            LedgerEntries = ledgerEntries
        };
    }

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
                        CommissionAfn = reader.GetDecimal(8)
                    });
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
            command.CommandTimeout = 180;
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
        if (request.Rates.Any(x => x.SourceToAfnRate < 0))
            throw new InvalidOperationException("نرخ تبدیل ارز نمی‌تواند منفی باشد.");
    }

    private async Task EnsureCommissionUsdValuationsAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
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
            .Where(x => x.CorrespondentId == request.CorrespondentId &&
                        x.HawalaType == request.HawalaType &&
                        x.Status != "Cancel" &&
                        x.CreatedAt >= utcStart && x.CreatedAt < utcEnd &&
                        (request.HawalaType == "HawalaReceive"
                            ? x.CommissionAmount == null || x.CommissionAmount == 0
                            : x.AgentCommissionAmount == null || x.AgentCommissionAmount == 0) &&
                        (request.HawalaType == "HawalaReceive"
                            ? x.FromCurrencyId == afnId || x.FromCurrencyId == usdId
                            : x.ToCurrencyId == afnId || x.ToCurrencyId == usdId) &&
                        !context.CorrespondentCommissionBatchItems.Any(item =>
                            item.HawalaId == x.Id && item.IsActive))
            .ToListAsync(cancellationToken);
        if (hawalas.Count == 0)
            return;

        var rateDates = hawalas.Where(x =>
                (request.HawalaType == "HawalaReceive" ? x.FromCurrencyId : x.ToCurrencyId) == afnId)
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
            var rate = currencyId == afnId ? rates[valuationDate] : (decimal?)null;
            hawala.CommissionBaseUsdAmount = currencyId == usdId
                ? decimal.Round(amount, 8, MidpointRounding.AwayFromZero)
                : decimal.Round(amount / rate!.Value, 8, MidpointRounding.AwayFromZero);
            hawala.CommissionUsdToAfnRate = rate;
            hawala.CommissionValuationDate = valuationDate;
            hawala.CommissionValuedAt = valuedAt;
        }
        await context.SaveChangesAsync(cancellationToken);
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

}
