using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

internal static class CorrespondentCommissionPeriodLock
{
    internal static Task AcquireAsync(ApplicationDbContext context, long correspondentId, CancellationToken cancellationToken = default)
    {
        var resource = $"CorrespondentCommissionPeriod:{context.CurrentTenantId}:{correspondentId}";
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = N'Exclusive',
                @LockOwner = N'Transaction', @LockTimeout = 5000;
            IF @result < 0 THROW 50028, N'نمایندگی در حال ثبت کمیشن یا بستن دوره است؛ دوباره تلاش کنید.', 1;
            """, cancellationToken);
    }
}
