using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IJournalService
{
    Task<DailyJournalDto> GetDailyJournalAsync(DateTime journalDate);

    Task<DailyJournalDto> GetJournalAsync(DateTime fromDate, DateTime toDate);

    Task<IReadOnlyList<JournalOperationDto>> GetAccountOperationsAsync(long accountId);

    Task<IReadOnlyList<CashDailyBalanceDto>> GetCashDailyBalancesAsync(DateTime journalDate);

    Task<IReadOnlyList<CurrentCashBalanceDto>> GetCurrentCashBalancesAsync();

    Task SaveCashOpeningBalancesAsync(
        DateTime journalDate,
        IReadOnlyCollection<UpdateCashOpeningBalanceDto> balances);

    Task CloseCashDayAsync(DateTime journalDate);
}
