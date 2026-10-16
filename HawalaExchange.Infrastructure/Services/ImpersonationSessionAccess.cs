using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public static class ImpersonationSessionAccess
{
    public static async Task<bool> IsValidAsync(ApplicationDbContext db, Guid id, long actorId, long targetId, long tenantId, CancellationToken ct = default)
    {
        var owner = PlatformRoles.Owner; var super = PlatformRoles.SuperAdmin;
        return await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM dbo.ImpersonationSessions s
            JOIN dbo.Users a ON a.Id=s.ActorUserId JOIN dbo.Users u ON u.Id=s.TargetUserId
            JOIN dbo.Tenants t ON t.Id=s.TenantId
            WHERE s.Id={id} AND s.ActorUserId={actorId} AND s.TargetUserId={targetId} AND s.TenantId={tenantId}
              AND s.EndedAt IS NULL AND s.ExpiresAt>SYSUTCDATETIME()
              AND a.IsActive=1 AND a.IsPlatformUser=1 AND a.SecurityStamp=s.ActorStamp
              AND u.IsActive=1 AND u.IsPlatformUser=0 AND u.TenantId=s.TenantId AND u.SecurityStamp=s.TargetStamp
              AND t.IsActive=1 AND t.IsArchived=0
              AND EXISTS(SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId
                         WHERE ur.UserId=a.Id AND r.Name IN ({owner},{super}))
            """).SingleAsync(ct).ConfigureAwait(false) == 1;
    }
}
