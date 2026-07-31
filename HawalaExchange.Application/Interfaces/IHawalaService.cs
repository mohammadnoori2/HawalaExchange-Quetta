using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IHawalaService
    {
        Task<HawalaDto> CreateHawalaAsync(CreateHawalaDto dto);
        Task<HawalaDto> UpdateHawalaAsync(long id, UpdateHawalaDto dto);
        Task DeleteHawalaAsync(long id);
        Task<HawalaDto?> GetHawalaByIdAsync(long id);
        Task<HawalaListResultDto> GetHawalasAsync(HawalaFilterDto filter);
        Task<HawalaStatisticsDto> GetStatisticsAsync();
        Task<CorrespondentHawalaRangeResultDto> GetCorrespondentRangeAsync(CorrespondentHawalaRangeFilterDto filter);
       
        Task<HawalaDto> MarkAsPaidAsync(long id, long paidFromAccountId);
        Task<HawalaDto> MarkAsPaidAsync(long id, PayHawalaDto payment);
        Task<HawalaDto> CancelHawalaAsync(long id, CancelHawalaDto cancellation);
        Task<long> GetNextNumberAsync(long correspondentId, string hawalaType);
    }
}
