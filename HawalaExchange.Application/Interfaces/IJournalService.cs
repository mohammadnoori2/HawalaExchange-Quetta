using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IJournalService
{
    Task<DailyJournalDto> GetDailyJournalAsync(DateTime journalDate);

    Task<DailyJournalDto> GetJournalAsync(DateTime fromDate, DateTime toDate);

    Task<IReadOnlyList<JournalOperationDto>> GetAccountOperationsAsync(long accountId);

    Task<AccountOperationsPageDto> GetAccountOperationsPageAsync(
        AccountOperationsFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<JournalOperationDto?> GetAccountOperationDetailsAsync(
        long accountId,
        string operationKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashDailyBalanceDto>> GetCashDailyBalancesAsync(DateTime journalDate);

    Task<IReadOnlyList<CurrentCashBalanceDto>> GetCurrentCashBalancesAsync();

    Task SaveCashOpeningBalancesAsync(
        DateTime journalDate,
        IReadOnlyCollection<UpdateCashOpeningBalanceDto> balances);

    Task CloseCashDayAsync(DateTime journalDate);

    Task<DailyCommissionRateDto> GetDailyCommissionRateAsync(DateTime journalDate);

    Task<DailyCommissionRateDto> SaveDailyCommissionRateAsync(
        DateTime journalDate,
        decimal usdToAfnRate);
}
