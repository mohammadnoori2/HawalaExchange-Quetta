using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicCommissionProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TYPE [dbo].[CommissionRateTableType_v1] AS TABLE
                (
                    [CurrencyId] bigint NOT NULL PRIMARY KEY,
                    [SourceToAfnRate] decimal(18,8) NOT NULL
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]
                    @Mode nvarchar(10),
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @CorrespondentId bigint,
                    @PeriodFrom date,
                    @PeriodTo date,
                    @FromUtc datetime2,
                    @ToUtcExclusive datetime2,
                    @CommissionPerLakhAfn decimal(18,4),
                    @UsdToAfnRate decimal(18,8),
                    @Rates [dbo].[CommissionRateTableType_v1] READONLY
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    IF @Mode NOT IN (N'Preview', N'Post')
                        THROW 50001, N'نوع عملیات محاسبه کمیشن معتبر نیست.', 1;
                    IF @PeriodTo < @PeriodFrom
                        THROW 50002, N'تاریخ پایان نمی‌تواند قبل از تاریخ آغاز باشد.', 1;
                    IF @CommissionPerLakhAfn <= 0
                        THROW 50003, N'کمیشن هر لک باید بزرگ‌تر از صفر باشد.', 1;
                    IF @Mode = N'Post' AND @UsdToAfnRate <= 0
                        THROW 50004, N'نرخ تبدیل USD به AFN الزامی است.', 1;
                    IF NOT EXISTS
                    (
                        SELECT 1 FROM [dbo].[Correspondents]
                        WHERE [TenantId] = @TenantId AND [Id] = @CorrespondentId
                          AND [IsArchived] = 0 AND [CommissionMethod] = N'PeriodicPerLakh'
                    )
                        THROW 50005, N'نمایندگی فعال با روش کمیشن دوره‌ای یافت نشد.', 1;
                    IF NOT EXISTS
                    (
                        SELECT 1 FROM [dbo].[Users]
                        WHERE [TenantId] = @TenantId AND [Id] = @CurrentUserId AND [IsActive] = 1
                    )
                        THROW 50006, N'کاربر جاری معتبر نیست.', 1;

                    IF @Mode = N'Post'
                        BEGIN TRANSACTION;

                    BEGIN TRY
                        DECLARE @AfnCurrencyId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Currencies]
                            WHERE [TenantId] = @TenantId AND [Code] = N'AFN' AND [IsActive] = 1
                        );

                        CREATE TABLE #Eligible
                        (
                            [HawalaId] bigint NOT NULL PRIMARY KEY,
                            [HawalaNumber] bigint NOT NULL,
                            [HawalaDate] datetime2 NOT NULL,
                            [CurrencyId] bigint NOT NULL,
                            [CurrencyCode] nvarchar(10) NOT NULL,
                            [SourceAmount] decimal(18,4) NOT NULL,
                            [SourceToAfnRate] decimal(18,8) NOT NULL,
                            [AfnEquivalent] decimal(38,8) NOT NULL,
                            [CommissionAfn] decimal(38,8) NOT NULL
                        );

                        INSERT INTO #Eligible
                        (
                            [HawalaId], [HawalaNumber], [HawalaDate], [CurrencyId],
                            [CurrencyCode], [SourceAmount], [SourceToAfnRate],
                            [AfnEquivalent], [CommissionAfn]
                        )
                        SELECT h.[Id], h.[Number], h.[CreatedAt], h.[FromCurrencyId], c.[Code],
                               CAST(h.[FromAmount] AS decimal(18,4)),
                               rate.[SourceToAfnRate],
                               CAST(h.[FromAmount] * rate.[SourceToAfnRate] AS decimal(38,8)),
                               CAST(h.[FromAmount] * rate.[SourceToAfnRate] / 100000 * @CommissionPerLakhAfn AS decimal(38,8))
                        FROM [dbo].[Hawalas] h WITH (UPDLOCK, HOLDLOCK)
                        INNER JOIN [dbo].[Currencies] c
                            ON c.[TenantId] = @TenantId AND c.[Id] = h.[FromCurrencyId]
                        LEFT JOIN @Rates supplied ON supplied.[CurrencyId] = h.[FromCurrencyId]
                        CROSS APPLY
                        (
                            SELECT CAST(CASE WHEN h.[FromCurrencyId] = @AfnCurrencyId THEN 1
                                             ELSE COALESCE(supplied.[SourceToAfnRate], 0) END
                                        AS decimal(18,8)) AS [SourceToAfnRate]
                        ) rate
                        WHERE h.[TenantId] = @TenantId
                          AND h.[CorrespondentId] = @CorrespondentId
                          AND h.[HawalaType] = N'HawalaReceive'
                          AND h.[Status] <> N'Cancel'
                          AND h.[CreatedAt] >= @FromUtc
                          AND h.[CreatedAt] < @ToUtcExclusive
                          AND (h.[CommissionAmount] IS NULL OR h.[CommissionAmount] = 0)
                          AND NOT EXISTS
                          (
                              SELECT 1 FROM [dbo].[CorrespondentCommissionBatchItems] bi WITH (UPDLOCK, HOLDLOCK)
                              WHERE bi.[TenantId] = @TenantId AND bi.[HawalaId] = h.[Id] AND bi.[IsActive] = 1
                          );

                        IF @Mode = N'Post' AND EXISTS (SELECT 1 FROM #Eligible WHERE [SourceToAfnRate] <= 0)
                        BEGIN
                            DECLARE @MissingCurrencies nvarchar(2000) =
                            (
                                SELECT STRING_AGG([CurrencyCode], N'، ')
                                FROM (SELECT DISTINCT [CurrencyCode] FROM #Eligible WHERE [SourceToAfnRate] <= 0) missing
                            );
                            DECLARE @MissingRateMessage nvarchar(2048) =
                                N'نرخ تبدیل به افغانی برای ' + @MissingCurrencies + N' وارد نشده است.';
                            THROW 50007, @MissingRateMessage, 1;
                        END;

                        DECLARE @HawalaCount int = (SELECT COUNT(*) FROM #Eligible);
                        DECLARE @TotalBaseAfn decimal(18,4) =
                            CAST(COALESCE((SELECT SUM([AfnEquivalent]) FROM #Eligible), 0) AS decimal(18,4));
                        DECLARE @TotalCommissionAfn decimal(18,4) =
                            CAST(ROUND(@TotalBaseAfn / 100000 * @CommissionPerLakhAfn, 0) AS decimal(18,4));
                        DECLARE @TotalCommissionUsd decimal(18,4) =
                            CAST(CASE WHEN @UsdToAfnRate > 0
                                      THEN ROUND(@TotalCommissionAfn / @UsdToAfnRate, 0)
                                      ELSE 0 END AS decimal(18,4));
                        DECLARE @CorrespondentName nvarchar(200) =
                            (SELECT [Name] FROM [dbo].[Correspondents]
                             WHERE [TenantId] = @TenantId AND [Id] = @CorrespondentId);

                        IF @Mode = N'Preview'
                        BEGIN
                            SELECT @CorrespondentId AS [CorrespondentId],
                                   @CorrespondentName AS [CorrespondentName],
                                   CAST(@PeriodFrom AS datetime2) AS [PeriodFrom],
                                   CAST(@PeriodTo AS datetime2) AS [PeriodTo],
                                   @HawalaCount AS [HawalaCount],
                                   @TotalBaseAfn AS [TotalBaseAfn],
                                   @TotalCommissionAfn AS [TotalCommissionAfn],
                                   @TotalCommissionUsd AS [TotalCommissionUsd];

                            SELECT [CurrencyId], [CurrencyCode],
                                   CAST(SUM([SourceAmount]) AS decimal(18,4)) AS [TotalAmount],
                                   MAX([SourceToAfnRate]) AS [SourceToAfnRate]
                            FROM #Eligible
                            GROUP BY [CurrencyId], [CurrencyCode]
                            ORDER BY [CurrencyCode];

                            SELECT [HawalaId], [HawalaNumber], [HawalaDate], [CurrencyId],
                                   [CurrencyCode], [SourceAmount], [SourceToAfnRate],
                                   CAST([AfnEquivalent] AS decimal(18,4)) AS [AfnEquivalent],
                                   CAST([CommissionAfn] AS decimal(18,4)) AS [CommissionAfn]
                            FROM #Eligible
                            ORDER BY [HawalaDate], [HawalaNumber];
                            RETURN;
                        END;

                        IF @HawalaCount = 0
                            THROW 50008, N'حواله محاسبه‌نشده‌ای در این دوره وجود ندارد.', 1;
                        IF @TotalCommissionUsd <= 0
                            THROW 50009, N'کمیشن نهایی پس از گردکردن کمتر از یک دالر است و قابل ثبت نیست.', 1;

                        DECLARE @UsdCurrencyId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Currencies]
                            WHERE [TenantId] = @TenantId AND [Code] = N'USD' AND [IsActive] = 1
                        );
                        IF @UsdCurrencyId IS NULL
                            THROW 50010, N'ارز فعال USD در سیستم یافت نشد.', 1;

                        DECLARE @CorrespondentAccountId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Accounts]
                            WHERE [TenantId] = @TenantId AND [CorrespondentId] = @CorrespondentId
                              AND [IsArchived] = 0
                        );
                        IF @CorrespondentAccountId IS NULL
                            THROW 50011, N'حساب فعال نمایندگی یافت نشد.', 1;

                        DECLARE @IncomeAccountId bigint;
                        SELECT @IncomeAccountId = [Id]
                        FROM [dbo].[Accounts] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = @TenantId AND [AccountCode] = N'3001';
                        IF @IncomeAccountId IS NULL
                        BEGIN
                            INSERT INTO [dbo].[Accounts]
                                ([TenantId], [AccountCode], [AccountName], [AccountType], [IsArchived], [CreatedAt])
                            VALUES
                                (@TenantId, N'3001', N'کارمزد حواله', N'Income', 0, SYSUTCDATETIME());
                            SET @IncomeAccountId = SCOPE_IDENTITY();
                        END
                        ELSE IF EXISTS
                        (
                            SELECT 1 FROM [dbo].[Accounts]
                            WHERE [TenantId] = @TenantId AND [Id] = @IncomeAccountId
                              AND ([IsArchived] = 1 OR [AccountType] <> N'Income')
                        )
                            THROW 50012, N'حساب 3001 باید یک حساب درآمد فعال باشد.', 1;

                        DECLARE @BranchId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Branches]
                            WHERE [TenantId] = @TenantId
                            ORDER BY CASE WHEN [Code] = N'HQ' THEN 0 ELSE 1 END, [Id]
                        );
                        IF @BranchId IS NULL
                            THROW 50013, N'برای صرافی جاری هیچ شعبه‌ای تعریف نشده است.', 1;

                        DECLARE @Now datetime2 = SYSUTCDATETIME();
                        DECLARE @NumberPrefix nvarchar(40) = N'PC-' + CONVERT(char(8), @Now, 112) + N'-';
                        DECLARE @LockResult int;
                        DECLARE @LockResource nvarchar(255) =
                            N'PeriodicCommissionNumber:' + CONVERT(nvarchar(20), @TenantId);
                        EXEC @LockResult = sp_getapplock
                            @Resource = @LockResource,
                            @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
                        IF @LockResult < 0
                            THROW 50014, N'ایجاد شماره سند کمیشن ممکن نشد؛ دوباره تلاش کنید.', 1;

                        DECLARE @NextNumber int = COALESCE
                        (
                            (SELECT MAX(TRY_CONVERT(int, SUBSTRING([TransactionNo], LEN(@NumberPrefix) + 1, 20)))
                             FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK)
                             WHERE [TenantId] = @TenantId AND [TransactionNo] LIKE @NumberPrefix + N'%'), 0
                        ) + 1;
                        DECLARE @TransactionNo nvarchar(50) = @NumberPrefix + FORMAT(@NextNumber, N'0000');
                        DECLARE @Remarks nvarchar(1000) = N'کمیشن دوره‌ای نمایندگی ' + @CorrespondentName
                            + N' از ' + CONVERT(nvarchar(10), @PeriodFrom, 23)
                            + N' تا ' + CONVERT(nvarchar(10), @PeriodTo, 23);

                        INSERT INTO [dbo].[Transactions]
                            ([TenantId], [TransactionNo], [TransactionType], [BranchId], [Status],
                             [Remarks], [CreatedBy], [CreatedAt])
                        VALUES
                            (@TenantId, @TransactionNo, N'PeriodicCorrespondentCommission', @BranchId,
                             N'Paid', @Remarks, @CurrentUserId, @Now);
                        DECLARE @TransactionId bigint = SCOPE_IDENTITY();

                        INSERT INTO [dbo].[CorrespondentCommissionBatches]
                            ([TenantId], [CorrespondentId], [PeriodFrom], [PeriodTo],
                             [CommissionPerLakhAfn], [UsdToAfnRate], [TotalBaseAfn],
                             [TotalCommissionAfn], [TotalCommissionUsd], [Status],
                             [PostingTransactionId], [CreatedBy], [CreatedAt])
                        VALUES
                            (@TenantId, @CorrespondentId, @PeriodFrom, @PeriodTo,
                             @CommissionPerLakhAfn, @UsdToAfnRate, @TotalBaseAfn,
                             @TotalCommissionAfn, @TotalCommissionUsd, N'Posted',
                             @TransactionId, @CurrentUserId, @Now);
                        DECLARE @BatchId bigint = SCOPE_IDENTITY();

                        INSERT INTO [dbo].[CorrespondentCommissionBatchItems]
                            ([TenantId], [BatchId], [HawalaId], [SourceCurrencyId], [SourceAmount],
                             [SourceToAfnRate], [AfnEquivalent], [CommissionAfn], [IsActive])
                        SELECT @TenantId, @BatchId, [HawalaId], [CurrencyId], [SourceAmount],
                               [SourceToAfnRate], CAST([AfnEquivalent] AS decimal(18,4)),
                               CAST([CommissionAfn] AS decimal(18,4)), 1
                        FROM #Eligible;

                        DECLARE @Description nvarchar(500) = N'کمیشن دوره‌ای نمایندگی '
                            + @CorrespondentName + N'، ' + CONVERT(nvarchar(20), @HawalaCount) + N' حواله';
                        INSERT INTO [dbo].[LedgerEntries]
                            ([TenantId], [TransactionId], [AccountId], [CurrencyId],
                             [TalabKar], [BadehKar], [Description], [CreatedAt])
                        VALUES
                            (@TenantId, @TransactionId, @CorrespondentAccountId, @UsdCurrencyId,
                             0, @TotalCommissionUsd, @Description, @Now),
                            (@TenantId, @TransactionId, @IncomeAccountId, @UsdCurrencyId,
                             @TotalCommissionUsd, 0, @Description, @Now);

                        INSERT INTO [dbo].[AuditLogs]
                            ([TenantId], [UserId], [ProcessId], [Action], [TableName], [RecordId],
                             [NewValue], [CreatedAt])
                        VALUES
                            (@TenantId, @CurrentUserId, NEWID(), N'POST_PERIODIC_COMMISSION',
                             N'CorrespondentCommissionBatches', @BatchId,
                             N'کمیشن ' + CONVERT(nvarchar(20), @HawalaCount) + N' حواله به مبلغ '
                                + CONVERT(nvarchar(50), @TotalCommissionUsd) + N' USD ثبت شد.', @Now);

                        COMMIT TRANSACTION;

                        SELECT @BatchId AS [Id], @CorrespondentId AS [CorrespondentId],
                               @CorrespondentName AS [CorrespondentName],
                               CAST(@PeriodFrom AS datetime2) AS [PeriodFrom],
                               CAST(@PeriodTo AS datetime2) AS [PeriodTo],
                               @HawalaCount AS [HawalaCount],
                               @CommissionPerLakhAfn AS [CommissionPerLakhAfn],
                               @UsdToAfnRate AS [UsdToAfnRate], @TotalBaseAfn AS [TotalBaseAfn],
                               @TotalCommissionAfn AS [TotalCommissionAfn],
                               @TotalCommissionUsd AS [TotalCommissionUsd], N'Posted' AS [Status],
                               @Now AS [CreatedAt];
                    END TRY
                    BEGIN CATCH
                        IF XACT_STATE() <> 0 AND @Mode = N'Post'
                            ROLLBACK TRANSACTION;
                        THROW;
                    END CATCH
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_ProcessPeriodicCommission_v1];");
            migrationBuilder.Sql("DROP TYPE IF EXISTS [dbo].[CommissionRateTableType_v1];");
        }
    }
}
