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

    }
}