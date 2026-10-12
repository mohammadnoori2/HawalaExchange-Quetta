using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261011160000_PreserveUnpostedImportDrafts")]
public sealed class PreserveUnpostedImportDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep the signature for older application instances, but never expire user drafts.
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_CleanupHawalaImportStaging_v1
                @TenantId bigint,
                @CreatedBefore datetime2
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT CAST(0 AS int) AS DeletedBatchCount;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not reintroduce automatic deletion of drafts during a rollback.
    }
}
