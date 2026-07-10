using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IAccountMoneyOperationService
{
    Task<IEnumerable<AccountMoneyOperationDto>> GetAllAsync();

    Task<AccountMoneyOperationDto?> GetByIdAsync(long id);

    Task<AccountMoneyOperationDto> CreateAsync(CreateAccountMoneyOperationDto dto);

    Task<AccountMoneyOperationDto> UpdateAsync(long id, UpdateAccountMoneyOperationDto dto);

    Task DeleteAsync(long id);
}