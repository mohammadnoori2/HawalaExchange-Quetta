using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IPlatformUserService
{
    Task<IReadOnlyList<PlatformUserDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PlatformUserDto> CreateAsync(CreatePlatformUserDto dto, CancellationToken cancellationToken = default);
    Task SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken = default);
}
