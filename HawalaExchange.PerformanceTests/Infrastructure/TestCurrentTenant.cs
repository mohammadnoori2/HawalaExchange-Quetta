using HawalaExchange.Application.Interfaces;

namespace HawalaExchange.PerformanceTests.Infrastructure;

internal sealed class TestCurrentTenant : ICurrentTenant
{
    public long TenantId { get; set; } = 1;
    public long UserId { get; set; }
    public bool HasTenant => TenantId > 0;
}
