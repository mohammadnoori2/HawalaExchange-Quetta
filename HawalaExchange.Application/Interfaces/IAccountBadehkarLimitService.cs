using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IAccountBadehkarLimitService : IBaseService<AccountBadehkarLimit, AccountBadehkarLimitDto, CreateAccountBadehkarLimitDto, UpdateAccountBadehkarLimitDto>
    {
        Task<AccountBadehkarLimitDto?> GetByAccountAndCurrencyAsync(long accountId, long currencyId);
        Task<IEnumerable<AccountBadehkarLimitDto>> GetByAccountAsync(long accountId);
        Task<IEnumerable<AccountBadehkarLimitDto>> GetByCurrencyAsync(long currencyId);
        Task<IEnumerable<AccountBadehkarLimitDto>> GetActiveLimitsAsync();
        Task<AccountBadehkarLimitDto> ActivateAsync(long id);
        Task<AccountBadehkarLimitDto> DeactivateAsync(long id);
    }
}