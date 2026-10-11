using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261011130000_SeparateImportLocationPreparation")]
public sealed class SeparateImportLocationPreparation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("LocationsPreparedAt", "HawalaImportBatches", type: "datetime2", nullable: true);
        migrationBuilder.Sql("""
            CREATE TYPE dbo.HawalaImportLocationTableType_v1 AS TABLE
            (RowId bigint NOT NULL PRIMARY KEY, PaymentLocationId bigint NOT NULL, DestinationCorrespondentId bigint NULL);
            """);
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_PrepareHawalaImportLocations_v1
                @TenantId bigint, @BatchId bigint, @OwnPaymentLocationId bigint,
                @Mappings dbo.HawalaImportLocationTableType_v1 READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;
                IF NOT EXISTS (SELECT 1 FROM dbo.HawalaImportBatches WITH (UPDLOCK, HOLDLOCK)
                               WHERE TenantId=@TenantId AND Id=@BatchId AND Status='Preview')
                    THROW 50001, 'Import preview is not available.', 1;
                IF (SELECT COUNT(*) FROM @Mappings) <> (SELECT COUNT(*) FROM dbo.HawalaImportRows WHERE TenantId=@TenantId AND BatchId=@BatchId)
                    OR EXISTS (SELECT 1 FROM @Mappings m LEFT JOIN dbo.HawalaImportRows r
                               ON r.Id=m.RowId AND r.TenantId=@TenantId AND r.BatchId=@BatchId WHERE r.Id IS NULL)
                    THROW 50002, 'Import preparation row mismatch.', 1;
                UPDATE r SET PaymentLocationId=m.PaymentLocationId, DestinationCorrespondentId=m.DestinationCorrespondentId
                FROM dbo.HawalaImportRows r JOIN @Mappings m ON m.RowId=r.Id
                WHERE r.TenantId=@TenantId AND r.BatchId=@BatchId;
                UPDATE dbo.HawalaImportBatches SET OwnPaymentLocationId=@OwnPaymentLocationId, LocationsPreparedAt=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND Id=@BatchId;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_PrepareHawalaImportLocations_v1;");
        migrationBuilder.Sql("DROP TYPE IF EXISTS dbo.HawalaImportLocationTableType_v1;");
        migrationBuilder.DropColumn("LocationsPreparedAt", "HawalaImportBatches");
    }
}
