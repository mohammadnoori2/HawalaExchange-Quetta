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
    public long CorrespondentId { get; set; }
    [MinLength(1)] public List<SettlementRateDto> Rates { get; set; } = [];
    [StringLength(500)] public string? Note { get; set; }
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
