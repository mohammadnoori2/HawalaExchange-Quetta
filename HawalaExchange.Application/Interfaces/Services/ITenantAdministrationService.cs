using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ITenantAdministrationService
{
    Task<IReadOnlyList<TenantDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TenantDto> CreateAsync(CreateTenantDto dto, CancellationToken cancellationToken = default);
    Task<TenantDto> UpdateAsync(long id, UpdateTenantDto dto, CancellationToken cancellationToken = default);
    Task ArchiveAsync(long id, CancellationToken cancellationToken = default);
    Task UnarchiveAsync(long id, CancellationToken cancellationToken = default);
}
