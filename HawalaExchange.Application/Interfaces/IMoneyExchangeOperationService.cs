using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IMoneyExchangeOperationService
{
    Task<IEnumerable<MoneyExchangeOperationDto>> GetAllAsync();

    Task<MoneyExchangeOperationDto?> GetByIdAsync(long id);

    Task<MoneyExchangeOperationDto> CreateAsync(CreateMoneyExchangeOperationDto dto);

    Task<MoneyExchangeOperationDto> UpdateAsync(long id, UpdateMoneyExchangeOperationDto dto);

    Task DeleteAsync(long id);
}