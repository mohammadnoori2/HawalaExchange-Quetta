namespace HawalaExchange.Application.Interfaces;

public interface ICurrentTenant
{
    long TenantId { get; }
    long UserId { get; }
    bool HasTenant { get; }
    Guid? ImpersonationSessionId => null;
    long? OriginalActorUserId => null;
    string? SessionSecurityStamp => null;
}
