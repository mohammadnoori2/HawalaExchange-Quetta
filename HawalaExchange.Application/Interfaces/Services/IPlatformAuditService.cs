using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IPlatformAuditService
{
    Task<PlatformAuditPageDto> GetAsync(PlatformAuditFilterDto filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActionsAsync(CancellationToken cancellationToken = default);
}
