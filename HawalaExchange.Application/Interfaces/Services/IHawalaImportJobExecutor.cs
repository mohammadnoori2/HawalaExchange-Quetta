using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

/// <summary>Worker-only posting boundary, resolved after the saved tenant/user identity is set.</summary>
public interface IHawalaImportJobExecutor
{
    Task<HawalaImportResultDto> ConfirmQueuedAsync(
        ConfirmHawalaImportDto request,
        IProgress<HawalaImportProgressDto>? progress,
        CancellationToken cancellationToken);
}
