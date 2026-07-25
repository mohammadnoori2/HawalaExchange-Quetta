using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ICurrencyCostService
{
    Task RebuildAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CurrencyCostPositionDto>> GetPositionsAsync(CancellationToken cancellationToken = default);
}
