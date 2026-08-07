using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ISaasBillingService
{
    Task<BillingDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingSubscriptionOptionDto>> GetSubscriptionOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionInvoiceDto>> GetInvoicesAsync(string? search = null, SubscriptionInvoiceStatus? status = null, CancellationToken cancellationToken = default);
    Task<SubscriptionInvoiceDto?> GetInvoiceAsync(long id, CancellationToken cancellationToken = default);
    Task<SubscriptionInvoiceDto> CreateInvoiceAsync(CreateSubscriptionInvoiceDto dto, CancellationToken cancellationToken = default);
    Task RecordPaymentAsync(RecordInvoicePaymentDto dto, CancellationToken cancellationToken = default);
    Task CancelInvoiceAsync(long invoiceId, CancellationToken cancellationToken = default);
    Task RefreshOverdueInvoicesAsync(CancellationToken cancellationToken = default);
}
