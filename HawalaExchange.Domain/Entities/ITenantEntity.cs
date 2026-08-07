namespace HawalaExchange.Domain.Entities;

/// <summary>Marks rows that belong to exactly one exchange tenant.</summary>
public interface ITenantEntity
{
    long TenantId { get; set; }
}
