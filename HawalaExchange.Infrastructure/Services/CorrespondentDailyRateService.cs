using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class CorrespondentDailyRateService(ApplicationDbContext context)
    : ICorrespondentDailyRateService
{
    public async Task<CorrespondentDailyRateDto> GetAsync(
        long correspondentId, DateTime date, CancellationToken cancellationToken = default)
    {
        var day = date.Date;
        var rate = await context.CorrespondentDailyCommissionRates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CorrespondentId == correspondentId && x.RateDate == day,
                cancellationToken);
        return new CorrespondentDailyRateDto
        {
            CorrespondentId = correspondentId,
            RateDate = day,
            UsdToAfnRate = rate?.UsdToAfnRate,
            ModifiedAt = rate?.ModifiedAt ?? rate?.CreatedAt
        };
    }

    public async Task<CorrespondentDailyRateDto> SaveAsync(
        long correspondentId, DateTime date, decimal usdToAfnRate,
        CancellationToken cancellationToken = default)
    {
        try { return await SaveCoreAsync(correspondentId, date, usdToAfnRate, null, false, cancellationToken); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async Task<CorrespondentDailyRateDto> SaveConfirmedAsync(CorrespondentDailyRateImpactDto confirmation,
        bool applyToHawalas, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SaveCoreAsync(confirmation.CorrespondentId, confirmation.RateDate,
                confirmation.NewUsdToAfnRate, confirmation, applyToHawalas, cancellationToken);
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async Task<CorrespondentDailyRateImpactDto> GetImpactAsync(long correspondentId, DateTime date,
        decimal usdToAfnRate, CancellationToken cancellationToken = default)
    {
        if (usdToAfnRate <= 0 || date.Date > DateTime.Today)
            throw new InvalidOperationException("تاریخ و نرخ نمایندگی معتبر وارد کنید.");
        var day = date.Date;
        var start = day.ToUniversalTime();
        var end = day.AddDays(1).ToUniversalTime();
        var daily = await GetAsync(correspondentId, day, cancellationToken);
        var items = await context.CorrespondentSettlementConversionHawalaItems.AsNoTracking()
            .Include(x => x.Hawala).Include(x => x.SourceCurrency)
            .Include(x => x.Conversion).ThenInclude(x => x.TargetCurrency)
            .Where(x => x.Conversion.CorrespondentId == correspondentId && x.Conversion.SourceMode == "Hawalas" &&
                x.Hawala.CreatedAt >= start && x.Hawala.CreatedAt < end && x.Hawala.Status != "Cancel")
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var closedThrough = await context.CorrespondentAccountPeriods.AsNoTracking()
            .Where(x => x.CorrespondentId == correspondentId).Select(x => (DateTime?)x.PeriodTo)
            .MaxAsync(cancellationToken);
        var impact = new CorrespondentDailyRateImpactDto
        {
            CorrespondentId = correspondentId, RateDate = day, NewUsdToAfnRate = usdToAfnRate,
            PreviousUsdToAfnRate = daily.UsdToAfnRate
        };
        foreach (var item in items)
        {
            if (!CorrespondentRateCalculator.Supports(item.SourceCurrency.Code, item.Conversion.TargetCurrency.Code))
                continue;
            var newRate = CorrespondentRateCalculator.ToSettlementRate(item.SourceCurrency.Code, item.SourceCurrency.QuotationPriority,
                item.Conversion.TargetCurrency.Code, item.Conversion.TargetCurrency.QuotationPriority, usdToAfnRate);
            if (item.ExchangeRate == newRate)
                continue;
            var target = CurrencyQuotationCalculator.ConvertFromAmount(item.SourceCurrencyId, item.SourceCurrency.Code,
                item.SourceCurrency.QuotationPriority, Math.Abs(item.SourceTalabKar - item.SourceBadehKar),
                item.Conversion.TargetCurrencyId, item.Conversion.TargetCurrency.Code,
                item.Conversion.TargetCurrency.QuotationPriority, newRate).ToAmount;
            impact.Hawalas.Add(new CorrespondentRateAffectedHawalaDto
            {
                ItemId = item.Id, HawalaNumber = item.Hawala.Number, SourceCurrencyCode = item.SourceCurrency.Code,
                TargetCurrencyCode = item.Conversion.TargetCurrency.Code, PreviousRate = item.ExchangeRate, NewRate = newRate,
                PreviousTargetAmount = Math.Abs(item.TargetTalabKar - item.TargetBadehKar),
                NewTargetAmount = decimal.Round(target, Math.Clamp(item.Conversion.TargetCurrency.DecimalPlaces, 0, 8), MidpointRounding.AwayFromZero),
                IsClosed = closedThrough.HasValue && (item.Hawala.CreatedAt <= closedThrough || item.Conversion.CreatedAt <= closedThrough)
            });
        }
        return impact;
    }

    private async Task<CorrespondentDailyRateDto> SaveCoreAsync(long correspondentId, DateTime date,
        decimal usdToAfnRate, CorrespondentDailyRateImpactDto? confirmation, bool applyToHawalas,
        CancellationToken cancellationToken)
    {
        var day = date.Date;
        if (day > DateTime.Today)
            throw new InvalidOperationException("برای روز آینده نمی‌توان نرخ کمیشن نمایندگی ثبت کرد.");
        if (usdToAfnRate <= 0)
            throw new InvalidOperationException("نرخ USD به AFN باید بزرگ‌تر از صفر باشد.");

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await context.Correspondents.AsNoTracking().AnyAsync(
                x => x.Id == correspondentId && !x.IsArchived, cancellationToken))
            throw new InvalidOperationException("نمایندگی مبدأ فعال یافت نشد.");

        var rate = await context.CorrespondentDailyCommissionRates.SingleOrDefaultAsync(
            x => x.CorrespondentId == correspondentId && x.RateDate == day, cancellationToken);
        if (confirmation != null)
        {
            var current = await GetImpactAsync(correspondentId, day, usdToAfnRate, cancellationToken);
            if (current.PreviousUsdToAfnRate != confirmation.PreviousUsdToAfnRate ||
                !current.Hawalas.Select(x => (x.ItemId, x.PreviousRate, x.PreviousTargetAmount, x.NewRate, x.NewTargetAmount, x.IsClosed))
                    .SequenceEqual(confirmation.Hawalas.Select(x => (x.ItemId, x.PreviousRate, x.PreviousTargetAmount, x.NewRate, x.NewTargetAmount, x.IsClosed))))
                throw new InvalidOperationException("نرخ یا حواله‌ها پس از پیش‌نمایش تغییر کرده‌اند؛ دوباره بررسی و تأیید کنید.");
            if (applyToHawalas && current.Hawalas.Any(x => x.IsClosed))
                throw new InvalidOperationException("بعضی حواله‌ها شامل دوره بسته‌شده هستند؛ اعمال گروهی نرخ به آن‌ها مجاز نیست.");
        }
        if (rate is not null && rate.UsdToAfnRate != usdToAfnRate)
        {
            var utcStart = day.ToUniversalTime();
            var utcEnd = day.AddDays(1).ToUniversalTime();
            var posted = await context.CorrespondentCommissionBatchItems.AsNoTracking().AnyAsync(
                item => item.IsActive && item.Batch.Status == "Posted" &&
                    ((item.Batch.CorrespondentId == correspondentId &&
                      (item.Batch.CommissionScope == "Incoming" || item.Batch.CommissionScope == "Forwarding" || item.Batch.CommissionScope == "Origin") &&
                      (item.ValuationDate == day || (item.ValuationDate == null &&
                       item.Hawala.CreatedAt >= utcStart && item.Hawala.CreatedAt < utcEnd))) ||
                     (item.Batch.AccountingVersion < 2 && item.Hawala.HawalaType == "HawalaSend" &&
                      item.Hawala.SourceHawala != null && item.Hawala.SourceHawala.CorrespondentId == correspondentId &&
                      item.Hawala.CreatedAt >= utcStart && item.Hawala.CreatedAt < utcEnd)),
                cancellationToken);
            if (posted)
                throw new InvalidOperationException(
                    "کمیشن حواله‌های این نمایندگی و روز قبلاً ثبت شده است؛ نرخ آن قابل تغییر نیست.");
        }

        var now = DateTime.UtcNow;
        if (rate is null)
        {
            rate = new CorrespondentDailyCommissionRate
            {
                CorrespondentId = correspondentId,
                RateDate = day,
                UsdToAfnRate = usdToAfnRate,
                CreatedBy = context.RequireCurrentUserId(),
                CreatedAt = now
            };
            context.CorrespondentDailyCommissionRates.Add(rate);
        }
        else if (rate.UsdToAfnRate != usdToAfnRate)
        {
            rate.UsdToAfnRate = usdToAfnRate;
            rate.ModifiedBy = context.RequireCurrentUserId();
            rate.ModifiedAt = now;
        }

        if (applyToHawalas && confirmation != null)
            await new CorrespondentSettlementService(context).ApplyDailyRateToHawalasAsync(
                correspondentId, confirmation.Hawalas.Select(x => x.ItemId).ToArray(), usdToAfnRate, cancellationToken);
        else
            await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(correspondentId, day, cancellationToken);
    }
}
