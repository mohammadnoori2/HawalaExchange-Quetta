namespace HawalaExchange.Application.Interfaces;

public interface ICurrentTenant
{
    long TenantId { get; }
    long UserId { get; }
    bool HasTenant { get; }
}
