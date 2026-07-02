using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IAccountService : IBaseService<Account, AccountDto, CreateAccountDto, UpdateAccountDto>
    {
        Task<AccountDto?> GetByAccountCodeAsync(string accountCode);
        Task<IEnumerable<AccountDto>> GetByAccountTypeAsync(string accountType);
        Task<IEnumerable<AccountDto>> GetByReferenceAsync(string referenceType, long referenceId);
        Task<IEnumerable<AccountDto>> GetActiveAccountsAsync();
        Task<AccountDto> ArchiveAsync(long id);
        Task<AccountDto> UnarchiveAsync(long id);
        Task<decimal> GetAccountBalanceAsync(long accountId, long currencyId);
        Task<IEnumerable<BalanceDto>> GetAllAccountBalancesAsync(long accountId);
        Task<string> GetNextAccountCodeAsync(string accountType);
    }
}