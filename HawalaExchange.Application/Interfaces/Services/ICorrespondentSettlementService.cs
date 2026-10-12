using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ICorrespondentSettlementService
{
    Task<CorrespondentSettlementPreviewDto> GetPreviewAsync(long correspondentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrespondentSettlementHawalaBalanceDto>> GetHawalaPreviewAsync(long correspondentId, IReadOnlyCollection<long> hawalaIds, CancellationToken cancellationToken = default);
    Task<CorrespondentSettlementResultDto> ConvertHawalasAsync(ConvertHawalasToSettlementDto dto, CancellationToken cancellationToken = default);
    Task<CorrespondentSettlementResultDto> ConvertBalanceAsync(ConvertCorrespondentBalanceDto dto, CancellationToken cancellationToken = default);
    Task<HawalaSettlementRateResultDto> UpdateHawalaRateAsync(UpdateHawalaSettlementRateDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrespondentSettlementDailyRateDto>> GetRatesAsync(long correspondentId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrespondentSettlementHistoryDto>> GetHistoryAsync(CorrespondentSettlementHistoryFilterDto filter, CancellationToken cancellationToken = default);
    Task<CorrespondentSettlementHistoryDto> GetHistoryDetailsAsync(long correspondentId, long conversionId, CancellationToken cancellationToken = default);
    Task ReverseAsync(long correspondentId, long conversionId, string reason, CancellationToken cancellationToken = default);
}
