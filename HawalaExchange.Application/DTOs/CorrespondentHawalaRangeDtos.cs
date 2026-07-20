namespace HawalaExchange.Application.DTOs;

public sealed class CorrespondentHawalaRangeFilterDto
{
    public long CorrespondentId { get; set; }
    public string HawalaType { get; set; } = "HawalaSend";
    public string RangeType { get; set; } = "Number";
    public long? StartNumber { get; set; }
    public long? EndNumber { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public sealed class CorrespondentHawalaRangeResultDto
{
    public List<HawalaDto> Hawalas { get; set; } = new();
    public List<CorrespondentHawalaRangeSummaryDto> Summaries { get; set; } = new();
}

public sealed class CorrespondentHawalaRangeSummaryDto
{
    public long CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal Balance => TotalDebit - TotalCredit;
}
