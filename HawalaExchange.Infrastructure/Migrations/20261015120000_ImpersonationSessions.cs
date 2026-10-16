using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015120000_ImpersonationSessions")]
public sealed class ImpersonationSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE dbo.ImpersonationSessions(
            Id uniqueidentifier NOT NULL PRIMARY KEY, ActorUserId bigint NOT NULL,
            TargetUserId bigint NOT NULL, TenantId bigint NOT NULL,
            ActorStamp nvarchar(256) NOT NULL, TargetStamp nvarchar(256) NOT NULL,
            Reason nvarchar(500) NOT NULL, StartedAt datetime2 NOT NULL,
            ExpiresAt datetime2 NOT NULL, EndedAt datetime2 NULL);
        CREATE INDEX IX_ImpersonationSessions_Actor ON dbo.ImpersonationSessions(ActorUserId,StartedAt);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a verified backup to downgrade impersonation sessions.");
}
