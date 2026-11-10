using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface ICustomerService : IBaseService<Customer, CustomerDto, CreateCustomerDto, UpdateCustomerDto>
    {
        Task<CustomerDto?> GetByCustomerCodeAsync(string customerCode);
        Task<IEnumerable<CustomerDto>> SearchAsync(string searchTerm);
        Task<IEnumerable<CustomerDto>> GetArchivedAsync();
        Task<CustomerDto> ArchiveAsync(long id);
        Task<CustomerDto> UnarchiveAsync(long id);
        Task<IReadOnlyList<CustomerOpeningBalanceDto>> GetOpeningBalancesAsync(long id);
        Task AddOpeningBalanceAsync(long id, CustomerOpeningBalanceRequestDto request);
        Task UpdateOpeningBalanceAsync(long id, long openingId, CustomerOpeningBalanceRequestDto request);
        Task DeleteOpeningBalanceAsync(long id, long openingId, string? reason = null);

    }
}
