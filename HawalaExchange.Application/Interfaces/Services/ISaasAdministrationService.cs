using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ISaasAdministrationService
{
    Task<SaasDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaasTenantDto>> GetTenantsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanDto> CreatePlanAsync(SaveSubscriptionPlanDto dto, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanDto> UpdatePlanAsync(long id, SaveSubscriptionPlanDto dto, CancellationToken cancellationToken = default);
    Task ChangeSubscriptionAsync(long tenantId, ChangeSubscriptionDto dto, CancellationToken cancellationToken = default);
    Task RecordPaymentAsync(RecordSubscriptionPaymentDto dto, CancellationToken cancellationToken = default);
    Task RefreshSubscriptionStatusesAsync(CancellationToken cancellationToken = default);
}
