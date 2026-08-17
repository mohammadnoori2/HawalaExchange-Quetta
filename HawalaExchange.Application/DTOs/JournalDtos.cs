namespace HawalaExchange.Application.DTOs;

public class DailyJournalDto
{
    public DateTime JournalDate { get; set; }

    public DateTime FromDate { get; set; }

    public DateTime ToDate { get; set; }

    /// <summary>یک ردیف برای هر عملیات تجارتی، صرف‌نظر از تعداد ثبت‌های حسابداری آن.</summary>
    public List<JournalOperationDto> Operations { get; set; } = new();

    /// <summary>تمام ردیف‌های خام حسابداری؛ برای جزئیات و سازگاری با بخش‌های قبلی.</summary>
    public List<JournalEntryDto> Entries { get; set; } = new();

    public List<DailyJournalCurrencySummaryDto> CurrencySummaries { get; set; } = new();
}

public class CashDailyBalanceDto
{
    public long Id { get; set; }

    public DateTime JournalDate { get; set; }

    public long AccountId { get; set; }

    public string AccountCode { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public int DecimalPlaces { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal DailyMovement { get; set; }

    public decimal CurrentBalance { get; set; }

    public decimal? ClosingBalance { get; set; }

    public bool IsClosed { get; set; }
}

public class UpdateCashOpeningBalanceDto
{
    public long AccountId { get; set; }

    public long CurrencyId { get; set; }

    public decimal OpeningBalance { get; set; }
}

public class CurrentCashBalanceDto
{
    public long AccountId { get; set; }

    public string AccountCode { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Balance { get; set; }
}

public class JournalOperationDto
{
    public string OperationKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public long? SourceId { get; set; }

    public string DocumentNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string SummarySentence { get; set; } = string.Empty;

    public List<string> AccountNames { get; set; } = new();

    public List<JournalOperationCurrencySummaryDto> CurrencySummaries { get; set; } = new();

    public List<JournalOperationFieldDto> SourceDetails { get; set; } = new();

    public List<JournalEntryDto> LedgerEntries { get; set; } = new();

    public int LedgerEntriesCount => LedgerEntries.Count;
}

public class JournalOperationCurrencySummaryDto
{
    public long CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal TotalTalabKar { get; set; }

    public decimal TotalBadehKar { get; set; }
}

public class JournalOperationFieldDto
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
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

    /// <summary>مشخص می‌کند که ثبت حسابداری مستقیماً مربوط به یک حساب صندوق است.</summary>
    public bool IsCashAccount { get; set; }

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
