using System.ComponentModel.DataAnnotations;

namespace HawalaExchange.Application.DTOs;

public sealed class CreateAedDealDto
{
    [Required, StringLength(50)] public string DealNumber { get; set; } = string.Empty;
    public long SourceCorrespondentId { get; set; }
    public long DubaiCorrespondentId { get; set; }
    public long SourceCurrencyId { get; set; }
    [Range(typeof(decimal), "0.0001", "99999999999999")] public decimal Amount { get; set; }
    [Range(0, 4)] public int RoundingDecimalPlaces { get; set; }
    [StringLength(500)] public string? Note { get; set; }
}

public sealed class PreviewAedConversionDto
{
    public long DealId { get; set; }
    [Range(typeof(decimal), "0.0001", "99999999999999")] public decimal SourceAmount { get; set; }
    [Range(typeof(decimal), "-999999999", "999999999")] public decimal ActualMarker { get; set; }
    [Range(typeof(decimal), "-999999999", "999999999")] public decimal DeclaredMarker { get; set; }
    [StringLength(500)] public string? Note { get; set; }
}

public sealed class AedConversionPreviewDto
{
    public long DealId { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal SourceAmount { get; set; }
    public decimal RemainingAmountBefore { get; set; }
    public decimal AedPerUsdRate { get; set; }
    public decimal ActualMarker { get; set; }
    public decimal DeclaredMarker { get; set; }
    public decimal FinalUsdAmount { get; set; }
    public decimal ProfitUsd { get; set; }
    public int RoundingDecimalPlaces { get; set; }
}

public sealed class AedDealDto
{
    public long Id { get; set; }
    public string DealNumber { get; set; } = string.Empty;
    public long SourceCorrespondentId { get; set; }
    public string SourceCorrespondentName { get; set; } = string.Empty;
    public long DubaiCorrespondentId { get; set; }
    public string DubaiCorrespondentName { get; set; } = string.Empty;
    public long SourceCurrencyId { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal ConvertedAmount { get; set; }
    public decimal RemainingAmount => OriginalAmount - ConvertedAmount;
    public decimal TotalFinalUsd { get; set; }
    public decimal TotalProfitUsd { get; set; }
    public decimal AedPerUsdRate { get; set; }
    public int RoundingDecimalPlaces { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<AedDealConversionDto> Conversions { get; set; } = [];
}

public sealed class AedDealConversionDto
{
    public long Id { get; set; }
    public decimal SourceAmount { get; set; }
    public decimal AedPerUsdRate { get; set; }
    public decimal ActualMarker { get; set; }
    public decimal DeclaredMarker { get; set; }
    public decimal FinalUsdAmount { get; set; }
    public decimal ProfitUsd { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
