namespace HawalaExchange.Application.DTOs;

public sealed class CorrespondentDailyRateDto
{
    public long CorrespondentId { get; set; }
    public DateTime RateDate { get; set; }
    public decimal? UsdToAfnRate { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

public sealed class CorrespondentDailyRateImpactDto
{
    public long CorrespondentId { get; set; }
    public DateTime RateDate { get; set; }
    public decimal NewUsdToAfnRate { get; set; }
    public decimal? PreviousUsdToAfnRate { get; set; }
    public List<CorrespondentRateAffectedHawalaDto> Hawalas { get; set; } = [];
}

public sealed class CorrespondentRateAffectedHawalaDto
{
    public long ItemId { get; set; }
    public long HawalaNumber { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public string TargetCurrencyCode { get; set; } = string.Empty;
    public decimal PreviousRate { get; set; }
    public decimal NewRate { get; set; }
    public decimal PreviousTargetAmount { get; set; }
    public decimal NewTargetAmount { get; set; }
    public bool IsClosed { get; set; }
}
