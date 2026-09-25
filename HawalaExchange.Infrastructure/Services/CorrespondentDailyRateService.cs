using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
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
        if (rate is not null && rate.UsdToAfnRate != usdToAfnRate)
        {
            var utcStart = day.ToUniversalTime();
            var utcEnd = day.AddDays(1).ToUniversalTime();
            var posted = await context.CorrespondentCommissionBatchItems.AsNoTracking().AnyAsync(
                item => item.IsActive && item.Batch.Status == "Posted" &&
                    item.Hawala.HawalaType == "HawalaSend" &&
                    item.Hawala.SourceHawala != null &&
                    item.Hawala.SourceHawala.CorrespondentId == correspondentId &&
                    item.Hawala.CreatedAt >= utcStart && item.Hawala.CreatedAt < utcEnd,
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

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(correspondentId, day, cancellationToken);
    }
}
