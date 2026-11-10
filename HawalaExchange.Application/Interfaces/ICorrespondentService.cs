using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface ICorrespondentService : IBaseService<Correspondent, CorrespondentDto, CreateCorrespondentDto, UpdateCorrespondentDto>
    {
        Task<IReadOnlyList<CorrespondentOpeningBalanceDto>> GetOpeningBalancesAsync(long id);
        Task AddOpeningBalanceAsync(long id, CorrespondentOpeningBalanceRequestDto request);
        Task UpdateOpeningBalanceAsync(long id, long openingId, CorrespondentOpeningBalanceRequestDto request);
        Task DeleteOpeningBalanceAsync(long id, long openingId, string? reason = null);
        Task<CorrespondentDto?> GetByCodeAsync(string code);
        Task<CorrespondentDetailsPageDto?> GetDetailsPageAsync(long id, CancellationToken cancellationToken = default);
        Task<CorrespondentStatusPageDto?> GetStatusPageAsync(long id, CancellationToken cancellationToken = default);
        Task<IEnumerable<CorrespondentDto>> GetByCountryAsync(string country);
        Task<IEnumerable<CorrespondentDto>> GetActiveAsync();
        Task<CorrespondentDto> ArchiveAsync(long id);
        Task<CorrespondentDto> UnarchiveAsync(long id);
        Task<CorrespondentPeriodClosePreviewDto> GetPeriodClosePreviewAsync(long id);
        Task<CorrespondentPeriodDto> ClosePeriodAsync(long id, CloseCorrespondentPeriodDto dto);
        Task<IReadOnlyList<CorrespondentPeriodDto>> GetPeriodsAsync(long id);
        Task<CorrespondentPeriodDto?> GetPeriodAsync(long periodId);
    }
}
