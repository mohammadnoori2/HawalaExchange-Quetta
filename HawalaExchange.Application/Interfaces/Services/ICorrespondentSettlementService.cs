using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ICorrespondentSettlementService
{
    Task<CorrespondentSettlementPreviewDto> GetPreviewAsync(long correspondentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrespondentSettlementHawalaBalanceDto>> GetHawalaPreviewAsync(long correspondentId, IReadOnlyCollection<long> hawalaIds, CancellationToken cancellationToken = default);
    Task<CorrespondentSettlementResultDto> ConvertHawalasAsync(ConvertHawalasToSettlementDto dto, CancellationToken cancellationToken = default);
    Task<CorrespondentSettlementResultDto> ConvertBalanceAsync(ConvertCorrespondentBalanceDto dto, CancellationToken cancellationToken = default);
    Task<HawalaSettlementRateResultDto> UpdateHawalaRateAsync(UpdateHawalaSettlementRateDto dto, CancellationToken cancellationToken = default);
}
