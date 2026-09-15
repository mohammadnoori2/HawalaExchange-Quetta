using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeAedDealProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE PROCEDURE [dbo].[usp_ProcessAedDeal_v1]
                    @Operation nvarchar(30),
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @DealId bigint = 0,
                    @ConversionId bigint = 0,
                    @DealNumber nvarchar(50) = NULL,
                    @SourceCorrespondentId bigint = 0,
                    @DubaiCorrespondentId bigint = 0,
                    @SourceCurrencyId bigint = 0,
                    @Amount decimal(18,4) = 0,
                    @RoundingDecimalPlaces int = 0,
                    @ActualMarker decimal(18,4) = 0,
                    @DeclaredMarker decimal(18,4) = 0,
                    @Note nvarchar(500) = NULL,
                    @Reason nvarchar(500) = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    IF @Operation NOT IN (N'Create', N'Convert', N'ReverseConversion', N'Cancel')
                        THROW 50201, N'نوع عملیات معامله درهم معتبر نیست.', 1;
                    IF NOT EXISTS
                    (
                        SELECT 1 FROM [dbo].[Users]
                        WHERE [TenantId] = @TenantId AND [Id] = @CurrentUserId AND [IsActive] = 1
                    )
                        THROW 50202, N'کاربر جاری معتبر نیست.', 1;

                    BEGIN TRANSACTION;
                    BEGIN TRY
                        DECLARE @Now datetime2 = SYSUTCDATETIME();
                        DECLARE @BranchId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Branches]
                            WHERE [TenantId] = @TenantId
                            ORDER BY CASE WHEN [Code] = N'HQ' THEN 0 ELSE 1 END, [Id]
                        );
                        IF @BranchId IS NULL
                            THROW 50203, N'برای صرافی جاری هیچ شعبه‌ای تعریف نشده است.', 1;

                        DECLARE @ResultDealId bigint;
                        DECLARE @LockResult int;

                        IF @Operation = N'Create'
                        BEGIN
                            SET @DealNumber = LTRIM(RTRIM(@DealNumber));
                            IF NULLIF(@DealNumber, N'') IS NULL
                                THROW 50204, N'نمبر معامله الزامی است.', 1;
                            IF @SourceCorrespondentId <= 0 OR @DubaiCorrespondentId <= 0
                                THROW 50205, N'طرف کویته و طرف دبی الزامی است.', 1;
                            IF @SourceCorrespondentId = @DubaiCorrespondentId
                                THROW 50206, N'طرف کویته و طرف دبی نمی‌تواند یکسان باشد.', 1;
                            IF @Amount <= 0
                                THROW 50207, N'مبلغ معامله باید بزرگ‌تر از صفر باشد.', 1;
                            IF @RoundingDecimalPlaces < 0 OR @RoundingDecimalPlaces > 4
                                THROW 50208, N'تعداد اعشار باید بین صفر تا چهار باشد.', 1;

                            DECLARE @CreateLockResource nvarchar(255) = N'AedDealNumber:'
                                + CONVERT(nvarchar(20), @TenantId) + N':' + @DealNumber;
                            EXEC @LockResult = sp_getapplock
                                @Resource = @CreateLockResource, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0
                                THROW 50209, N'قفل ایجاد معامله دریافت نشد؛ دوباره تلاش کنید.', 1;
                            IF EXISTS
                                (SELECT 1 FROM [dbo].[AedDeals] WITH (UPDLOCK, HOLDLOCK)
                                 WHERE [TenantId] = @TenantId AND [DealNumber] = @DealNumber)
                                THROW 50210, N'نمبر معامله قبلاً ثبت شده است.', 1;

                            DECLARE @CreateSourceName nvarchar(200);
                            DECLARE @CreateSourceAccountId bigint;
                            SELECT @CreateSourceName = c.[Name], @CreateSourceAccountId = a.[Id]
                            FROM [dbo].[Correspondents] c
                            INNER JOIN [dbo].[Accounts] a
                                ON a.[TenantId] = @TenantId AND a.[CorrespondentId] = c.[Id] AND a.[IsArchived] = 0
                            WHERE c.[TenantId] = @TenantId AND c.[Id] = @SourceCorrespondentId AND c.[IsArchived] = 0;
                            DECLARE @CreateDubaiName nvarchar(200);
                            DECLARE @CreateDubaiAccountId bigint;
                            SELECT @CreateDubaiName = c.[Name], @CreateDubaiAccountId = a.[Id]
                            FROM [dbo].[Correspondents] c
                            INNER JOIN [dbo].[Accounts] a
                                ON a.[TenantId] = @TenantId AND a.[CorrespondentId] = c.[Id] AND a.[IsArchived] = 0
                            WHERE c.[TenantId] = @TenantId AND c.[Id] = @DubaiCorrespondentId AND c.[IsArchived] = 0;
                            IF @CreateSourceAccountId IS NULL OR @CreateDubaiAccountId IS NULL
                                THROW 50211, N'نمایندگی فعال یا حساب نمایندگی یافت نشد.', 1;

                            DECLARE @CreateCurrencyCode nvarchar(10) =
                                (SELECT [Code] FROM [dbo].[Currencies]
                                 WHERE [TenantId] = @TenantId AND [Id] = @SourceCurrencyId AND [IsActive] = 1);
                            IF @CreateCurrencyCode NOT IN (N'AED', N'USD') OR @CreateCurrencyCode IS NULL
                                THROW 50212, N'ارز معامله فقط می‌تواند AED یا USD باشد.', 1;

                            DECLARE @CreatePrefix nvarchar(40) = N'AEDH-' + CONVERT(char(8), SYSDATETIME(), 112) + N'-';
                            DECLARE @CreateNumberLock nvarchar(255) = N'AedTransactionNumber:'
                                + CONVERT(nvarchar(20), @TenantId) + N':AEDH';
                            EXEC @LockResult = sp_getapplock
                                @Resource = @CreateNumberLock, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0 THROW 50213, N'ایجاد شماره سند ممکن نشد؛ دوباره تلاش کنید.', 1;
                            DECLARE @CreateNext int = COALESCE
                            (
                                (SELECT MAX(TRY_CONVERT(int, SUBSTRING([TransactionNo], LEN(@CreatePrefix) + 1, 20)))
                                 FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK)
                                 WHERE [TenantId] = @TenantId AND [TransactionNo] LIKE @CreatePrefix + N'%'), 0
                            ) + 1;
                            DECLARE @CreateTransactionNo nvarchar(50) = @CreatePrefix + FORMAT(@CreateNext, N'0000');
                            DECLARE @CreateRemarks nvarchar(1000) = N'ثبت معامله درهم ' + @DealNumber
                                + N': ' + @CreateSourceName + N' به ' + @CreateDubaiName;

                            INSERT INTO [dbo].[Transactions]
                                ([TenantId], [TransactionNo], [TransactionType], [BranchId], [Status],
                                 [Remarks], [CreatedBy], [CreatedAt])
                            VALUES
                                (@TenantId, @CreateTransactionNo, N'AedDealHolding', @BranchId, N'Paid',
                                 @CreateRemarks, @CurrentUserId, @Now);
                            DECLARE @CreateTransactionId bigint = SCOPE_IDENTITY();
                            DECLARE @CreateDescription nvarchar(500) =
                                N'نگهداری معامله ' + @DealNumber + N' نزد ' + @CreateDubaiName;
                            INSERT INTO [dbo].[LedgerEntries]
                                ([TenantId], [TransactionId], [AccountId], [CurrencyId],
                                 [TalabKar], [BadehKar], [Description], [CreatedAt])
                            VALUES
                                (@TenantId, @CreateTransactionId, @CreateDubaiAccountId, @SourceCurrencyId,
                                 0, @Amount, @CreateDescription, @Now),
                                (@TenantId, @CreateTransactionId, @CreateSourceAccountId, @SourceCurrencyId,
                                 @Amount, 0, @CreateDescription, @Now);

                            INSERT INTO [dbo].[AedDeals]
                                ([TenantId], [DealNumber], [SourceCorrespondentId], [DubaiCorrespondentId],
                                 [SourceCurrencyId], [OriginalAmount], [ConvertedAmount], [TotalFinalUsd],
                                 [TotalProfitUsd], [AedPerUsdRate], [RoundingDecimalPlaces], [Status],
                                 [HoldingTransactionId], [Note], [CreatedAt], [CreatedBy])
                            VALUES
                                (@TenantId, @DealNumber, @SourceCorrespondentId, @DubaiCorrespondentId,
                                 @SourceCurrencyId, @Amount, 0, 0, 0, 3.67, @RoundingDecimalPlaces,
                                 N'Held', @CreateTransactionId, NULLIF(LTRIM(RTRIM(@Note)), N''), @Now, @CurrentUserId);
                            SET @ResultDealId = SCOPE_IDENTITY();

                            INSERT INTO [dbo].[AuditLogs]
                                ([TenantId], [UserId], [ProcessId], [Action], [TableName], [RecordId], [NewValue], [CreatedAt])
                            VALUES
                                (@TenantId, @CurrentUserId, NEWID(), N'CREATE_AED_DEAL', N'AedDeals', @ResultDealId,
                                 N'معامله ' + @DealNumber + N' به مبلغ ' + CONVERT(nvarchar(50), @Amount)
                                    + N' ' + @CreateCurrencyCode + N' ثبت شد.', @Now);
                        END
                        ELSE IF @Operation = N'Convert'
                        BEGIN
                            IF @DealId <= 0 OR @Amount <= 0
                                THROW 50214, N'معامله و مبلغ تبدیل معتبر الزامی است.', 1;
                            DECLARE @ConvertLockResource nvarchar(255) = N'AedDeal:'
                                + CONVERT(nvarchar(20), @TenantId) + N':' + CONVERT(nvarchar(20), @DealId);
                            EXEC @LockResult = sp_getapplock
                                @Resource = @ConvertLockResource, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0 THROW 50215, N'قفل تبدیل معامله دریافت نشد؛ دوباره تلاش کنید.', 1;

                            DECLARE @ConvertDealNumber nvarchar(50);
                            DECLARE @ConvertStatus nvarchar(20);
                            DECLARE @ConvertOriginal decimal(18,4);
                            DECLARE @ConvertAlready decimal(18,4);
                            DECLARE @ConvertRate decimal(18,8);
                            DECLARE @ConvertPlaces int;
                            DECLARE @ConvertCurrencyId bigint;
                            DECLARE @ConvertCurrencyCode nvarchar(10);
                            DECLARE @ConvertSourceCorrespondentId bigint;
                            DECLARE @ConvertDubaiCorrespondentId bigint;
                            SELECT @ConvertDealNumber = d.[DealNumber], @ConvertStatus = d.[Status],
                                   @ConvertOriginal = d.[OriginalAmount], @ConvertAlready = d.[ConvertedAmount],
                                   @ConvertRate = d.[AedPerUsdRate], @ConvertPlaces = d.[RoundingDecimalPlaces],
                                   @ConvertCurrencyId = d.[SourceCurrencyId], @ConvertCurrencyCode = currency.[Code],
                                   @ConvertSourceCorrespondentId = d.[SourceCorrespondentId],
                                   @ConvertDubaiCorrespondentId = d.[DubaiCorrespondentId]
                            FROM [dbo].[AedDeals] d WITH (UPDLOCK, HOLDLOCK)
                            INNER JOIN [dbo].[Currencies] currency
                                ON currency.[TenantId] = @TenantId AND currency.[Id] = d.[SourceCurrencyId]
                            WHERE d.[TenantId] = @TenantId AND d.[Id] = @DealId;
                            IF @ConvertDealNumber IS NULL THROW 50216, N'معامله درهم یافت نشد.', 1;
                            IF @ConvertStatus = N'Cancelled' THROW 50217, N'معامله لغو شده قابل تبدیل نیست.', 1;
                            DECLARE @ConvertRemaining decimal(18,4) = @ConvertOriginal - @ConvertAlready;
                            IF @Amount > @ConvertRemaining
                                THROW 50218, N'مبلغ تبدیل بیشتر از مانده معامله است.', 1;
                            DECLARE @ActualAdjustment decimal(18,4) =
                                CAST(ROUND(@Amount / 100000 * @ActualMarker, 4) AS decimal(18,4));
                            DECLARE @DeclaredAdjustment decimal(18,4) =
                                CAST(ROUND(@Amount / 100000 * @DeclaredMarker, 4) AS decimal(18,4));
                            DECLARE @ActualAdjusted decimal(38,8) = @Amount + @Amount / 100000 * @ActualMarker;
                            DECLARE @DeclaredAdjusted decimal(38,8) = @Amount + @Amount / 100000 * @DeclaredMarker;
                            IF @ActualAdjusted <= 0 OR @DeclaredAdjusted <= 0
                                THROW 50219, N'حاصل مشخصه معامله نباید منفی یا صفر شود.', 1;
                            DECLARE @FinalUsd decimal(18,4) = CAST(ROUND(
                                CASE WHEN @ConvertCurrencyCode = N'AED' THEN @ActualAdjusted / @ConvertRate ELSE @ActualAdjusted END,
                                @ConvertPlaces) AS decimal(18,4));
                            DECLARE @DeclaredUsd decimal(18,4) = CAST(ROUND(
                                CASE WHEN @ConvertCurrencyCode = N'AED' THEN @DeclaredAdjusted / @ConvertRate ELSE @DeclaredAdjusted END,
                                @ConvertPlaces) AS decimal(18,4));
                            IF @FinalUsd <= 0 OR @DeclaredUsd <= 0
                                THROW 50220, N'حاصل تبدیل پس از گردکردن باید بزرگ‌تر از صفر باشد.', 1;
                            DECLARE @ProfitUsd decimal(18,4) = @FinalUsd - @DeclaredUsd;

                            DECLARE @ConvertSourceAccountId bigint =
                                (SELECT TOP (1) [Id] FROM [dbo].[Accounts]
                                 WHERE [TenantId] = @TenantId AND [CorrespondentId] = @ConvertSourceCorrespondentId AND [IsArchived] = 0);
                            DECLARE @ConvertDubaiAccountId bigint =
                                (SELECT TOP (1) [Id] FROM [dbo].[Accounts]
                                 WHERE [TenantId] = @TenantId AND [CorrespondentId] = @ConvertDubaiCorrespondentId AND [IsArchived] = 0);
                            DECLARE @UsdCurrencyId bigint =
                                (SELECT TOP (1) [Id] FROM [dbo].[Currencies]
                                 WHERE [TenantId] = @TenantId AND [Code] = N'USD' AND [IsActive] = 1);
                            IF @ConvertSourceAccountId IS NULL OR @ConvertDubaiAccountId IS NULL
                                THROW 50221, N'حساب فعال نمایندگی یافت نشد.', 1;
                            IF @UsdCurrencyId IS NULL THROW 50222, N'ارز فعال USD در سیستم یافت نشد.', 1;

                            DECLARE @ProfitLossAccountId bigint = NULL;
                            DECLARE @ProfitLossCode nvarchar(20) = CASE WHEN @ProfitUsd > 0 THEN N'3003' ELSE N'4003' END;
                            DECLARE @ProfitLossName nvarchar(200) = CASE WHEN @ProfitUsd > 0 THEN N'مفاد معاملات درهم' ELSE N'زیان معاملات درهم' END;
                            DECLARE @ProfitLossType nvarchar(50) = CASE WHEN @ProfitUsd > 0 THEN N'Income' ELSE N'Expense' END;
                            IF @ProfitUsd <> 0
                            BEGIN
                                SELECT @ProfitLossAccountId = [Id] FROM [dbo].[Accounts] WITH (UPDLOCK, HOLDLOCK)
                                WHERE [TenantId] = @TenantId AND [AccountCode] = @ProfitLossCode;
                                IF @ProfitLossAccountId IS NULL
                                BEGIN
                                    INSERT INTO [dbo].[Accounts]
                                        ([TenantId], [AccountCode], [AccountName], [AccountType], [IsArchived], [CreatedAt])
                                    VALUES (@TenantId, @ProfitLossCode, @ProfitLossName, @ProfitLossType, 0, @Now);
                                    SET @ProfitLossAccountId = SCOPE_IDENTITY();
                                END
                                ELSE IF EXISTS
                                    (SELECT 1 FROM [dbo].[Accounts] WHERE [TenantId] = @TenantId
                                     AND [Id] = @ProfitLossAccountId AND ([IsArchived] = 1 OR [AccountType] <> @ProfitLossType))
                                    THROW 50223, N'حساب مفاد یا زیان معاملات درهم معتبر نیست.', 1;
                            END;

                            DECLARE @ConvertPrefix nvarchar(40) = N'AEDC-' + CONVERT(char(8), SYSDATETIME(), 112) + N'-';
                            DECLARE @ConvertNumberLock nvarchar(255) = N'AedTransactionNumber:'
                                + CONVERT(nvarchar(20), @TenantId) + N':AEDC';
                            EXEC @LockResult = sp_getapplock
                                @Resource = @ConvertNumberLock, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0 THROW 50224, N'ایجاد شماره سند ممکن نشد؛ دوباره تلاش کنید.', 1;
                            DECLARE @ConvertNext int = COALESCE
                            (
                                (SELECT MAX(TRY_CONVERT(int, SUBSTRING([TransactionNo], LEN(@ConvertPrefix) + 1, 20)))
                                 FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK)
                                 WHERE [TenantId] = @TenantId AND [TransactionNo] LIKE @ConvertPrefix + N'%'), 0
                            ) + 1;
                            DECLARE @ConvertTransactionNo nvarchar(50) = @ConvertPrefix + FORMAT(@ConvertNext, N'0000');
                            INSERT INTO [dbo].[Transactions]
                                ([TenantId], [TransactionNo], [TransactionType], [BranchId], [Status], [Remarks], [CreatedBy], [CreatedAt])
                            VALUES
                                (@TenantId, @ConvertTransactionNo, N'AedDealConversion', @BranchId, N'Paid',
                                 N'تبدیل ' + CONVERT(nvarchar(50), @Amount) + N' ' + @ConvertCurrencyCode
                                    + N' از معامله ' + @ConvertDealNumber + N' به USD', @CurrentUserId, @Now);
                            DECLARE @ConvertTransactionId bigint = SCOPE_IDENTITY();

                            INSERT INTO [dbo].[AedDealConversions]
                                ([TenantId], [AedDealId], [SourceAmount], [AedPerUsdRate], [ActualMarker], [DeclaredMarker],
                                 [ActualAdjustmentSource], [DeclaredAdjustmentSource], [FinalUsdAmount], [DeclaredUsdAmount],
                                 [ProfitUsd], [PostingTransactionId], [Status], [Note], [CreatedAt], [CreatedBy])
                            VALUES
                                (@TenantId, @DealId, @Amount, @ConvertRate, @ActualMarker, @DeclaredMarker,
                                 @ActualAdjustment, @DeclaredAdjustment, @FinalUsd, @DeclaredUsd, @ProfitUsd,
                                 @ConvertTransactionId, N'Posted', NULLIF(LTRIM(RTRIM(@Note)), N''), @Now, @CurrentUserId);
                            DECLARE @NewConversionId bigint = SCOPE_IDENTITY();

                            UPDATE [dbo].[AedDeals]
                            SET [ConvertedAmount] = [ConvertedAmount] + @Amount,
                                [TotalFinalUsd] = [TotalFinalUsd] + @FinalUsd,
                                [TotalProfitUsd] = [TotalProfitUsd] + @ProfitUsd,
                                [Status] = CASE WHEN [ConvertedAmount] + @Amount = [OriginalAmount]
                                                THEN N'Converted' ELSE N'PartiallyConverted' END
                            WHERE [TenantId] = @TenantId AND [Id] = @DealId;

                            DECLARE @ConvertDescription nvarchar(500) = N'تبدیل معامله ' + @ConvertDealNumber + N' به USD';
                            INSERT INTO [dbo].[LedgerEntries]
                                ([TenantId], [TransactionId], [AccountId], [CurrencyId], [TalabKar], [BadehKar], [Description], [CreatedAt])
                            VALUES
                                (@TenantId, @ConvertTransactionId, @ConvertSourceAccountId, @ConvertCurrencyId, 0, @Amount, @ConvertDescription, @Now),
                                (@TenantId, @ConvertTransactionId, @ConvertDubaiAccountId, @ConvertCurrencyId, @Amount, 0, @ConvertDescription, @Now),
                                (@TenantId, @ConvertTransactionId, @ConvertDubaiAccountId, @UsdCurrencyId, 0, @FinalUsd, @ConvertDescription, @Now),
                                (@TenantId, @ConvertTransactionId, @ConvertSourceAccountId, @UsdCurrencyId, @DeclaredUsd, 0, @ConvertDescription, @Now);
                            IF @ProfitUsd > 0
                                INSERT INTO [dbo].[LedgerEntries]
                                    ([TenantId], [TransactionId], [AccountId], [CurrencyId], [TalabKar], [BadehKar], [Description], [CreatedAt])
                                VALUES (@TenantId, @ConvertTransactionId, @ProfitLossAccountId, @UsdCurrencyId,
                                        @ProfitUsd, 0, @ConvertDescription, @Now);
                            ELSE IF @ProfitUsd < 0
                                INSERT INTO [dbo].[LedgerEntries]
                                    ([TenantId], [TransactionId], [AccountId], [CurrencyId], [TalabKar], [BadehKar], [Description], [CreatedAt])
                                VALUES (@TenantId, @ConvertTransactionId, @ProfitLossAccountId, @UsdCurrencyId,
                                        0, ABS(@ProfitUsd), @ConvertDescription, @Now);

                            INSERT INTO [dbo].[AuditLogs]
                                ([TenantId], [UserId], [ProcessId], [Action], [TableName], [RecordId], [NewValue], [CreatedAt])
                            VALUES
                                (@TenantId, @CurrentUserId, NEWID(), N'CONVERT_AED_DEAL', N'AedDealConversions', @NewConversionId,
                                 N'معامله ' + @ConvertDealNumber + N': ' + CONVERT(nvarchar(50), @Amount)
                                    + N' ' + @ConvertCurrencyCode + N' به ' + CONVERT(nvarchar(50), @FinalUsd) + N' USD تبدیل شد.', @Now);
                            SET @ResultDealId = @DealId;
                        END
                        ELSE IF @Operation = N'ReverseConversion'
                        BEGIN
                            IF @ConversionId <= 0 OR NULLIF(LTRIM(RTRIM(@Reason)), N'') IS NULL
                                THROW 50225, N'تبدیل و دلیل برگشت معتبر الزامی است.', 1;
                            DECLARE @ReverseDealId bigint =
                                (SELECT [AedDealId] FROM [dbo].[AedDealConversions]
                                 WHERE [TenantId] = @TenantId AND [Id] = @ConversionId);
                            IF @ReverseDealId IS NULL THROW 50226, N'تبدیل معامله یافت نشد.', 1;
                            DECLARE @ReverseLockResource nvarchar(255) = N'AedDeal:'
                                + CONVERT(nvarchar(20), @TenantId) + N':' + CONVERT(nvarchar(20), @ReverseDealId);
                            EXEC @LockResult = sp_getapplock
                                @Resource = @ReverseLockResource, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0 THROW 50227, N'قفل برگشت معامله دریافت نشد؛ دوباره تلاش کنید.', 1;

                            DECLARE @ReverseStatus nvarchar(20);
                            DECLARE @ReversePostingTransactionId bigint;
                            DECLARE @ReverseSourceAmount decimal(18,4);
                            DECLARE @ReverseFinalUsd decimal(18,4);
                            DECLARE @ReverseProfitUsd decimal(18,4);
                            DECLARE @ReverseDealNumber nvarchar(50);
                            SELECT @ReverseStatus = conversion.[Status],
                                   @ReversePostingTransactionId = conversion.[PostingTransactionId],
                                   @ReverseSourceAmount = conversion.[SourceAmount],
                                   @ReverseFinalUsd = conversion.[FinalUsdAmount],
                                   @ReverseProfitUsd = conversion.[ProfitUsd],
                                   @ReverseDealNumber = deal.[DealNumber]
                            FROM [dbo].[AedDealConversions] conversion WITH (UPDLOCK, HOLDLOCK)
                            INNER JOIN [dbo].[AedDeals] deal WITH (UPDLOCK, HOLDLOCK)
                                ON deal.[TenantId] = @TenantId AND deal.[Id] = conversion.[AedDealId]
                            WHERE conversion.[TenantId] = @TenantId AND conversion.[Id] = @ConversionId;
                            IF @ReverseStatus <> N'Posted'
                                THROW 50228, N'این تبدیل قبلاً برگشت داده شده است.', 1;
                            IF NOT EXISTS
                                (SELECT 1 FROM [dbo].[LedgerEntries]
                                 WHERE [TenantId] = @TenantId AND [TransactionId] = @ReversePostingTransactionId)
                                THROW 50229, N'سند حسابداری تبدیل یافت نشد.', 1;

                            DECLARE @ReversePrefix nvarchar(40) = N'AEDCR-' + CONVERT(char(8), SYSDATETIME(), 112) + N'-';
                            DECLARE @ReverseNumberLock nvarchar(255) = N'AedTransactionNumber:'
                                + CONVERT(nvarchar(20), @TenantId) + N':AEDCR';
                            EXEC @LockResult = sp_getapplock
                                @Resource = @ReverseNumberLock, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0 THROW 50230, N'ایجاد شماره سند ممکن نشد؛ دوباره تلاش کنید.', 1;
                            DECLARE @ReverseNext int = COALESCE
                            (
                                (SELECT MAX(TRY_CONVERT(int, SUBSTRING([TransactionNo], LEN(@ReversePrefix) + 1, 20)))
                                 FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK)
                                 WHERE [TenantId] = @TenantId AND [TransactionNo] LIKE @ReversePrefix + N'%'), 0
                            ) + 1;
                            DECLARE @ReverseTransactionNo nvarchar(50) = @ReversePrefix + FORMAT(@ReverseNext, N'0000');
                            INSERT INTO [dbo].[Transactions]
                                ([TenantId], [TransactionNo], [TransactionType], [BranchId], [Status], [Remarks],
                                 [CreatedBy], [CreatedAt], [ReversedTransactionId])
                            VALUES
                                (@TenantId, @ReverseTransactionNo, N'AedDealReversal', @BranchId, N'Paid',
                                 N'برگشت تبدیل معامله ' + @ReverseDealNumber + N': ' + LTRIM(RTRIM(@Reason)),
                                 @CurrentUserId, @Now, @ReversePostingTransactionId);
                            DECLARE @ReverseTransactionId bigint = SCOPE_IDENTITY();
                            INSERT INTO [dbo].[LedgerEntries]
                                ([TenantId], [TransactionId], [AccountId], [CurrencyId], [TalabKar], [BadehKar], [Description], [CreatedAt])
                            SELECT @TenantId, @ReverseTransactionId, [AccountId], [CurrencyId], [BadehKar], [TalabKar],
                                   N'برگشت: ' + COALESCE([Description], N''), @Now
                            FROM [dbo].[LedgerEntries]
                            WHERE [TenantId] = @TenantId AND [TransactionId] = @ReversePostingTransactionId;
                            UPDATE [dbo].[AedDealConversions]
                            SET [Status] = N'Reversed', [ReversalTransactionId] = @ReverseTransactionId,
                                [ReversalReason] = LTRIM(RTRIM(@Reason)), [ReversedAt] = @Now, [ReversedBy] = @CurrentUserId
                            WHERE [TenantId] = @TenantId AND [Id] = @ConversionId;
                            UPDATE [dbo].[AedDeals]
                            SET [ConvertedAmount] = [ConvertedAmount] - @ReverseSourceAmount,
                                [TotalFinalUsd] = [TotalFinalUsd] - @ReverseFinalUsd,
                                [TotalProfitUsd] = [TotalProfitUsd] - @ReverseProfitUsd,
                                [Status] = CASE WHEN [ConvertedAmount] - @ReverseSourceAmount <= 0
                                                THEN N'Held' ELSE N'PartiallyConverted' END
                            WHERE [TenantId] = @TenantId AND [Id] = @ReverseDealId;
                            UPDATE [dbo].[Transactions]
                            SET [Status] = N'Cancel', [CancelReason] = LTRIM(RTRIM(@Reason)),
                                [CancelledAt] = @Now, [CancelledBy] = @CurrentUserId
                            WHERE [TenantId] = @TenantId AND [Id] = @ReversePostingTransactionId;
                            INSERT INTO [dbo].[AuditLogs]
                                ([TenantId], [UserId], [ProcessId], [Action], [TableName], [RecordId], [NewValue], [CreatedAt])
                            VALUES
                                (@TenantId, @CurrentUserId, NEWID(), N'REVERSE_AED_CONVERSION', N'AedDealConversions', @ConversionId,
                                 N'تبدیل معامله ' + @ReverseDealNumber + N' برگشت داده شد.', @Now);
                            SET @ResultDealId = @ReverseDealId;
                        END
                        ELSE
                        BEGIN
                            IF @DealId <= 0 OR NULLIF(LTRIM(RTRIM(@Reason)), N'') IS NULL
                                THROW 50231, N'معامله و دلیل لغو معتبر الزامی است.', 1;
                            DECLARE @CancelLockResource nvarchar(255) = N'AedDeal:'
                                + CONVERT(nvarchar(20), @TenantId) + N':' + CONVERT(nvarchar(20), @DealId);
                            EXEC @LockResult = sp_getapplock
                                @Resource = @CancelLockResource, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0 THROW 50232, N'قفل لغو معامله دریافت نشد؛ دوباره تلاش کنید.', 1;
                            DECLARE @CancelStatus nvarchar(20);
                            DECLARE @HoldingTransactionId bigint;
                            DECLARE @CancelDealNumber nvarchar(50);
                            SELECT @CancelStatus = [Status], @HoldingTransactionId = [HoldingTransactionId],
                                   @CancelDealNumber = [DealNumber]
                            FROM [dbo].[AedDeals] WITH (UPDLOCK, HOLDLOCK)
                            WHERE [TenantId] = @TenantId AND [Id] = @DealId;
                            IF @CancelDealNumber IS NULL THROW 50233, N'معامله یافت نشد.', 1;
                            IF @CancelStatus = N'Cancelled' THROW 50234, N'معامله قبلاً لغو شده است.', 1;
                            IF EXISTS
                                (SELECT 1 FROM [dbo].[AedDealConversions] WITH (UPDLOCK, HOLDLOCK)
                                 WHERE [TenantId] = @TenantId AND [AedDealId] = @DealId AND [Status] = N'Posted')
                                THROW 50235, N'ابتدا تمام تبدیل‌های فعال این معامله را برگشت دهید.', 1;

                            DECLARE @CancelPrefix nvarchar(40) = N'AEDR-' + CONVERT(char(8), SYSDATETIME(), 112) + N'-';
                            DECLARE @CancelNumberLock nvarchar(255) = N'AedTransactionNumber:'
                                + CONVERT(nvarchar(20), @TenantId) + N':AEDR';
                            EXEC @LockResult = sp_getapplock
                                @Resource = @CancelNumberLock, @LockMode = N'Exclusive',
                                @LockOwner = N'Transaction', @LockTimeout = 30000;
                            IF @LockResult < 0 THROW 50236, N'ایجاد شماره سند ممکن نشد؛ دوباره تلاش کنید.', 1;
                            DECLARE @CancelNext int = COALESCE
                            (
                                (SELECT MAX(TRY_CONVERT(int, SUBSTRING([TransactionNo], LEN(@CancelPrefix) + 1, 20)))
                                 FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK)
                                 WHERE [TenantId] = @TenantId AND [TransactionNo] LIKE @CancelPrefix + N'%'), 0
                            ) + 1;
                            DECLARE @CancelTransactionNo nvarchar(50) = @CancelPrefix + FORMAT(@CancelNext, N'0000');
                            INSERT INTO [dbo].[Transactions]
                                ([TenantId], [TransactionNo], [TransactionType], [BranchId], [Status], [Remarks],
                                 [CreatedBy], [CreatedAt], [ReversedTransactionId])
                            VALUES
                                (@TenantId, @CancelTransactionNo, N'AedDealReversal', @BranchId, N'Paid',
                                 N'لغو معامله ' + @CancelDealNumber + N': ' + LTRIM(RTRIM(@Reason)),
                                 @CurrentUserId, @Now, @HoldingTransactionId);
                            DECLARE @CancelTransactionId bigint = SCOPE_IDENTITY();
                            INSERT INTO [dbo].[LedgerEntries]
                                ([TenantId], [TransactionId], [AccountId], [CurrencyId], [TalabKar], [BadehKar], [Description], [CreatedAt])
                            SELECT @TenantId, @CancelTransactionId, [AccountId], [CurrencyId], [BadehKar], [TalabKar],
                                   N'لغو: ' + COALESCE([Description], N''), @Now
                            FROM [dbo].[LedgerEntries]
                            WHERE [TenantId] = @TenantId AND [TransactionId] = @HoldingTransactionId;
                            UPDATE [dbo].[AedDeals]
                            SET [Status] = N'Cancelled', [ReversalTransactionId] = @CancelTransactionId,
                                [CancelReason] = LTRIM(RTRIM(@Reason)), [CancelledAt] = @Now, [CancelledBy] = @CurrentUserId
                            WHERE [TenantId] = @TenantId AND [Id] = @DealId;
                            UPDATE [dbo].[Transactions]
                            SET [Status] = N'Cancel', [CancelReason] = LTRIM(RTRIM(@Reason)),
                                [CancelledAt] = @Now, [CancelledBy] = @CurrentUserId
                            WHERE [TenantId] = @TenantId AND [Id] = @HoldingTransactionId;
                            INSERT INTO [dbo].[AuditLogs]
                                ([TenantId], [UserId], [ProcessId], [Action], [TableName], [RecordId], [NewValue], [CreatedAt])
                            VALUES
                                (@TenantId, @CurrentUserId, NEWID(), N'CANCEL_AED_DEAL', N'AedDeals', @DealId,
                                 N'معامله ' + @CancelDealNumber + N' لغو شد.', @Now);
                            SET @ResultDealId = @DealId;
                        END;

                        COMMIT TRANSACTION;
                        SELECT @ResultDealId AS [DealId];
                    END TRY
                    BEGIN CATCH
                        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                        THROW;
                    END CATCH
                END;
                """);

            migrationBuilder.Sql("""
                CREATE PROCEDURE [dbo].[usp_CreateAedDeal_v1]
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @DealNumber nvarchar(50),
                    @SourceCorrespondentId bigint,
                    @DubaiCorrespondentId bigint,
                    @SourceCurrencyId bigint,
                    @Amount decimal(18,4),
                    @RoundingDecimalPlaces int,
                    @Note nvarchar(500) = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    EXEC [dbo].[usp_ProcessAedDeal_v1]
                        @Operation = N'Create', @TenantId = @TenantId,
                        @CurrentUserId = @CurrentUserId, @DealNumber = @DealNumber,
                        @SourceCorrespondentId = @SourceCorrespondentId,
                        @DubaiCorrespondentId = @DubaiCorrespondentId,
                        @SourceCurrencyId = @SourceCurrencyId, @Amount = @Amount,
                        @RoundingDecimalPlaces = @RoundingDecimalPlaces, @Note = @Note;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE PROCEDURE [dbo].[usp_ConvertAedDeal_v1]
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @DealId bigint,
                    @Amount decimal(18,4),
                    @ActualMarker decimal(18,4),
                    @DeclaredMarker decimal(18,4),
                    @Note nvarchar(500) = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    EXEC [dbo].[usp_ProcessAedDeal_v1]
                        @Operation = N'Convert', @TenantId = @TenantId,
                        @CurrentUserId = @CurrentUserId, @DealId = @DealId,
                        @Amount = @Amount, @ActualMarker = @ActualMarker,
                        @DeclaredMarker = @DeclaredMarker, @Note = @Note;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE PROCEDURE [dbo].[usp_ReverseAedDealConversion_v1]
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @ConversionId bigint,
                    @Reason nvarchar(500)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    EXEC [dbo].[usp_ProcessAedDeal_v1]
                        @Operation = N'ReverseConversion', @TenantId = @TenantId,
                        @CurrentUserId = @CurrentUserId, @ConversionId = @ConversionId,
                        @Reason = @Reason;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE PROCEDURE [dbo].[usp_CancelAedDeal_v1]
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @DealId bigint,
                    @Reason nvarchar(500)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    EXEC [dbo].[usp_ProcessAedDeal_v1]
                        @Operation = N'Cancel', @TenantId = @TenantId,
                        @CurrentUserId = @CurrentUserId, @DealId = @DealId,
                        @Reason = @Reason;
                END;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_CancelAedDeal_v1];");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_ReverseAedDealConversion_v1];");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_ConvertAedDeal_v1];");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_CreateAedDeal_v1];");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_ProcessAedDeal_v1];");

        }
    }
}

