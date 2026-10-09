using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IHawalaImportQueue
{
    Task EnqueueAsync(ConfirmHawalaImportDto request, CancellationToken cancellationToken = default);
    Task RetryAsync(long batchId, CancellationToken cancellationToken = default);
}
