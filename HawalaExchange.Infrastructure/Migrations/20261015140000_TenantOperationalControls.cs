using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015140000_TenantOperationalControls")]
public sealed class TenantOperationalControls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE dbo.TenantOperationalControls(
            TenantId bigint NOT NULL CONSTRAINT PK_TenantOperationalControls PRIMARY KEY,
            ReadOnly bit NOT NULL CONSTRAINT DF_TenantControls_ReadOnly DEFAULT(0),
            MaxUsers int NULL, DisabledFeaturesJson nvarchar(2000) NOT NULL DEFAULT(N'[]'),
            TimeZoneId nvarchar(100) NOT NULL DEFAULT(N'Asia/Kabul'),
            ModifiedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
            CONSTRAINT FK_TenantControls_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
            CONSTRAINT CK_TenantControls_MaxUsers CHECK(MaxUsers IS NULL OR MaxUsers>0));
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("TenantOperationalControls");
}
