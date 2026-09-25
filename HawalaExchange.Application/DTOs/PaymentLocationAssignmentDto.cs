namespace HawalaExchange.Application.DTOs;

public sealed class PaymentLocationAssignmentDto
{
    public long Id { get; set; }
    public long PaymentLocationId { get; set; }
    public string PaymentLocationName { get; set; } = string.Empty;
    public long CorrespondentId { get; set; }
    public string CorrespondentName { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}
