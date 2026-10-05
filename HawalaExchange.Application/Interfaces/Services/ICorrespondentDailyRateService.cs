using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ICorrespondentDailyRateService
{
    Task<CorrespondentDailyRateDto> GetAsync(long correspondentId, DateTime date,
        CancellationToken cancellationToken = default);
    Task<CorrespondentDailyRateDto> SaveAsync(long correspondentId, DateTime date,
        decimal usdToAfnRate, CancellationToken cancellationToken = default);
    Task<CorrespondentDailyRateImpactDto> GetImpactAsync(long correspondentId, DateTime date,
        decimal usdToAfnRate, CancellationToken cancellationToken = default);
    Task<CorrespondentDailyRateDto> SaveConfirmedAsync(CorrespondentDailyRateImpactDto confirmation,
        bool applyToHawalas, CancellationToken cancellationToken = default);
}
