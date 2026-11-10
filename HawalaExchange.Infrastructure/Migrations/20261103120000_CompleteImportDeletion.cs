using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103120000_CompleteImportDeletion")]
public sealed class CompleteImportDeletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsDeleted", "Hawalas", type: "bit", nullable: false, defaultValue: false);
        // Previously removed imports must not reappear in normal hawala lists.
        migrationBuilder.Sql("UPDATE h SET IsDeleted=1 FROM dbo.Hawalas h JOIN dbo.HawalaImportRows r ON r.TenantId=h.TenantId AND (r.HawalaId=h.Id OR r.GeneratedSendHawalaId=h.Id) JOIN dbo.HawalaImportBatches b ON b.TenantId=r.TenantId AND b.Id=r.BatchId WHERE b.Status=N'Deleted';");
        using var stream = typeof(CompleteImportDeletion).Assembly.GetManifestResourceStream("HawalaExchange.Infrastructure.Sql.DeleteImportedHawalaAccounting.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
        migrationBuilder.Sql("EXEC(N'ALTER PROCEDURE dbo.usp_GetHawalaStatistics_v1 @TenantId bigint AS BEGIN SET NOCOUNT ON; SELECT COALESCE(SUM(CASE WHEN HawalaType=N''HawalaSend'' THEN CAST(1 AS bigint) ELSE 0 END),0) HawalaSendCount,COALESCE(SUM(CASE WHEN HawalaType=N''HawalaReceive'' THEN CAST(1 AS bigint) ELSE 0 END),0) HawalaReceiveCount,COALESCE(SUM(CASE WHEN HawalaType=N''HawalaOther'' THEN CAST(1 AS bigint) ELSE 0 END),0) HawalaOtherCount,COALESCE(SUM(CASE WHEN Status=N''Pending'' THEN CAST(1 AS bigint) ELSE 0 END),0) PendingCount,COALESCE(SUM(CASE WHEN Status=N''Paid'' THEN CAST(1 AS bigint) ELSE 0 END),0) PaidCount FROM dbo.Hawalas WHERE TenantId=@TenantId AND IsDeleted=0; END');");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_DeleteImportedHawalaAccounting_v1;");
        migrationBuilder.Sql("EXEC(N'ALTER PROCEDURE dbo.usp_GetHawalaStatistics_v1 @TenantId bigint AS BEGIN SET NOCOUNT ON; SELECT COALESCE(SUM(CASE WHEN HawalaType=N''HawalaSend'' THEN CAST(1 AS bigint) ELSE 0 END),0) HawalaSendCount,COALESCE(SUM(CASE WHEN HawalaType=N''HawalaReceive'' THEN CAST(1 AS bigint) ELSE 0 END),0) HawalaReceiveCount,COALESCE(SUM(CASE WHEN HawalaType=N''HawalaOther'' THEN CAST(1 AS bigint) ELSE 0 END),0) HawalaOtherCount,COALESCE(SUM(CASE WHEN Status=N''Pending'' THEN CAST(1 AS bigint) ELSE 0 END),0) PendingCount,COALESCE(SUM(CASE WHEN Status=N''Paid'' THEN CAST(1 AS bigint) ELSE 0 END),0) PaidCount FROM dbo.Hawalas WHERE TenantId=@TenantId; END');");
        migrationBuilder.DropColumn("IsDeleted", "Hawalas");
    }
}
