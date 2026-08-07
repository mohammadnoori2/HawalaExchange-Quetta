using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ISubscriptionAccessService
{
    Task<SubscriptionAccessDto> GetAccessAsync(long tenantId, CancellationToken cancellationToken = default);
    Task EnsureCanSignInAsync(long tenantId, CancellationToken cancellationToken = default);
    Task EnsureCanWriteAsync(long tenantId, CancellationToken cancellationToken = default);
    Task EnsureUserCapacityAsync(long tenantId, CancellationToken cancellationToken = default);
    Task EnsureBranchCapacityAsync(long tenantId, CancellationToken cancellationToken = default);
    Task EnsureMonthlyTransactionCapacityAsync(long tenantId, CancellationToken cancellationToken = default);
    Task EnsureDocumentCapacityAsync(long tenantId, long incomingBytes, CancellationToken cancellationToken = default);
}
