using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IExpenseService
    {
        Task<IEnumerable<ExpenseDto>> GetAllAsync();

        Task<ExpenseDto?> GetByIdAsync(long id);

        Task<ExpenseDto> CreateExpenseAsync(CreateExpenseDto createDto);

        Task<ExpenseDto> UpdateExpenseAsync(long id, UpdateExpenseDto updateDto);

        Task DeleteExpenseAsync(long id);

        Task<IEnumerable<ExpenseDto>> GetExpensesByDateRangeAsync(DateTime fromDate, DateTime toDate);

        Task<IEnumerable<ExpenseDto>> GetExpensesByCurrencyAsync(long currencyId);

        Task<decimal> GetTotalExpensesByCurrencyAsync(
            long currencyId,
            DateTime? fromDate = null,
            DateTime? toDate = null);
    }
}