using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IPaymentLocationAssignmentService
{
    Task<IReadOnlyList<PaymentLocationAssignmentDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PaymentLocationAssignmentDto> AssignAsync(
        long paymentLocationId,
        long correspondentId,
        DateTime effectiveFrom,
        CancellationToken cancellationToken = default);
}
