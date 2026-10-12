using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed partial class CorrespondentSettlementService
{
    internal Task CaptureAsync(long conversionId, DateTime? rateDate, bool confirmDifferentRate, CancellationToken token = default)
        => context.Database.ExecuteSqlInterpolatedAsync($"EXEC dbo.usp_CaptureCorrespondentSettlement_v1 @TenantId={context.CurrentTenantId},@ConversionId={conversionId},@UserId={context.RequireCurrentUserId()},@RateDate={rateDate},@UtcOffsetMinutes={(int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes},@ConfirmDifferentDailyRate={confirmDifferentRate}", token);

    public async Task<IReadOnlyList<CorrespondentSettlementDailyRateDto>> GetRatesAsync(long correspondentId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        using var scope = PinCurrentTenant();
        return await context.Database.SqlQuery<CorrespondentSettlementDailyRateDto>($"""
            SELECT r.RateDate,r.SourceCurrencyId,r.TargetCurrencyId,s.Code SourceCurrencyCode,t.Code TargetCurrencyCode,r.ExchangeRate,r.ModifiedAt
            FROM dbo.CorrespondentSettlementDailyRates r
            JOIN dbo.Currencies s ON s.TenantId=r.TenantId AND s.Id=r.SourceCurrencyId
            JOIN dbo.Currencies t ON t.TenantId=r.TenantId AND t.Id=r.TargetCurrencyId
            WHERE r.TenantId={context.CurrentTenantId} AND r.CorrespondentId={correspondentId}
                AND ({from} IS NULL OR r.RateDate>={from}) AND ({to} IS NULL OR r.RateDate<={to})
            ORDER BY r.RateDate DESC,s.Code,t.Code
            """).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CorrespondentSettlementHistoryDto>> GetHistoryAsync(CorrespondentSettlementHistoryFilterDto filter, CancellationToken cancellationToken = default)
    {
        using var scope = PinCurrentTenant();
        var rows = await ReadHistoryRowsAsync(filter, null, cancellationToken);
        return rows.Select(MapHistory).ToList();
    }

    public async Task<CorrespondentSettlementHistoryDto> GetHistoryDetailsAsync(long correspondentId, long conversionId, CancellationToken cancellationToken = default)
    {
        using var scope = PinCurrentTenant();
        var rows = await ReadHistoryRowsAsync(new() { CorrespondentId = correspondentId }, conversionId, cancellationToken);
        var details = MapHistory(rows.SingleOrDefault() ?? throw new KeyNotFoundException("تبدیل نمایندگی یافت نشد."));
        var revisions = await context.Database.SqlQuery<SettlementRevisionRow>($"""
            SELECT s.CreatedAt,u.FullName UserName,s.SnapshotJson FROM dbo.CorrespondentSettlementHistorySnapshots s
            JOIN dbo.Users u ON u.TenantId=s.TenantId AND u.Id=s.CreatedBy
            WHERE s.TenantId={context.CurrentTenantId} AND s.CorrespondentId={correspondentId} AND s.ConversionId={conversionId}
            ORDER BY s.Id
            """).ToListAsync(cancellationToken);
        details.Revisions = revisions.Select(x => new CorrespondentSettlementHistoryRevisionDto
        { CreatedAt = x.CreatedAt, UserName = x.UserName, Items = ReadItems(x.SnapshotJson) }).ToList();
        return details;
    }

    private Task<List<SettlementHistoryRow>> ReadHistoryRowsAsync(CorrespondentSettlementHistoryFilterDto filter, long? id, CancellationToken token)
    {
        if (filter.FromDate > filter.ToDate) throw new InvalidOperationException("بازه تاریخ معتبر نیست.");
        var size = Math.Clamp(filter.PageSize, 1, 100);
        var skip = (Math.Clamp(filter.Page, 1, 100000) - 1) * size;
        var from = filter.FromDate?.Date; var to = filter.ToDate?.Date;
        return context.Database.SqlQuery<SettlementHistoryRow>($"""
            SELECT c.Id ConversionId,c.CreatedAt,u.FullName UserName,c.SourceMode,c.Note,t.Code TargetCurrencyCode,
                latest.Status,
                CASE WHEN {id} IS NOT NULL THEN latest.SnapshotJson ELSE
                    (SELECT j.RateDate,j.SourceCurrencyId,j.SourceCurrencyCode,j.ExchangeRate,SUM(j.SourceAmount) SourceAmount,
                        SUM(j.TargetAmount) TargetAmount,SUM(j.PrincipalTargetAmount) PrincipalTargetAmount,SUM(j.UsdAmount) UsdAmount
                    FROM OPENJSON(latest.SnapshotJson) WITH(RateDate date,SourceCurrencyId bigint,SourceCurrencyCode nvarchar(10),ExchangeRate decimal(18,8),SourceAmount decimal(18,4),TargetAmount decimal(18,4),PrincipalTargetAmount decimal(28,8),UsdAmount decimal(28,8)) j
                    GROUP BY j.RateDate,j.SourceCurrencyId,j.SourceCurrencyCode,j.ExchangeRate ORDER BY j.RateDate,j.SourceCurrencyCode FOR JSON PATH)
                END SnapshotJson,latest.HawalaCount
            FROM dbo.CorrespondentSettlementConversions c
            JOIN dbo.Currencies t ON t.TenantId=c.TenantId AND t.Id=c.TargetCurrencyId
            JOIN dbo.Users u ON u.TenantId=c.TenantId AND u.Id=c.CreatedBy
            CROSS APPLY (SELECT TOP(1) s.Status,s.SnapshotJson,s.HawalaCount FROM dbo.CorrespondentSettlementHistorySnapshots s
                WHERE s.TenantId=c.TenantId AND s.ConversionId=c.Id ORDER BY s.Id DESC) latest
            WHERE c.TenantId={context.CurrentTenantId} AND c.CorrespondentId={filter.CorrespondentId}
                AND ({id} IS NULL OR c.Id={id}) AND ({filter.Status} IS NULL OR latest.Status={filter.Status})
                AND EXISTS(SELECT 1 FROM OPENJSON(latest.SnapshotJson) WITH(RateDate date,SourceCurrencyId bigint) j
                    WHERE ({from} IS NULL OR j.RateDate>={from}) AND ({to} IS NULL OR j.RateDate<={to})
                        AND ({filter.CurrencyId} IS NULL OR j.SourceCurrencyId={filter.CurrencyId}))
            ORDER BY c.CreatedAt DESC,c.Id DESC OFFSET {skip} ROWS FETCH NEXT {size} ROWS ONLY
            """).ToListAsync(token);
    }

    private static IReadOnlyList<CorrespondentSettlementHistoryItemDto> ReadItems(string json)
        => JsonSerializer.Deserialize<List<CorrespondentSettlementHistoryItemDto>>(json) ?? [];
    private static CorrespondentSettlementHistoryDto MapHistory(SettlementHistoryRow x) => new()
    { ConversionId = x.ConversionId, CreatedAt = x.CreatedAt, UserName = x.UserName, SourceMode = x.SourceMode, Status = x.Status,
        Note = x.Note, TargetCurrencyCode = x.TargetCurrencyCode, HawalaCount = x.HawalaCount, Items = ReadItems(x.SnapshotJson) };
    private sealed class SettlementHistoryRow
    {
        public long ConversionId { get; set; } public DateTime CreatedAt { get; set; }
        public string UserName { get; set; } = ""; public string SourceMode { get; set; } = "";
        public string? Note { get; set; } public string TargetCurrencyCode { get; set; } = "";
        public string Status { get; set; } = ""; public string SnapshotJson { get; set; } = "[]";
        public int HawalaCount { get; set; }
    }
    private sealed class SettlementRevisionRow
    { public DateTime CreatedAt { get; set; } public string UserName { get; set; } = ""; public string SnapshotJson { get; set; } = "[]"; }

    public async Task ReverseAsync(long correspondentId, long conversionId, string reason, CancellationToken cancellationToken = default)
    {
        using var scope = PinCurrentTenant();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new InvalidOperationException("دلیل برگشت را وارد کنید (حداکثر 500 حرف).");
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            await context.EnsureCurrentTenantWriteAccessAsync(cancellationToken);
            await CorrespondentCommissionPeriodLock.AcquireAsync(context, correspondentId, cancellationToken);
            var conversion = await context.CorrespondentSettlementConversions.Include(x => x.Transaction).Include(x => x.Hawalas)
                .SingleOrDefaultAsync(x => x.Id == conversionId && x.CorrespondentId == correspondentId, cancellationToken)
                ?? throw new KeyNotFoundException("تبدیل نمایندگی یافت نشد.");
            if (conversion.Transaction.Status == "Cancel") throw new InvalidOperationException("این تبدیل قبلاً برگشت داده شده است.");
            await EnsureConversionCanChangeAsync(conversion, cancellationToken);
            var entries = await context.LedgerEntries.AsNoTracking().Where(x => x.TransactionId == conversion.TransactionId).ToListAsync(cancellationToken);
            var reversal = new Transaction { TransactionNo = "SCR-" + Guid.NewGuid().ToString("N"), TransactionType = "CorrespondentSettlementReversal",
                BranchId = await context.GetDefaultBranchIdAsync(cancellationToken), Status = "Paid", CreatedBy = context.RequireCurrentUserId(),
                ReversedTransactionId = conversion.TransactionId, Remarks = reason.Trim() };
            context.Transactions.Add(reversal); await context.SaveChangesAsync(cancellationToken);
            foreach (var entry in entries) context.LedgerEntries.Add(new LedgerEntry { TransactionId = reversal.Id, AccountId = entry.AccountId,
                CurrencyId = entry.CurrencyId, TalabKar = entry.BadehKar, BadehKar = entry.TalabKar, Description = "برگشت تبدیل ارز توافقی: " + reason.Trim() });
            conversion.Transaction.Status = "Cancel"; conversion.Transaction.CancelledBy = context.RequireCurrentUserId();
            conversion.Transaction.CancelledAt = DateTime.UtcNow; conversion.Transaction.CancelReason = reason.Trim();
            await context.SaveChangesAsync(cancellationToken);
            // Keep journals and immutable history, but release operational links for a new conversion.
            await context.LedgerEntries.Where(x => x.SettlementHawalaItemId != null && x.TransactionId == conversion.TransactionId)
                .ExecuteUpdateAsync(x => x.SetProperty(e => e.SettlementHawalaItemId, (long?)null), cancellationToken);
            await context.CorrespondentSettlementConversionHawalaItems.Where(x => x.ConversionId == conversionId).ExecuteDeleteAsync(cancellationToken);
            await context.CorrespondentSettlementConversionHawalas.Where(x => x.ConversionId == conversionId).ExecuteDeleteAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.CorrespondentSettlementValuations WHERE TenantId={context.CurrentTenantId} AND ConversionId={conversionId}; UPDATE dbo.CorrespondentSettlementHistorySnapshots SET Status=N'Reversed' WHERE TenantId={context.CurrentTenantId} AND ConversionId={conversionId} AND Status IN(N'Active',N'Adjusted');", cancellationToken);
            context.AuditLogs.Add(new AuditLog { Action = "REVERSE_SETTLEMENT", TableName = "CorrespondentSettlementConversions", RecordId = conversionId,
                UserId = context.RequireCurrentUserId(), NewValue = reason.Trim() });
            await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        finally { context.ChangeTracker.Clear(); }
    }

    internal async Task EnsureConversionCanChangeAsync(CorrespondentSettlementConversion conversion, CancellationToken token = default)
    {
        var ids = await context.CorrespondentSettlementConversionHawalas.Where(x => x.ConversionId == conversion.Id).Select(x => x.HawalaId).ToArrayAsync(token);
        if (await context.CorrespondentCommissionBatchItems.AnyAsync(x => x.IsActive && x.Batch.Status == "Posted" &&
            (ids.Contains(x.HawalaId) || x.Hawala.SourceHawalaId.HasValue && ids.Contains(x.Hawala.SourceHawalaId.Value)), token))
            throw new InvalidOperationException("کمیشن این حواله‌ها قبلاً ثبت شده است؛ ابتدا کمیشن مرتبط را برگشت دهید. محاسبه مجدد خودکار انجام نمی‌شود.");
        if (await context.CorrespondentAccountPeriods.AnyAsync(x => x.CorrespondentId == conversion.CorrespondentId && x.PeriodTo >= conversion.CreatedAt, token) ||
            await context.CorrespondentAccountPeriodHawalas.AnyAsync(x => ids.Contains(x.HawalaId), token))
            throw new InvalidOperationException("تبدیل در دوره بسته‌شده قرار دارد؛ سوابق دوره بسته قابل بازنویسی نیستند.");
    }

    internal Task MarkDetachedAsync(long conversionId, long hawalaId, CancellationToken token = default)
        => context.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.CorrespondentSettlementValuations WHERE TenantId={context.CurrentTenantId} AND HawalaId={hawalaId}; UPDATE dbo.CorrespondentSettlementHistorySnapshots SET Status=N'Adjusted' WHERE TenantId={context.CurrentTenantId} AND ConversionId={conversionId} AND Status=N'Active';", token);
}
