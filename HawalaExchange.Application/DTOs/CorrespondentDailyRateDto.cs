namespace HawalaExchange.Application.DTOs;

public sealed class CorrespondentDailyRateDto
{
    public long CorrespondentId { get; set; }
    public DateTime RateDate { get; set; }
    public decimal? UsdToAfnRate { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
