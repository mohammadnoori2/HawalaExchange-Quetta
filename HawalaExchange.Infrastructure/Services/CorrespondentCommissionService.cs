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
        var preview = await ExecutePreviewAsync(request, cancellationToken);
        var deductions = await GetPendingCancellationDeductionsAsync(
            request.CorrespondentId, cancellationToken);
        ApplyDeductionsToPreview(preview, deductions, request.CommissionPerLakhAfn, request.UsdToAfnRate);
        return preview;
    }

    public async Task<CorrespondentCommissionBatchDto> PostAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request, requireUsdRate: true);
        var rates = CreateRatesTable(request.Rates);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
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
            request.CorrespondentId, cancellationToken);
        if (deductions.Count > 0)
            await AddDeductionsToPostedBatchAsync(result, deductions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<List<CorrespondentCommissionBatchItem>> GetPendingCancellationDeductionsAsync(
        long correspondentId,
        CancellationToken cancellationToken)
    {
        var candidates = await context.CorrespondentCommissionBatchItems
            .AsNoTracking()
            .Include(x => x.Hawala)
            .Include(x => x.SourceCurrency)
            .Include(x => x.Batch)
            .Where(x => !x.IsActive && x.Batch.Status == "Posted" &&
                        x.Batch.CorrespondentId == correspondentId &&
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
        decimal usdToAfnRate)
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
            preview.Items.Sum(x => x.AfnEquivalent), 4, MidpointRounding.AwayFromZero);
        preview.TotalCommissionAfn = decimal.Round(
            preview.TotalBaseAfn / 100000m * commissionPerLakhAfn,
            0, MidpointRounding.AwayFromZero);
        preview.TotalCommissionUsd = usdToAfnRate > 0
            ? decimal.Round(preview.TotalCommissionAfn / usdToAfnRate, 0, MidpointRounding.AwayFromZero)
            : 0;
    }

    private async Task AddDeductionsToPostedBatchAsync(
        CorrespondentCommissionBatchDto result,
        IReadOnlyCollection<CorrespondentCommissionBatchItem> deductions,
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
            batch.Items.Sum(x => x.AfnEquivalent), 4, MidpointRounding.AwayFromZero);
        batch.TotalCommissionAfn = decimal.Round(
            batch.TotalBaseAfn / 100000m * batch.CommissionPerLakhAfn,
            0, MidpointRounding.AwayFromZero);
        batch.TotalCommissionUsd = decimal.Round(
            batch.TotalCommissionAfn / batch.UsdToAfnRate,
            0, MidpointRounding.AwayFromZero);
        if (batch.TotalCommissionUsd <= 0)
            throw new InvalidOperationException(
                "پس از کسر حواله‌های لغوشده، کمیشن قابل پرداختی باقی نمی‌ماند.");

        var ledgerEntries = await context.LedgerEntries
            .Where(x => x.TransactionId == batch.PostingTransactionId)
            .ToListAsync(cancellationToken);
        foreach (var entry in ledgerEntries)
        {
            if (entry.TalabKar > 0) entry.TalabKar = batch.TotalCommissionUsd;
            if (entry.BadehKar > 0) entry.BadehKar = batch.TotalCommissionUsd;
        }
        await context.SaveChangesAsync(cancellationToken);

        result.HawalaCount = batch.Items.Count;
        result.TotalBaseAfn = batch.TotalBaseAfn;
        result.TotalCommissionAfn = batch.TotalCommissionAfn;
        result.TotalCommissionUsd = batch.TotalCommissionUsd;
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

    private async Task<CorrespondentCommissionPreviewDto> ExecutePreviewAsync(
        CorrespondentCommissionPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, requireUsdRate: false);
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

    private static void ValidateRequest(CorrespondentCommissionPreviewRequestDto request, bool requireUsdRate)
    {
        if (request.CorrespondentId <= 0)
            throw new InvalidOperationException("نمایندگی معتبر انتخاب نشده است.");
        if (request.PeriodTo.Date < request.PeriodFrom.Date)
            throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ آغاز باشد.");
        if (request.CommissionPerLakhAfn <= 0)
            throw new InvalidOperationException("کمیشن هر لک باید بزرگ‌تر از صفر باشد.");
        if (requireUsdRate && request.UsdToAfnRate <= 0)
            throw new InvalidOperationException("نرخ تبدیل USD به AFN الزامی است.");
        if (request.Rates.Any(x => x.SourceToAfnRate < 0))
            throw new InvalidOperationException("نرخ تبدیل ارز نمی‌تواند منفی باشد.");
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
