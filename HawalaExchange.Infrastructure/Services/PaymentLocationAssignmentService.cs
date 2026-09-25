using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class PaymentLocationAssignmentService(ApplicationDbContext context)
    : IPaymentLocationAssignmentService
{
    public async Task<IReadOnlyList<PaymentLocationAssignmentDto>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        await context.PaymentLocationCorrespondentAssignments.AsNoTracking()
            .OrderBy(x => x.PaymentLocation.Name)
            .ThenByDescending(x => x.EffectiveFrom)
            .Select(x => new PaymentLocationAssignmentDto
            {
                Id = x.Id,
                PaymentLocationId = x.PaymentLocationId,
                PaymentLocationName = x.PaymentLocation.Name,
                CorrespondentId = x.CorrespondentId,
                CorrespondentName = x.Correspondent.Name,
                EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo
            }).ToListAsync(cancellationToken);

    public async Task<PaymentLocationAssignmentDto> AssignAsync(
        long paymentLocationId,
        long correspondentId,
        DateTime effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        var date = effectiveFrom.Date;
        if (date < DateTime.Today)
            throw new InvalidOperationException("تاریخ آغاز مسئولیت نمی‌تواند در گذشته باشد؛ حواله‌های قبلی باید محفوظ بمانند.");

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var location = await context.PaymentLocations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == paymentLocationId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("محل پرداخت فعال پیدا نشد.");
        var correspondent = await context.Correspondents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == correspondentId && !x.IsArchived, cancellationToken)
            ?? throw new InvalidOperationException("نمایندگی فعال پیدا نشد.");

        var latest = await context.PaymentLocationCorrespondentAssignments
            .Where(x => x.PaymentLocationId == paymentLocationId)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null)
        {
            if (date <= latest.EffectiveFrom)
                throw new InvalidOperationException("برای این محل، مسئولیت از این تاریخ یا تاریخ بعدی ثبت شده است.");
            if (latest.CorrespondentId == correspondentId)
                throw new InvalidOperationException("این نمایندگی هم‌اکنون مسئول محل پرداخت است.");
            latest.EffectiveTo = date;
        }

        var assignment = new PaymentLocationCorrespondentAssignment
        {
            PaymentLocationId = paymentLocationId,
            CorrespondentId = correspondentId,
            EffectiveFrom = date,
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        context.PaymentLocationCorrespondentAssignments.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PaymentLocationAssignmentDto
        {
            Id = assignment.Id,
            PaymentLocationId = paymentLocationId,
            PaymentLocationName = location.Name,
            CorrespondentId = correspondentId,
            CorrespondentName = correspondent.Name,
            EffectiveFrom = date
        };
    }
}
