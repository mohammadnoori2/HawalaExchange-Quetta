using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IExpenseService
    {
        Task<ExpenseDto> CreateExpenseAsync(CreateExpenseDto createDto);
        Task<IEnumerable<ExpenseDto>> GetExpensesByTransactionAsync(long transactionId);
        Task<IEnumerable<ExpenseDto>> GetExpensesByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<IEnumerable<ExpenseDto>> GetExpensesByCurrencyAsync(long currencyId);
        Task<ExpenseDto?> GetExpenseByIdAsync(long id);
        Task<ExpenseDto> UpdateExpenseAsync(long id, CreateExpenseDto updateDto);
        Task DeleteExpenseAsync(long id);
        Task<decimal> GetTotalExpensesByCurrencyAsync(long currencyId, DateTime? fromDate = null, DateTime? toDate = null);
        Task<IEnumerable<ExpenseDto>> GetExpensesByBranchAsync(long branchId);
    }
}