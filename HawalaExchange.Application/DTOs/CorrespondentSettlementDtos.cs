using System.ComponentModel.DataAnnotations;

namespace HawalaExchange.Application.DTOs;

public sealed class SettlementRateDto
{
    public long SourceCurrencyId { get; set; }
    [Range(typeof(decimal), "0.00000001", "999999999999")] public decimal Rate { get; set; }
}

public sealed class HawalaSettlementRateDto
{
    public long HawalaId { get; set; }
    public long SourceCurrencyId { get; set; }
    [Range(typeof(decimal), "0.00000001", "999999999999")] public decimal Rate { get; set; }
}

public sealed class ConvertHawalasToSettlementDto
{
    public bool ConfirmDifferentDailyRate { get; set; }
    public long CorrespondentId { get; set; }
    [MinLength(1)] public List<long> HawalaIds { get; set; } = [];
    [MinLength(1)] public List<HawalaSettlementRateDto> HawalaRates { get; set; } = [];
    [StringLength(500)] public string? Note { get; set; }
}

public sealed class UpdateHawalaSettlementRateDto
{
    public long HawalaId { get; set; }
    public long SourceCurrencyId { get; set; }
    [Range(typeof(decimal), "0.00000001", "999999999999")] public decimal Rate { get; set; }
}

public sealed class CorrespondentSettlementHawalaBalanceDto : CorrespondentSettlementBalanceDto
{
    public long HawalaId { get; set; }
    public long HawalaNumber { get; set; }
    public DateTime RateDate { get; set; }
}

public sealed class HawalaSettlementRateResultDto
{
    public long HawalaId { get; set; }
    public long HawalaNumber { get; set; }
    public decimal PreviousRate { get; set; }
    public decimal ExchangeRate { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal SourceAmount { get; set; }
    public string TargetCurrencyCode { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
}

public sealed class ConvertCorrespondentBalanceDto
{
    public bool ConfirmDifferentDailyRate { get; set; }
    public long CorrespondentId { get; set; }
    public DateTime RateDate { get; set; } = DateTime.Today;
    [MinLength(1)] public List<SettlementRateDto> Rates { get; set; } = [];
    [StringLength(500)] public string? Note { get; set; }
}

public sealed class CorrespondentSettlementHistoryFilterDto
{
    public long CorrespondentId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public long? CurrencyId { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class CorrespondentSettlementHistoryDto
{
    public long ConversionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UserName { get; set; } = "";
    public string SourceMode { get; set; } = "";
    public string Status { get; set; } = "";
    public string TargetCurrencyCode { get; set; } = "";
    public string? Note { get; set; }
    public int HawalaCount { get; set; }
    public IReadOnlyList<CorrespondentSettlementHistoryItemDto> Items { get; set; } = [];
    public IReadOnlyList<CorrespondentSettlementHistoryRevisionDto> Revisions { get; set; } = [];
}

public sealed class CorrespondentSettlementHistoryItemDto
{
    public long? HawalaId { get; set; }
    public long? HawalaNumber { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string PaymentLocationName { get; set; } = "";
    public DateTime RateDate { get; set; }
    public long SourceCurrencyId { get; set; }
    public string SourceCurrencyCode { get; set; } = "";
    public decimal SourceAmount { get; set; }
    public decimal ExchangeRate { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal? PrincipalTargetAmount { get; set; }
    public decimal? UsdAmount { get; set; }
}

public sealed class CorrespondentSettlementHistoryRevisionDto
{
    public DateTime CreatedAt { get; set; }
    public string UserName { get; set; } = "";
    public IReadOnlyList<CorrespondentSettlementHistoryItemDto> Items { get; set; } = [];
}

public sealed class CorrespondentSettlementDailyRateDto
{
    public DateTime RateDate { get; set; }
    public long SourceCurrencyId { get; set; }
    public long TargetCurrencyId { get; set; }
    public string SourceCurrencyCode { get; set; } = "";
    public string TargetCurrencyCode { get; set; } = "";
    public decimal ExchangeRate { get; set; }
    public DateTime ModifiedAt { get; set; }
}

public class CorrespondentSettlementBalanceDto
{
    public long SourceCurrencyId { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public int SourceQuotationPriority { get; set; }
    public decimal TalabKar { get; set; }
    public decimal BadehKar { get; set; }
    public decimal NetAmount => TalabKar - BadehKar;
}

public sealed class CorrespondentSettlementPreviewDto
{
    public long CorrespondentId { get; set; }
    public string CorrespondentName { get; set; } = string.Empty;
    public long TargetCurrencyId { get; set; }
    public string TargetCurrencyCode { get; set; } = string.Empty;
    public int TargetQuotationPriority { get; set; }
    public int TargetDecimalPlaces { get; set; }
    public IReadOnlyList<CorrespondentSettlementBalanceDto> Balances { get; set; } = [];
    public int PendingHawalaCount { get; set; }
}

public sealed class CorrespondentSettlementResultDto
{
    public long ConversionId { get; set; }
    public long TransactionId { get; set; }
    public int HawalaCount { get; set; }
    public int CurrencyCount { get; set; }
    public IReadOnlyList<CorrespondentSettlementResultItemDto> Items { get; set; } = [];
}

public sealed class CorrespondentSettlementResultItemDto
{
    public long? HawalaId { get; set; }
    public long? HawalaNumber { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal SourceAmount { get; set; }
    public string BalanceDirection { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public string TargetCurrencyCode { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
}
