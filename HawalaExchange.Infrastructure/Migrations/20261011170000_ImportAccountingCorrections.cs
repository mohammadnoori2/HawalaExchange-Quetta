using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261011170000_ImportAccountingCorrections")]
public sealed class ImportAccountingCorrections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR ALTER PROCEDURE dbo.usp_ReverseImportedHawalaLedger_v1
            @TenantId bigint, @UserId bigint, @HawalaIds nvarchar(max)
        AS
        BEGIN
            SET NOCOUNT ON; SET XACT_ABORT ON;
            IF @@TRANCOUNT=0 THROW 50001, 'Import correction requires a transaction.', 1;
            CREATE TABLE #Ids (Id bigint PRIMARY KEY);
            INSERT #Ids SELECT DISTINCT CONVERT(bigint,[value]) FROM OPENJSON(@HawalaIds);
            IF EXISTS (SELECT 1 FROM #Ids ids WHERE NOT EXISTS
                (SELECT 1 FROM dbo.HawalaImportRows r WHERE r.TenantId=@TenantId AND (r.HawalaId=ids.Id OR r.GeneratedSendHawalaId=ids.Id)))
                THROW 50002, N'حواله متعلق به آپلود صرافی جاری نیست.', 1;
            IF EXISTS (SELECT 1 FROM dbo.CorrespondentSettlementConversionHawalas c JOIN #Ids ids ON ids.Id=c.HawalaId WHERE c.TenantId=@TenantId)
                THROW 50003, N'ابتدا تبدیل ارز توافقی مرتبط با حواله را برگشت دهید.', 1;
            CREATE TABLE #Net (HawalaId bigint,AccountId bigint,CurrencyId bigint,Net decimal(18,4));
            INSERT #Net
                SELECT h.Id,e.AccountId,e.CurrencyId,SUM(e.TalabKar-e.BadehKar)
                FROM #Ids ids JOIN dbo.Hawalas h WITH (UPDLOCK,HOLDLOCK) ON h.TenantId=@TenantId AND h.Id=ids.Id
                JOIN dbo.LedgerEntries e ON e.TenantId=@TenantId AND e.HawalaId=h.Id
                WHERE h.Status<>N'Cancel'
                GROUP BY h.Id,e.AccountId,e.CurrencyId HAVING SUM(e.TalabKar-e.BadehKar)<>0;
            IF NOT EXISTS (SELECT 1 FROM #Net) RETURN;
            -- Hawala journals have exactly one source (HawalaId), never TransactionId as well.
            INSERT dbo.LedgerEntries (TenantId,HawalaId,AccountId,CurrencyId,TalabKar,BadehKar,Description,CreatedAt)
                SELECT @TenantId,HawalaId,AccountId,CurrencyId,
                    CASE WHEN Net<0 THEN -Net ELSE 0 END,CASE WHEN Net>0 THEN Net ELSE 0 END,
                    N'اصلاح حواله آپلود گروهی — برگشت مانده ثبت قبلی',SYSUTCDATETIME() FROM #Net;
        END;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_ReverseImportedHawalaLedger_v1;");
}
