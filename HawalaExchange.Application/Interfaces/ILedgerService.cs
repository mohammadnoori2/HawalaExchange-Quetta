using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface ILedgerService
    {
        Task<LedgerEntryDto> CreateLedgerEntryAsync(CreateLedgerEntryDto createDto);
        Task<IEnumerable<LedgerEntryDto>> GetEntriesByTransactionAsync(long transactionId);
        Task<IEnumerable<LedgerEntryDto>> GetEntriesByAccountAsync(long accountId, long? currencyId = null);
        Task<IEnumerable<LedgerEntryDto>> GetEntriesByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<decimal> GetAccountBalanceAsync(long accountId, long currencyId);
        Task<IEnumerable<BalanceDto>> GetAccountBalancesAsync(long accountId);
        Task<IEnumerable<LedgerEntryDto>> GetCustomerLedgerAsync(long customerId);
        Task<IEnumerable<LedgerEntryDto>> GetCorrespondentLedgerAsync(long correspondentId);
        Task<IEnumerable<LedgerEntryDto>> GetTrialBalanceAsync(DateTime asOfDate);
    }
}