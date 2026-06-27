using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IBalanceService
    {
        Task<IEnumerable<CustomerBalanceDto>> GetAllCustomerBalancesAsync();
        Task<CustomerBalanceDto?> GetCustomerBalanceAsync(long customerId);
        Task<IEnumerable<CorrespondentBalanceDto>> GetAllCorrespondentBalancesAsync();
        Task<CorrespondentBalanceDto?> GetCorrespondentBalanceAsync(long correspondentId);
        Task<IEnumerable<CashBalanceDto>> GetAllCashBalancesAsync(long branchId);
        Task<CashBalanceDto?> GetCashBalanceAsync(long accountId);
        Task<BranchBalanceDto?> GetBranchBalanceAsync(long branchId);
        Task<IEnumerable<BalanceDto>> GetAccountBalanceAsync(long accountId);
        Task<bool> ValidateBadehkarLimitAsync(long accountId, long currencyId, decimal amount);
        Task<IEnumerable<AccountBalanceDto>> GetAccountsWithLimitsAsync();
    }
}