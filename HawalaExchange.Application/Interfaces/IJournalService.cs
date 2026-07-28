using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IJournalService
{
    Task<DailyJournalDto> GetDailyJournalAsync(DateTime journalDate);

    Task<DailyJournalDto> GetJournalAsync(DateTime fromDate, DateTime toDate);
}
