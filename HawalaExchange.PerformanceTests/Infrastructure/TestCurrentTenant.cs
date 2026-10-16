using HawalaExchange.Application.Interfaces;

namespace HawalaExchange.PerformanceTests.Infrastructure;

internal sealed class TestCurrentTenant : ICurrentTenant
{
    public string? SessionSecurityStamp {get;set;}
    public long TenantId { get; set; } = 1;
    public long UserId { get; set; }
    public bool HasTenant => TenantId > 0;
    public Guid? ImpersonationSessionId { get; set; }
    public long? OriginalActorUserId { get; set; }
}
