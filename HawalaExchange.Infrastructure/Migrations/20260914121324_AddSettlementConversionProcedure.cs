using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementConversionProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TYPE [dbo].[IdTableType_v1] AS TABLE
                (
                    [Id] bigint NOT NULL PRIMARY KEY
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TYPE [dbo].[SettlementRateTableType_v1] AS TABLE
                (
                    [HawalaId] bigint NOT NULL,
                    [SourceCurrencyId] bigint NOT NULL,
                    [Rate] decimal(18,8) NOT NULL,
                    PRIMARY KEY ([HawalaId], [SourceCurrencyId])
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE PROCEDURE [dbo].[usp_ProcessCorrespondentSettlement_v1]
                    @SourceMode nvarchar(20),
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @CorrespondentId bigint,
                    @Note nvarchar(500) = NULL,
                    @HawalaIds [dbo].[IdTableType_v1] READONLY,
                    @Rates [dbo].[SettlementRateTableType_v1] READONLY
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    IF @SourceMode NOT IN (N'Hawalas', N'Account')
                        THROW 50101, N'روش تبدیل مانده معتبر نیست.', 1;
                    IF @CorrespondentId <= 0
                        THROW 50102, N'نمایندگی معتبر انتخاب نشده است.', 1;
                    IF NOT EXISTS
                    (
                        SELECT 1 FROM [dbo].[Users]
                        WHERE [TenantId] = @TenantId AND [Id] = @CurrentUserId AND [IsActive] = 1
                    )
                        THROW 50103, N'کاربر جاری معتبر نیست.', 1;
                    IF @SourceMode = N'Hawalas' AND NOT EXISTS (SELECT 1 FROM @HawalaIds)
                        THROW 50104, N'حداقل یک حواله را برای تبدیل انتخاب کنید.', 1;
                    IF @SourceMode = N'Account' AND NOT EXISTS
                        (SELECT 1 FROM @Rates WHERE [HawalaId] = 0 AND [Rate] > 0)
                        THROW 50105, N'حداقل یک ارز را برای تبدیل انتخاب کنید.', 1;

                    BEGIN TRANSACTION;
                    BEGIN TRY
                        DECLARE @LockResult int;
                        DECLARE @LockResource nvarchar(255) = N'CorrespondentSettlement:'
                            + CONVERT(nvarchar(20), @TenantId) + N':' + CONVERT(nvarchar(20), @CorrespondentId);
                        EXEC @LockResult = sp_getapplock
                            @Resource = @LockResource, @LockMode = N'Exclusive',
                            @LockOwner = N'Transaction', @LockTimeout = 30000;
                        IF @LockResult < 0
                            THROW 50106, N'قفل تبدیل مانده دریافت نشد؛ دوباره تلاش کنید.', 1;

                        DECLARE @CorrespondentName nvarchar(200);
                        DECLARE @TargetCurrencyId bigint;
                        SELECT @CorrespondentName = c.[Name], @TargetCurrencyId = c.[SettlementCurrencyId]
                        FROM [dbo].[Correspondents] c WITH (UPDLOCK, HOLDLOCK)
                        WHERE c.[TenantId] = @TenantId AND c.[Id] = @CorrespondentId AND c.[IsArchived] = 0;
                        IF @CorrespondentName IS NULL
                            THROW 50107, N'نمایندگی فعال یافت نشد.', 1;
                        IF @TargetCurrencyId IS NULL
                            THROW 50108, N'برای این نمایندگی ارز توافقی تعیین نشده است.', 1;

                        DECLARE @TargetCurrencyCode nvarchar(10);
                        DECLARE @TargetPriority int;
                        DECLARE @TargetDecimalPlaces int;
                        SELECT @TargetCurrencyCode = [Code], @TargetPriority = [QuotationPriority],
                               @TargetDecimalPlaces = [DecimalPlaces]
                        FROM [dbo].[Currencies]
                        WHERE [TenantId] = @TenantId AND [Id] = @TargetCurrencyId AND [IsActive] = 1;
                        IF @TargetCurrencyCode IS NULL
                            THROW 50109, N'ارز توافقی این نمایندگی غیرفعال یا نامعتبر است.', 1;

                        DECLARE @CorrespondentAccountId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Accounts] WITH (UPDLOCK, HOLDLOCK)
                            WHERE [TenantId] = @TenantId AND [CorrespondentId] = @CorrespondentId
                              AND [IsArchived] = 0
                        );
                        IF @CorrespondentAccountId IS NULL
                            THROW 50110, N'حساب فعال نمایندگی یافت نشد.', 1;

                        CREATE TABLE #Balances
                        (
                            [RowId] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            [HawalaId] bigint NULL,
                            [HawalaNumber] bigint NULL,
                            [SourceCurrencyId] bigint NOT NULL,
                            [SourceCurrencyCode] nvarchar(10) NOT NULL,
                            [SourcePriority] int NOT NULL,
                            [SourceTalabKar] decimal(18,4) NOT NULL,
                            [SourceBadehKar] decimal(18,4) NOT NULL,
                            [ExchangeRate] decimal(18,8) NOT NULL,
                            [TargetTalabKar] decimal(18,4) NOT NULL,
                            [TargetBadehKar] decimal(18,4) NOT NULL
                        );

                        IF @SourceMode = N'Hawalas'
                        BEGIN
                            IF EXISTS
                            (
                                SELECT 1 FROM @HawalaIds selected
                                LEFT JOIN [dbo].[Hawalas] h WITH (UPDLOCK, HOLDLOCK)
                                    ON h.[TenantId] = @TenantId AND h.[Id] = selected.[Id]
                                   AND h.[CorrespondentId] = @CorrespondentId
                                   AND h.[Status] <> N'Cancel'
                                   AND CASE WHEN h.[HawalaType] = N'HawalaSend'
                                            THEN h.[ToCurrencyId] ELSE h.[FromCurrencyId] END <> @TargetCurrencyId
                                WHERE h.[Id] IS NULL OR EXISTS
                                (
                                    SELECT 1 FROM [dbo].[CorrespondentSettlementConversionHawalas] link WITH (UPDLOCK, HOLDLOCK)
                                    WHERE link.[TenantId] = @TenantId AND link.[HawalaId] = selected.[Id]
                                )
                            )
                                THROW 50111, N'یک یا چند حواله معتبر نیست، قبلاً تبدیل شده یا به ارز توافقی ثبت شده است.', 1;

                            INSERT INTO #Balances
                            (
                                [HawalaId], [HawalaNumber], [SourceCurrencyId], [SourceCurrencyCode],
                                [SourcePriority], [SourceTalabKar], [SourceBadehKar], [ExchangeRate],
                                [TargetTalabKar], [TargetBadehKar]
                            )
                            SELECT h.[Id], h.[Number], le.[CurrencyId], currency.[Code], currency.[QuotationPriority],
                                   CASE WHEN SUM(le.[TalabKar]) > SUM(le.[BadehKar])
                                        THEN CAST(SUM(le.[TalabKar]) - SUM(le.[BadehKar]) AS decimal(18,4)) ELSE 0 END,
                                   CASE WHEN SUM(le.[BadehKar]) > SUM(le.[TalabKar])
                                        THEN CAST(SUM(le.[BadehKar]) - SUM(le.[TalabKar]) AS decimal(18,4)) ELSE 0 END,
                                   COALESCE(rate.[Rate], 0), 0, 0
                            FROM @HawalaIds selected
                            INNER JOIN [dbo].[Hawalas] h
                                ON h.[TenantId] = @TenantId AND h.[Id] = selected.[Id]
                            INNER JOIN [dbo].[LedgerEntries] le WITH (UPDLOCK, HOLDLOCK)
                                ON le.[TenantId] = @TenantId AND le.[HawalaId] = h.[Id]
                               AND le.[AccountId] = @CorrespondentAccountId
                               AND le.[CurrencyId] <> @TargetCurrencyId
                            INNER JOIN [dbo].[Currencies] currency
                                ON currency.[TenantId] = @TenantId AND currency.[Id] = le.[CurrencyId]
                            LEFT JOIN @Rates rate
                                ON rate.[HawalaId] = h.[Id] AND rate.[SourceCurrencyId] = le.[CurrencyId]
                            GROUP BY h.[Id], h.[Number], le.[CurrencyId], currency.[Code],
                                     currency.[QuotationPriority], rate.[Rate]
                            HAVING SUM(le.[TalabKar]) <> SUM(le.[BadehKar]);

                            IF (SELECT COUNT(DISTINCT [HawalaId]) FROM #Balances) <> (SELECT COUNT(*) FROM @HawalaIds)
                                THROW 50112, N'برای یک یا چند حواله ماندهٔ قابل تبدیل پیدا نشد.', 1;
                            IF EXISTS (SELECT 1 FROM #Balances WHERE [ExchangeRate] <= 0)
                                THROW 50113, N'نرخ یک یا چند حواله وارد نشده است.', 1;
                        END
                        ELSE
                        BEGIN
                            INSERT INTO #Balances
                            (
                                [SourceCurrencyId], [SourceCurrencyCode], [SourcePriority],
                                [SourceTalabKar], [SourceBadehKar], [ExchangeRate],
                                [TargetTalabKar], [TargetBadehKar]
                            )
                            SELECT le.[CurrencyId], currency.[Code], currency.[QuotationPriority],
                                   CASE WHEN SUM(le.[TalabKar]) > SUM(le.[BadehKar])
                                        THEN CAST(SUM(le.[TalabKar]) - SUM(le.[BadehKar]) AS decimal(18,4)) ELSE 0 END,
                                   CASE WHEN SUM(le.[BadehKar]) > SUM(le.[TalabKar])
                                        THEN CAST(SUM(le.[BadehKar]) - SUM(le.[TalabKar]) AS decimal(18,4)) ELSE 0 END,
                                   rate.[Rate], 0, 0
                            FROM @Rates rate
                            INNER JOIN [dbo].[LedgerEntries] le WITH (UPDLOCK, HOLDLOCK)
                                ON le.[TenantId] = @TenantId AND le.[AccountId] = @CorrespondentAccountId
                               AND le.[CurrencyId] = rate.[SourceCurrencyId]
                            INNER JOIN [dbo].[Currencies] currency
                                ON currency.[TenantId] = @TenantId AND currency.[Id] = le.[CurrencyId]
                            WHERE rate.[HawalaId] = 0 AND rate.[Rate] > 0
                              AND le.[CurrencyId] <> @TargetCurrencyId
                            GROUP BY le.[CurrencyId], currency.[Code], currency.[QuotationPriority], rate.[Rate]
                            HAVING SUM(le.[TalabKar]) <> SUM(le.[BadehKar]);

                            IF (SELECT COUNT(*) FROM #Balances) <>
                               (SELECT COUNT(*) FROM @Rates WHERE [HawalaId] = 0 AND [Rate] > 0)
                                THROW 50114, N'یک یا چند ارز انتخاب‌شده دیگر ماندهٔ قابل تبدیل ندارد.', 1;
                        END;

                        UPDATE #Balances
                        SET [TargetTalabKar] = CASE WHEN [SourceTalabKar] > [SourceBadehKar]
                             THEN CAST(ROUND(CASE WHEN [SourcePriority] < @TargetPriority OR
                                                      ([SourcePriority] = @TargetPriority AND [SourceCurrencyCode] < @TargetCurrencyCode)
                                                  THEN ([SourceTalabKar] - [SourceBadehKar]) * [ExchangeRate]
                                                  ELSE ([SourceTalabKar] - [SourceBadehKar]) / [ExchangeRate] END,
                                             CASE WHEN @TargetDecimalPlaces < 0 THEN 0
                                                  WHEN @TargetDecimalPlaces > 8 THEN 8 ELSE @TargetDecimalPlaces END)
                                       AS decimal(18,4)) ELSE 0 END,
                            [TargetBadehKar] = CASE WHEN [SourceBadehKar] > [SourceTalabKar]
                             THEN CAST(ROUND(CASE WHEN [SourcePriority] < @TargetPriority OR
                                                      ([SourcePriority] = @TargetPriority AND [SourceCurrencyCode] < @TargetCurrencyCode)
                                                  THEN ([SourceBadehKar] - [SourceTalabKar]) * [ExchangeRate]
                                                  ELSE ([SourceBadehKar] - [SourceTalabKar]) / [ExchangeRate] END,
                                             CASE WHEN @TargetDecimalPlaces < 0 THEN 0
                                                  WHEN @TargetDecimalPlaces > 8 THEN 8 ELSE @TargetDecimalPlaces END)
                                       AS decimal(18,4)) ELSE 0 END;
                        IF EXISTS (SELECT 1 FROM #Balances WHERE [TargetTalabKar] <= 0 AND [TargetBadehKar] <= 0)
                            THROW 50115, N'حاصل یک یا چند تبدیل معتبر نیست.', 1;

                        DECLARE @ClearingAccountId bigint;
                        SELECT @ClearingAccountId = [Id]
                        FROM [dbo].[Accounts] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = @TenantId AND [AccountCode] = N'SYS-SETTLEMENT-CLEARING';
                        IF @ClearingAccountId IS NULL
                        BEGIN
                            INSERT INTO [dbo].[Accounts]
                                ([TenantId], [AccountCode], [AccountName], [AccountType], [IsArchived], [CreatedAt])
                            VALUES
                                (@TenantId, N'SYS-SETTLEMENT-CLEARING', N'حساب واسط تبدیل ارز نمایندگی‌ها',
                                 N'CurrencyConversionClearing', 0, SYSUTCDATETIME());
                            SET @ClearingAccountId = SCOPE_IDENTITY();
                        END
                        ELSE IF EXISTS
                        (
                            SELECT 1 FROM [dbo].[Accounts]
                            WHERE [TenantId] = @TenantId AND [Id] = @ClearingAccountId
                              AND [AccountType] <> N'CurrencyConversionClearing'
                        )
                            THROW 50116, N'کد حساب واسط تبدیل ارز قبلاً برای حساب دیگری استفاده شده است.', 1;

                        DECLARE @BranchId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Branches]
                            WHERE [TenantId] = @TenantId
                            ORDER BY CASE WHEN [Code] = N'HQ' THEN 0 ELSE 1 END, [Id]
                        );
                        IF @BranchId IS NULL
                            THROW 50117, N'برای صرافی جاری هیچ شعبه‌ای تعریف نشده است.', 1;

                        DECLARE @Now datetime2 = SYSUTCDATETIME();
                        DECLARE @NumberPrefix nvarchar(40) = N'SC-' + CONVERT(char(8), @Now, 112) + N'-';
                        DECLARE @NumberLockResource nvarchar(255) =
                            N'SettlementConversionNumber:' + CONVERT(nvarchar(20), @TenantId);
                        EXEC @LockResult = sp_getapplock
                            @Resource = @NumberLockResource, @LockMode = N'Exclusive',
                            @LockOwner = N'Transaction', @LockTimeout = 30000;
                        IF @LockResult < 0
                            THROW 50118, N'ایجاد شماره سند تبدیل ممکن نشد؛ دوباره تلاش کنید.', 1;
                        DECLARE @NextNumber int = COALESCE
                        (
                            (SELECT MAX(TRY_CONVERT(int, SUBSTRING([TransactionNo], LEN(@NumberPrefix) + 1, 20)))
                             FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK)
                             WHERE [TenantId] = @TenantId AND [TransactionNo] LIKE @NumberPrefix + N'%'), 0
                        ) + 1;
                        DECLARE @TransactionNo nvarchar(50) = @NumberPrefix + FORMAT(@NextNumber, N'0000');
                        DECLARE @Remarks nvarchar(1000) = CASE WHEN @SourceMode = N'Hawalas'
                            THEN N'تبدیل حواله‌های نمایندگی ' ELSE N'تبدیل حساب نمایندگی ' END
                            + @CorrespondentName + N' به ' + @TargetCurrencyCode
                            + CASE WHEN NULLIF(LTRIM(RTRIM(@Note)), N'') IS NULL THEN N'' ELSE N'. ' + LTRIM(RTRIM(@Note)) END;

                        INSERT INTO [dbo].[Transactions]
                            ([TenantId], [TransactionNo], [TransactionType], [BranchId], [Status],
                             [Remarks], [CreatedBy], [CreatedAt])
                        VALUES
                            (@TenantId, @TransactionNo, N'CorrespondentSettlementConversion', @BranchId,
                             N'Paid', @Remarks, @CurrentUserId, @Now);
                        DECLARE @TransactionId bigint = SCOPE_IDENTITY();

                        INSERT INTO [dbo].[CorrespondentSettlementConversions]
                            ([TenantId], [CorrespondentId], [TargetCurrencyId], [TransactionId],
                             [SourceMode], [Note], [CreatedAt], [CreatedBy])
                        VALUES
                            (@TenantId, @CorrespondentId, @TargetCurrencyId, @TransactionId,
                             @SourceMode, NULLIF(LTRIM(RTRIM(@Note)), N''), @Now, @CurrentUserId);
                        DECLARE @ConversionId bigint = SCOPE_IDENTITY();
                        DECLARE @HawalaCount int;

                        IF @SourceMode = N'Hawalas'
                        BEGIN
                            INSERT INTO [dbo].[CorrespondentSettlementConversionHawalas]
                                ([TenantId], [ConversionId], [HawalaId])
                            SELECT @TenantId, @ConversionId, [Id] FROM @HawalaIds;
                            SET @HawalaCount = @@ROWCOUNT;

                            DECLARE @InsertedHawalaItems TABLE
                            (
                                [Id] bigint NOT NULL,
                                [HawalaId] bigint NOT NULL,
                                [SourceCurrencyId] bigint NOT NULL,
                                PRIMARY KEY ([HawalaId], [SourceCurrencyId])
                            );
                            INSERT INTO [dbo].[CorrespondentSettlementConversionHawalaItems]
                            (
                                [TenantId], [ConversionId], [HawalaId], [SourceCurrencyId],
                                [SourceTalabKar], [SourceBadehKar], [ExchangeRate],
                                [TargetTalabKar], [TargetBadehKar]
                            )
                            OUTPUT inserted.[Id], inserted.[HawalaId], inserted.[SourceCurrencyId]
                                INTO @InsertedHawalaItems ([Id], [HawalaId], [SourceCurrencyId])
                            SELECT @TenantId, @ConversionId, [HawalaId], [SourceCurrencyId],
                                   [SourceTalabKar], [SourceBadehKar], [ExchangeRate],
                                   [TargetTalabKar], [TargetBadehKar]
                            FROM #Balances;

                            INSERT INTO [dbo].[LedgerEntries]
                            (
                                [TenantId], [TransactionId], [AccountId], [CurrencyId],
                                [TalabKar], [BadehKar], [SettlementHawalaItemId], [Description], [CreatedAt]
                            )
                            SELECT @TenantId, @TransactionId, entries.[AccountId], entries.[CurrencyId],
                                   entries.[TalabKar], entries.[BadehKar], item.[Id],
                                   N'تبدیل مانده نمایندگی ' + @CorrespondentName + N' به ارز توافقی', @Now
                            FROM #Balances balance
                            INNER JOIN @InsertedHawalaItems item
                                ON item.[HawalaId] = balance.[HawalaId]
                               AND item.[SourceCurrencyId] = balance.[SourceCurrencyId]
                            CROSS APPLY
                            (
                                SELECT @CorrespondentAccountId, balance.[SourceCurrencyId],
                                       balance.[SourceBadehKar], balance.[SourceTalabKar]
                                UNION ALL SELECT @ClearingAccountId, balance.[SourceCurrencyId],
                                       balance.[SourceTalabKar], balance.[SourceBadehKar]
                                UNION ALL SELECT @CorrespondentAccountId, @TargetCurrencyId,
                                       balance.[TargetTalabKar], balance.[TargetBadehKar]
                                UNION ALL SELECT @ClearingAccountId, @TargetCurrencyId,
                                       balance.[TargetBadehKar], balance.[TargetTalabKar]
                            ) entries ([AccountId], [CurrencyId], [TalabKar], [BadehKar]);
                        END
                        ELSE
                        BEGIN
                            INSERT INTO [dbo].[CorrespondentSettlementConversionHawalas]
                                ([TenantId], [ConversionId], [HawalaId])
                            SELECT @TenantId, @ConversionId, h.[Id]
                            FROM [dbo].[Hawalas] h WITH (UPDLOCK, HOLDLOCK)
                            WHERE h.[TenantId] = @TenantId AND h.[CorrespondentId] = @CorrespondentId
                              AND h.[Status] <> N'Cancel'
                              AND CASE WHEN h.[HawalaType] = N'HawalaSend'
                                       THEN h.[ToCurrencyId] ELSE h.[FromCurrencyId] END IN
                                  (SELECT [SourceCurrencyId] FROM #Balances)
                              AND NOT EXISTS
                                  (SELECT 1 FROM [dbo].[CorrespondentSettlementConversionHawalas] link
                                   WHERE link.[TenantId] = @TenantId AND link.[HawalaId] = h.[Id]);
                            SET @HawalaCount = @@ROWCOUNT;

                            INSERT INTO [dbo].[CorrespondentSettlementConversionItems]
                            (
                                [TenantId], [ConversionId], [SourceCurrencyId], [SourceTalabKar],
                                [SourceBadehKar], [ExchangeRate], [TargetTalabKar], [TargetBadehKar]
                            )
                            SELECT @TenantId, @ConversionId, [SourceCurrencyId], [SourceTalabKar],
                                   [SourceBadehKar], [ExchangeRate], [TargetTalabKar], [TargetBadehKar]
                            FROM #Balances;

                            INSERT INTO [dbo].[LedgerEntries]
                            (
                                [TenantId], [TransactionId], [AccountId], [CurrencyId],
                                [TalabKar], [BadehKar], [Description], [CreatedAt]
                            )
                            SELECT @TenantId, @TransactionId, entries.[AccountId], entries.[CurrencyId],
                                   entries.[TalabKar], entries.[BadehKar],
                                   N'تبدیل مانده نمایندگی ' + @CorrespondentName + N' به ارز توافقی', @Now
                            FROM #Balances balance
                            CROSS APPLY
                            (
                                SELECT @CorrespondentAccountId, balance.[SourceCurrencyId],
                                       balance.[SourceBadehKar], balance.[SourceTalabKar]
                                UNION ALL SELECT @ClearingAccountId, balance.[SourceCurrencyId],
                                       balance.[SourceTalabKar], balance.[SourceBadehKar]
                                UNION ALL SELECT @CorrespondentAccountId, @TargetCurrencyId,
                                       balance.[TargetTalabKar], balance.[TargetBadehKar]
                                UNION ALL SELECT @ClearingAccountId, @TargetCurrencyId,
                                       balance.[TargetBadehKar], balance.[TargetTalabKar]
                            ) entries ([AccountId], [CurrencyId], [TalabKar], [BadehKar]);
                        END;

                        DECLARE @CurrencyCount int = (SELECT COUNT(DISTINCT [SourceCurrencyId]) FROM #Balances);
                        INSERT INTO [dbo].[AuditLogs]
                            ([TenantId], [UserId], [ProcessId], [Action], [TableName], [RecordId],
                             [NewValue], [CreatedAt])
                        VALUES
                            (@TenantId, @CurrentUserId, NEWID(),
                             CASE WHEN @SourceMode = N'Hawalas' THEN N'CONVERT_HAWALAS_TO_SETTLEMENT'
                                  ELSE N'CONVERT_BALANCE_TO_SETTLEMENT' END,
                             N'CorrespondentSettlementConversions', @ConversionId,
                             N'نمایندگی ' + @CorrespondentName + N': ' + CONVERT(nvarchar(20), @CurrencyCount)
                                + N' ارز و ' + CONVERT(nvarchar(20), @HawalaCount)
                                + N' حواله به ' + @TargetCurrencyCode + N' تبدیل شد.', @Now);

                        COMMIT TRANSACTION;

                        SELECT @ConversionId AS [ConversionId], @TransactionId AS [TransactionId],
                               @HawalaCount AS [HawalaCount], @CurrencyCount AS [CurrencyCount];
                        SELECT [HawalaId], [HawalaNumber], [SourceCurrencyCode],
                               CAST(ABS([SourceTalabKar] - [SourceBadehKar]) AS decimal(18,4)) AS [SourceAmount],
                               CASE WHEN [SourceTalabKar] > [SourceBadehKar] THEN N'طلبکار' ELSE N'بدهکار' END AS [BalanceDirection],
                               [ExchangeRate], @TargetCurrencyCode AS [TargetCurrencyCode],
                               CAST(ABS([TargetTalabKar] - [TargetBadehKar]) AS decimal(18,4)) AS [TargetAmount]
                        FROM #Balances ORDER BY [HawalaNumber], [SourceCurrencyCode];
                    END TRY
                    BEGIN CATCH
                        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                        THROW;
                    END CATCH
                END;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_ProcessCorrespondentSettlement_v1];");
            migrationBuilder.Sql("DROP TYPE IF EXISTS [dbo].[SettlementRateTableType_v1];");
            migrationBuilder.Sql("DROP TYPE IF EXISTS [dbo].[IdTableType_v1];");
        }
    }
}
