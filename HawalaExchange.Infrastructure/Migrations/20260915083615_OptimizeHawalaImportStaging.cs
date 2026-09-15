using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeHawalaImportStaging : Migration
    {
        /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TYPE [dbo].[HawalaImportResultTableType_v1] AS TABLE
            (
                [RowId] bigint NOT NULL PRIMARY KEY,
                [HawalaId] bigint NOT NULL,
                [GeneratedSendHawalaId] bigint NULL,
                [PaymentLocationId] bigint NOT NULL,
                [DestinationCorrespondentId] bigint NULL,
                [AgentCommissionAmount] decimal(18,2) NULL,
                [AgentCommissionCurrencyId] bigint NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[usp_ValidateHawalaImportStaging_v1]
                @TenantId bigint,
                @BatchId bigint
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE @CorrespondentId bigint, @ExpectedRows int, @Status nvarchar(30);
                SELECT @CorrespondentId = [CorrespondentId], @ExpectedRows = [RowCount], @Status = [Status]
                FROM [dbo].[HawalaImportBatches] WITH (UPDLOCK, HOLDLOCK)
                WHERE [TenantId] = @TenantId AND [Id] = @BatchId;

                IF @CorrespondentId IS NULL
                    THROW 51000, N'پیش‌نمایش آپلود پیدا نشد.', 1;
                IF @Status <> N'Preview'
                    THROW 51000, N'این پیش‌نمایش دیگر قابل اعتبارسنجی نیست.', 1;
                IF (SELECT COUNT(*) FROM [dbo].[HawalaImportRows] WHERE [TenantId] = @TenantId AND [BatchId] = @BatchId) <> @ExpectedRows
                    THROW 51000, N'تعداد ردیف‌های جدول آماده‌سازی با فایل برابر نیست.', 1;

                UPDATE r
                SET [CurrencyId] = c.[Id], [CurrencyCode] = c.[Code]
                FROM [dbo].[HawalaImportRows] r
                INNER JOIN [dbo].[Currencies] c
                    ON c.[TenantId] = r.[TenantId]
                   AND c.[IsActive] = 1
                   AND UPPER(LTRIM(RTRIM(c.[Code]))) = UPPER(LTRIM(RTRIM(r.[CurrencyCode])))
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId;

                UPDATE r
                SET [AgentCommissionCurrencyId] = COALESCE(cc.[Id], r.[CurrencyId]),
                    [AgentCommissionCurrencyCode] = COALESCE(cc.[Code], c.[Code])
                FROM [dbo].[HawalaImportRows] r
                LEFT JOIN [dbo].[Currencies] cc
                    ON cc.[TenantId] = r.[TenantId] AND cc.[IsActive] = 1
                   AND UPPER(LTRIM(RTRIM(cc.[Code]))) = UPPER(LTRIM(RTRIM(r.[AgentCommissionCurrencyCode])))
                LEFT JOIN [dbo].[Currencies] c
                    ON c.[TenantId] = r.[TenantId] AND c.[Id] = r.[CurrencyId] AND c.[IsActive] = 1
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId
                  AND r.[AgentCommissionAmount] IS NOT NULL;

                UPDATE r SET [ValidationErrors] =
                    CASE WHEN NULLIF(r.[ValidationErrors], N'') IS NULL
                         THEN N'شماره حواله در همین فایل تکراری است.'
                         ELSE r.[ValidationErrors] + N' | شماره حواله در همین فایل تکراری است.' END
                FROM [dbo].[HawalaImportRows] r
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId AND r.[HawalaNumber] IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM [dbo].[HawalaImportRows] d
                      WHERE d.[TenantId] = r.[TenantId] AND d.[BatchId] = r.[BatchId]
                        AND d.[HawalaNumber] = r.[HawalaNumber] AND d.[Id] <> r.[Id]);

                UPDATE r SET [ValidationErrors] =
                    CASE WHEN NULLIF(r.[ValidationErrors], N'') IS NULL
                         THEN N'رفرنس در همین فایل تکراری است.'
                         ELSE r.[ValidationErrors] + N' | رفرنس در همین فایل تکراری است.' END
                FROM [dbo].[HawalaImportRows] r
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId AND NULLIF(r.[ReferenceNumber], N'') IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM [dbo].[HawalaImportRows] d
                      WHERE d.[TenantId] = r.[TenantId] AND d.[BatchId] = r.[BatchId]
                        AND d.[ReferenceNumber] = r.[ReferenceNumber] AND d.[Id] <> r.[Id]);

                UPDATE r SET [ValidationErrors] =
                    CASE WHEN NULLIF(r.[ValidationErrors], N'') IS NULL
                         THEN N'شماره حواله قبلاً برای این نمایندگی ثبت شده است.'
                         ELSE r.[ValidationErrors] + N' | شماره حواله قبلاً برای این نمایندگی ثبت شده است.' END
                FROM [dbo].[HawalaImportRows] r
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId
                  AND EXISTS (
                      SELECT 1 FROM [dbo].[Hawalas] h
                      WHERE h.[TenantId] = @TenantId AND h.[CorrespondentId] = @CorrespondentId
                        AND h.[HawalaType] = N'HawalaReceive' AND h.[Number] = r.[HawalaNumber]);

                UPDATE r SET [ValidationErrors] =
                    CASE WHEN NULLIF(r.[ValidationErrors], N'') IS NULL
                         THEN N'رفرنس قبلاً برای این نمایندگی ثبت شده است.'
                         ELSE r.[ValidationErrors] + N' | رفرنس قبلاً برای این نمایندگی ثبت شده است.' END
                FROM [dbo].[HawalaImportRows] r
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId
                  AND NULLIF(r.[ReferenceNumber], N'') IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM [dbo].[Hawalas] h
                      WHERE h.[TenantId] = @TenantId AND h.[CorrespondentId] = @CorrespondentId
                        AND h.[HawalaType] = N'HawalaReceive' AND h.[ReferenceNumber] = r.[ReferenceNumber]);

                UPDATE r SET [ValidationErrors] =
                    CASE WHEN NULLIF(r.[ValidationErrors], N'') IS NULL
                         THEN N'ارز تعریف یا فعال نیست.'
                         ELSE r.[ValidationErrors] + N' | ارز تعریف یا فعال نیست.' END
                FROM [dbo].[HawalaImportRows] r
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId
                  AND (r.[ValidationErrors] IS NULL OR r.[ValidationErrors] NOT LIKE N'%ارز «%')
                  AND NOT EXISTS (SELECT 1 FROM [dbo].[Currencies] c
                                  WHERE c.[TenantId] = @TenantId AND c.[Id] = r.[CurrencyId] AND c.[IsActive] = 1);

                UPDATE r SET [ValidationErrors] =
                    CASE WHEN NULLIF(r.[ValidationErrors], N'') IS NULL
                         THEN N'ارز کمیشن تعریف یا فعال نیست.'
                         ELSE r.[ValidationErrors] + N' | ارز کمیشن تعریف یا فعال نیست.' END
                FROM [dbo].[HawalaImportRows] r
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId
                  AND r.[AgentCommissionAmount] IS NOT NULL
                  AND (r.[ValidationErrors] IS NULL OR r.[ValidationErrors] NOT LIKE N'%ارز کمیشن%')
                  AND NOT EXISTS (SELECT 1 FROM [dbo].[Currencies] c
                                  WHERE c.[TenantId] = @TenantId AND c.[Id] = r.[AgentCommissionCurrencyId] AND c.[IsActive] = 1);
            END
            """);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[usp_CleanupHawalaImportStaging_v1]
                @TenantId bigint,
                @CreatedBefore datetime2
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;
                DELETE FROM [dbo].[HawalaImportBatches]
                WHERE [TenantId] = @TenantId
                  AND [Status] = N'Preview'
                  AND [CreatedAt] < @CreatedBefore;
                SELECT @@ROWCOUNT AS [DeletedBatchCount];
            END
            """);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[usp_FinalizeHawalaImportStaging_v1]
                @TenantId bigint,
                @BatchId bigint,
                @ConfirmedBy bigint,
                @Mappings [dbo].[HawalaImportResultTableType_v1] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE @ExpectedRows int, @Status nvarchar(30);
                SELECT @ExpectedRows = [RowCount], @Status = [Status]
                FROM [dbo].[HawalaImportBatches] WITH (UPDLOCK, HOLDLOCK)
                WHERE [TenantId] = @TenantId AND [Id] = @BatchId;
                IF @ExpectedRows IS NULL
                    THROW 51000, N'پیش‌نمایش آپلود پیدا نشد.', 1;
                IF @Status <> N'Preview'
                    THROW 51000, N'این پیش‌نمایش قبلاً ثبت شده یا دیگر قابل استفاده نیست.', 1;
                IF (SELECT COUNT(*) FROM @Mappings) <> @ExpectedRows
                    THROW 51000, N'نتیجه ثبت با تعداد ردیف‌های پیش‌نمایش برابر نیست.', 1;
                IF EXISTS (
                    SELECT 1 FROM @Mappings m
                    LEFT JOIN [dbo].[HawalaImportRows] r
                      ON r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId AND r.[Id] = m.[RowId]
                    WHERE r.[Id] IS NULL)
                    THROW 51000, N'یکی از ردیف‌های نتیجه متعلق به این پیش‌نمایش نیست.', 1;
                IF EXISTS (
                    SELECT 1 FROM @Mappings m
                    LEFT JOIN [dbo].[Hawalas] h
                      ON h.[TenantId] = @TenantId AND h.[Id] = m.[HawalaId] AND h.[HawalaType] = N'HawalaReceive'
                    LEFT JOIN [dbo].[Hawalas] g
                      ON g.[TenantId] = @TenantId AND g.[Id] = m.[GeneratedSendHawalaId]
                     AND g.[HawalaType] = N'HawalaSend' AND g.[SourceHawalaId] = m.[HawalaId]
                    WHERE h.[Id] IS NULL OR (m.[GeneratedSendHawalaId] IS NOT NULL AND g.[Id] IS NULL))
                    THROW 51000, N'ارتباط حواله‌های ثبت‌شده با پیش‌نمایش معتبر نیست.', 1;

                UPDATE r
                SET r.[HawalaId] = m.[HawalaId],
                    r.[GeneratedSendHawalaId] = m.[GeneratedSendHawalaId],
                    r.[PaymentLocationId] = m.[PaymentLocationId],
                    r.[DestinationCorrespondentId] = m.[DestinationCorrespondentId],
                    r.[AgentCommissionAmount] = m.[AgentCommissionAmount],
                    r.[AgentCommissionCurrencyId] = m.[AgentCommissionCurrencyId],
                    r.[AgentCommissionCurrencyCode] = c.[Code]
                FROM [dbo].[HawalaImportRows] r
                INNER JOIN @Mappings m ON m.[RowId] = r.[Id]
                LEFT JOIN [dbo].[Currencies] c
                  ON c.[TenantId] = @TenantId AND c.[Id] = m.[AgentCommissionCurrencyId]
                WHERE r.[TenantId] = @TenantId AND r.[BatchId] = @BatchId;

                UPDATE [dbo].[HawalaImportBatches]
                SET [Status] = N'Posted', [ConfirmedAt] = SYSUTCDATETIME(), [ConfirmedBy] = @ConfirmedBy
                WHERE [TenantId] = @TenantId AND [Id] = @BatchId;
            END
            """);
        }

        /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_FinalizeHawalaImportStaging_v1];");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_ValidateHawalaImportStaging_v1];");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_CleanupHawalaImportStaging_v1];");
        migrationBuilder.Sql("DROP TYPE IF EXISTS [dbo].[HawalaImportResultTableType_v1];");
    }
}
}
