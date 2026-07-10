
using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces
{
    public interface ICapitalInvestmentService
    {
        Task<List<CapitalInvestmentDto>> GetAllAsync();

        Task<CapitalInvestmentDto?> GetByIdAsync(long id);

        Task<long> CreateAsync(CreateCapitalInvestmentDto dto);

        Task UpdateAsync(long id, UpdateCapitalInvestmentDto dto);

        Task DeleteAsync(long id);
    }
}
