namespace HawalaExchange.Application.DTOs;

public class DailyJournalDto
{
    public DateTime JournalDate { get; set; }

    public List<JournalEntryDto> Entries { get; set; } = new();

    public List<DailyJournalCurrencySummaryDto> CurrencySummaries { get; set; } = new();
}

public class JournalEntryDto
{
    public long Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public long? SourceId { get; set; }

    public long AccountId { get; set; }

    public string AccountCode { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal TalabKar { get; set; }

    public decimal BadehKar { get; set; }

    public string? Description { get; set; }
}

public class DailyJournalCurrencySummaryDto
{
    public long CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public int EntriesCount { get; set; }

    public decimal TotalTalabKar { get; set; }

    public decimal TotalBadehKar { get; set; }
}