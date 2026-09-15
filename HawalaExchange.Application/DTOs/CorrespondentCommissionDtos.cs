using System.ComponentModel.DataAnnotations;

namespace HawalaExchange.Application.DTOs;

public sealed class CorrespondentCommissionRateDto
{
    public long CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal SourceToAfnRate { get; set; }
}

public sealed class CorrespondentCommissionPreviewRequestDto
{
    public long CorrespondentId { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal CommissionPerLakhAfn { get; set; } = 200;
    [Range(typeof(decimal), "0.00000001", "999999999999")] public decimal UsdToAfnRate { get; set; }
    public List<CorrespondentCommissionRateDto> Rates { get; set; } = [];
}

public sealed class CorrespondentCommissionPreviewDto
{
    public long CorrespondentId { get; set; }
    public string CorrespondentName { get; set; } = string.Empty;
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public int HawalaCount { get; set; }
    public decimal TotalBaseAfn { get; set; }
    public decimal TotalCommissionAfn { get; set; }
    public decimal TotalCommissionUsd { get; set; }
    public List<CorrespondentCommissionRateDto> Rates { get; set; } = [];
    public List<CorrespondentCommissionItemDto> Items { get; set; } = [];
}

public sealed class CorrespondentCommissionItemDto
{
    public long HawalaId { get; set; }
    public long HawalaNumber { get; set; }
    public DateTime HawalaDate { get; set; }
    public long CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal SourceAmount { get; set; }
    public decimal SourceToAfnRate { get; set; }
    public decimal AfnEquivalent { get; set; }
    public decimal CommissionAfn { get; set; }
}

public sealed class CorrespondentCommissionBatchDto
{
    public long Id { get; set; }
    public long CorrespondentId { get; set; }
    public string CorrespondentName { get; set; } = string.Empty;
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public int HawalaCount { get; set; }
    public decimal CommissionPerLakhAfn { get; set; }
    public decimal UsdToAfnRate { get; set; }
    public decimal TotalBaseAfn { get; set; }
    public decimal TotalCommissionAfn { get; set; }
    public decimal TotalCommissionUsd { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
