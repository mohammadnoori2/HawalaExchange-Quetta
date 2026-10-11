using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261011140000_BulkImportReferencePreparation")]
public sealed class BulkImportReferencePreparation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE TYPE dbo.ImportEntityNamesTableType_v1 AS TABLE (NormalizedName nvarchar(200) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL);");
        migrationBuilder.Sql("CREATE TYPE dbo.ImportAssignmentTableType_v1 AS TABLE (PaymentLocationId bigint NOT NULL PRIMARY KEY, CorrespondentId bigint NOT NULL);");
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_BulkCreateImportLocations_v1
                @TenantId bigint, @UserId bigint, @Names dbo.ImportEntityNamesTableType_v1 READONLY
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                IF @@TRANCOUNT = 0 THROW 50001, 'Import preparation requires a transaction.', 1;
                IF EXISTS (SELECT 1 FROM @Names n JOIN dbo.PaymentLocations p WITH (UPDLOCK,HOLDLOCK)
                           ON p.TenantId=@TenantId AND p.NormalizedName=n.NormalizedName WHERE p.IsActive=0)
                    OR EXISTS (SELECT 1 FROM @Names n JOIN dbo.PaymentLocationAliases a WITH (UPDLOCK,HOLDLOCK)
                               ON a.TenantId=@TenantId AND a.NormalizedName=n.NormalizedName
                               JOIN dbo.PaymentLocations p ON p.TenantId=@TenantId AND p.Id=a.PaymentLocationId WHERE p.IsActive=0)
                    THROW 50002, N'نام محل پرداخت با یک محل غیرفعال تکراری است.', 1;
                DECLARE @Created TABLE (Id bigint, Name nvarchar(200));
                INSERT dbo.PaymentLocations (TenantId,Name,NormalizedName,Address,IsActive,CreatedAt,CreatedBy)
                OUTPUT inserted.Id,inserted.Name INTO @Created
                SELECT @TenantId,n.Name,n.NormalizedName,N'',1,SYSUTCDATETIME(),@UserId FROM @Names n
                WHERE NOT EXISTS (SELECT 1 FROM dbo.PaymentLocations p WITH (UPDLOCK,HOLDLOCK) WHERE p.TenantId=@TenantId AND p.NormalizedName=n.NormalizedName)
                  AND NOT EXISTS (SELECT 1 FROM dbo.PaymentLocationAliases a WITH (UPDLOCK,HOLDLOCK) WHERE a.TenantId=@TenantId AND a.NormalizedName=n.NormalizedName);
                INSERT dbo.AuditLogs (TenantId,UserId,ProcessId,Action,TableName,RecordId,NewValue,CreatedAt)
                SELECT @TenantId,@UserId,NEWID(),'CREATE','PaymentLocations',c.Id,
                       (SELECT c.Name AS Name,N'ایجاد محل پرداخت در آماده‌سازی گروهی' AS __Description FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SYSUTCDATETIME() FROM @Created c;
                SELECT n.NormalizedName,p.Id FROM @Names n JOIN dbo.PaymentLocations p
                ON p.TenantId=@TenantId AND p.IsActive=1 AND p.NormalizedName=n.NormalizedName
                UNION ALL
                SELECT n.NormalizedName,p.Id FROM @Names n JOIN dbo.PaymentLocationAliases a ON a.TenantId=@TenantId AND a.NormalizedName=n.NormalizedName
                JOIN dbo.PaymentLocations p ON p.TenantId=@TenantId AND p.Id=a.PaymentLocationId AND p.IsActive=1
                WHERE NOT EXISTS (SELECT 1 FROM dbo.PaymentLocations direct WHERE direct.TenantId=@TenantId AND direct.NormalizedName=n.NormalizedName);
            END
            """);
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_BulkCreateImportCorrespondents_v1
                @TenantId bigint, @UserId bigint, @Names dbo.ImportEntityNamesTableType_v1 READONLY
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                IF @@TRANCOUNT = 0 THROW 50001, 'Import preparation requires a transaction.', 1;
                IF EXISTS (SELECT 1 FROM @Names n JOIN dbo.Correspondents c WITH (UPDLOCK,HOLDLOCK)
                           ON c.TenantId=@TenantId AND c.Name=n.Name AND c.IsArchived=0)
                    THROW 50002, N'نمایندگی از قبل وجود دارد؛ آن را از فهرست موجود انتخاب کنید.', 1;
                DECLARE @Base bigint, @AccountBase bigint, @Count int=(SELECT COUNT(*) FROM @Names), @CandidateCount int;
                SELECT @Base=COALESCE(MAX(TRY_CONVERT(bigint,SUBSTRING(Code,4,47))),0)
                FROM dbo.Correspondents WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND Code LIKE 'AG-%';
                SELECT @AccountBase=COALESCE(MAX(TRY_CONVERT(bigint,AccountCode)),1999)
                FROM dbo.Accounts WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND (AccountCode LIKE '2%' OR AccountType='Correspondent');
                IF @AccountBase<1999 SET @AccountBase=1999;
                SELECT @CandidateCount=@Count+COUNT(*) FROM dbo.Accounts WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId;
                DECLARE @Codes TABLE (RowNumber bigint PRIMARY KEY, AccountCode nvarchar(50));
                ;WITH candidates AS
                (SELECT TOP (@CandidateCount) @AccountBase+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS Number
                 FROM sys.all_objects a CROSS JOIN sys.all_objects b), available AS
                (SELECT TOP (@Count) Number FROM candidates n WHERE NOT EXISTS
                 (SELECT 1 FROM dbo.Accounts a WHERE a.TenantId=@TenantId AND a.AccountCode=CONVERT(nvarchar(50),n.Number)) ORDER BY Number)
                INSERT @Codes SELECT ROW_NUMBER() OVER(ORDER BY Number),CONVERT(nvarchar(50),Number) FROM available;
                IF (SELECT COUNT(*) FROM @Codes)<>@Count THROW 50003, 'Could not allocate account codes.', 1;
                DECLARE @Created TABLE (Id bigint, Name nvarchar(200), Code nvarchar(50));
                ;WITH numbered AS (SELECT Name,ROW_NUMBER() OVER(ORDER BY NormalizedName) AS RowNumber FROM @Names)
                INSERT dbo.Correspondents (TenantId,Code,Name,City,IsArchived,CommissionMethod,CreatedAt)
                OUTPUT inserted.Id,inserted.Name,inserted.Code INTO @Created
                SELECT @TenantId,'AG-'+CASE WHEN @Base+n.RowNumber<1000 THEN RIGHT('000'+CONVERT(varchar(30),@Base+n.RowNumber),3)
                       ELSE CONVERT(varchar(30),@Base+n.RowNumber) END,n.Name,LEFT(n.Name,100),0,'PerTransaction',SYSUTCDATETIME() FROM numbered n;
                DECLARE @Accounts TABLE (Id bigint, AccountName nvarchar(200), AccountCode nvarchar(50), CorrespondentId bigint);
                ;WITH numbered AS (SELECT c.*,ROW_NUMBER() OVER(ORDER BY n.NormalizedName) AS RowNumber FROM @Created c JOIN @Names n ON n.Name=c.Name)
                INSERT dbo.Accounts (TenantId,AccountCode,AccountName,AccountType,CorrespondentId,IsArchived,CreatedAt)
                OUTPUT inserted.Id,inserted.AccountName,inserted.AccountCode,inserted.CorrespondentId INTO @Accounts
                SELECT @TenantId,codes.AccountCode,LEFT(N'نماینده: '+c.Name,200),'Correspondent',c.Id,0,SYSUTCDATETIME()
                FROM numbered c JOIN @Codes codes ON codes.RowNumber=c.RowNumber;
                INSERT dbo.AuditLogs (TenantId,UserId,ProcessId,Action,TableName,RecordId,NewValue,CreatedAt)
                SELECT @TenantId,@UserId,NEWID(),'CREATE','Correspondents',c.Id,
                    (SELECT c.Name AS Name,c.Code AS Code,N'ایجاد نمایندگی در آماده‌سازی گروهی' AS __Description FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SYSUTCDATETIME() FROM @Created c;
                INSERT dbo.AuditLogs (TenantId,UserId,ProcessId,Action,TableName,RecordId,NewValue,CreatedAt)
                SELECT @TenantId,@UserId,NEWID(),'CREATE','Accounts',a.Id,
                    (SELECT a.AccountName AS AccountName,a.AccountCode AS AccountCode,a.CorrespondentId AS CorrespondentId,
                            N'ایجاد حساب نمایندگی در آماده‌سازی گروهی' AS __Description FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SYSUTCDATETIME() FROM @Accounts a;
                SELECT n.NormalizedName,c.Id FROM @Names n JOIN @Created c ON c.Name=n.Name;
            END
            """);
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_BulkCreateImportAssignments_v1
                @TenantId bigint, @UserId bigint, @EffectiveFrom date, @Assignments dbo.ImportAssignmentTableType_v1 READONLY
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                IF @@TRANCOUNT = 0 THROW 50001, 'Import preparation requires a transaction.', 1;
                IF EXISTS (SELECT 1 FROM @Assignments a LEFT JOIN dbo.PaymentLocations p
                           ON p.TenantId=@TenantId AND p.Id=a.PaymentLocationId AND p.IsActive=1
                           LEFT JOIN dbo.Correspondents c ON c.TenantId=@TenantId AND c.Id=a.CorrespondentId AND c.IsArchived=0
                           WHERE p.Id IS NULL OR c.Id IS NULL)
                    THROW 50002, N'محل پرداخت یا نمایندگی مسئول فعال یا متعلق به صرافی جاری نیست.', 1;
                IF EXISTS (SELECT 1 FROM @Assignments a JOIN dbo.PaymentLocationCorrespondentAssignments history WITH (UPDLOCK,HOLDLOCK)
                           ON history.TenantId=@TenantId AND history.PaymentLocationId=a.PaymentLocationId)
                    THROW 50003, N'برای محل پرداخت تاریخچه مسئولیت ثبت شده است؛ آن را دوباره بررسی کنید.', 1;
                DECLARE @Created TABLE (Id bigint, PaymentLocationId bigint, CorrespondentId bigint);
                INSERT dbo.PaymentLocationCorrespondentAssignments (TenantId,PaymentLocationId,CorrespondentId,EffectiveFrom,CreatedBy,CreatedAt)
                OUTPUT inserted.Id,inserted.PaymentLocationId,inserted.CorrespondentId INTO @Created
                SELECT @TenantId,a.PaymentLocationId,a.CorrespondentId,@EffectiveFrom,@UserId,SYSUTCDATETIME() FROM @Assignments a;
                INSERT dbo.AuditLogs (TenantId,UserId,ProcessId,Action,TableName,RecordId,NewValue,CreatedAt)
                SELECT @TenantId,@UserId,NEWID(),'CREATE','PaymentLocationCorrespondentAssignments',c.Id,
                       (SELECT c.PaymentLocationId AS PaymentLocationId,c.CorrespondentId AS CorrespondentId,
                        @EffectiveFrom AS EffectiveFrom,N'تعیین مسئول پرداخت در آماده‌سازی گروهی' AS __Description FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SYSUTCDATETIME() FROM @Created c;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_BulkCreateImportAssignments_v1; DROP PROCEDURE IF EXISTS dbo.usp_BulkCreateImportCorrespondents_v1; DROP PROCEDURE IF EXISTS dbo.usp_BulkCreateImportLocations_v1; DROP TYPE IF EXISTS dbo.ImportAssignmentTableType_v1; DROP TYPE IF EXISTS dbo.ImportEntityNamesTableType_v1;");
    }
}
