namespace HawalaExchange.Infrastructure.Services;

public sealed class UserPermissionChangeBus
{
    public event Action<long,long>? Changed;
    public void Publish(long tenantId,long userId)
    {
        foreach(var subscriber in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action<long,long>)subscriber)(tenantId,userId); }
            catch { /* A disconnected circuit cannot invalidate an already-committed change. */ }
        }
    }
}
