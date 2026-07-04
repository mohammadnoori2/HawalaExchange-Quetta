using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IHawalaService
    {
        Task<PaginatedResult<HawalaDto>> GetHawalasAsync(HawalaFilterDto filter);
        Task<HawalaDto> GetHawalaByIdAsync(long id);
        Task<HawalaDto> CreateHawalaAsync(CreateHawalaDto createDto);
        Task<HawalaDto> UpdateHawalaAsync(long id, UpdateHawalaDto updateDto);
        Task<HawalaDto> MarkAsPaidAsync(long id, long userId);
        Task<HawalaDto> CancelHawalaAsync(long id, string cancelReason, long userId);
        Task<HawalaStatisticsDto> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
        Task<IEnumerable<string>> GetDistinctHawalaTypesAsync();
        Task<int> DeleteHawalaAsync(long id);
    }
}