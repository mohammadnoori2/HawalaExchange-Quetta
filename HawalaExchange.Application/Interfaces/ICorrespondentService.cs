using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface ICorrespondentService : IBaseService<Correspondent, CorrespondentDto, CreateCorrespondentDto, UpdateCorrespondentDto>
    {
        Task<CorrespondentDto?> GetByCodeAsync(string code);
        Task<CorrespondentDetailsPageDto?> GetDetailsPageAsync(long id, CancellationToken cancellationToken = default);
        Task<CorrespondentStatusPageDto?> GetStatusPageAsync(long id, CancellationToken cancellationToken = default);
        Task<IEnumerable<CorrespondentDto>> GetByCountryAsync(string country);
        Task<IEnumerable<CorrespondentDto>> GetActiveAsync();
        Task<CorrespondentDto> ArchiveAsync(long id);
        Task<CorrespondentDto> UnarchiveAsync(long id);
    }
}
