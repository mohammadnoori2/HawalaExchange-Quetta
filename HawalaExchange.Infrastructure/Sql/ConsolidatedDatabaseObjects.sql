CREATE PROCEDURE [dbo].[usp_GetHawalaStatistics_v1]
                    @TenantId bigint
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT
                        COALESCE(SUM(CASE WHEN [HawalaType] = N'HawalaSend' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [HawalaSendCount],
                        COALESCE(SUM(CASE WHEN [HawalaType] = N'HawalaReceive' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [HawalaReceiveCount],
                        COALESCE(SUM(CASE WHEN [HawalaType] = N'HawalaOther' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [HawalaOtherCount],
                        COALESCE(SUM(CASE WHEN [Status] = N'Pending' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [PendingCount],
                        COALESCE(SUM(CASE WHEN [Status] = N'Paid' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [PaidCount]
                    FROM [dbo].[Hawalas]
                    WHERE [TenantId] = @TenantId;
                END;
GO
CREATE PROCEDURE [dbo].[usp_GetDailyOperationalReport_v1]
                    @TenantId bigint,
                    @FromUtc datetime2,
                    @ToUtcExclusive datetime2,
                    @BranchId bigint = NULL,
                    @CurrencyId bigint = NULL,
                    @UtcOffsetMinutes int
                AS
                BEGIN
                    SET NOCOUNT ON;

                    ;WITH [Dates] AS
                    (
                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, @FromUtc) AS date) AS [ReportDate]
                        UNION ALL
                        SELECT DATEADD(day, 1, [ReportDate])
                        FROM [Dates]
                        WHERE [ReportDate] < DATEADD(day, -1,
                            CAST(DATEADD(minute, @UtcOffsetMinutes, @ToUtcExclusive) AS date))
                    ),
                    [FilteredTransactions] AS
                    (
                        SELECT [Id], [TransactionType], [CreatedAt]
                        FROM [dbo].[Transactions]
                        WHERE [TenantId] = @TenantId
                          AND [CreatedAt] >= @FromUtc
                          AND [CreatedAt] < @ToUtcExclusive
                          AND (@BranchId IS NULL OR [BranchId] = @BranchId)
                          AND [Status] <> N'Cancel'
                          AND (@CurrencyId IS NULL OR EXISTS
                              (SELECT 1 FROM [dbo].[TransactionDetails] fd
                               WHERE fd.[TenantId] = @TenantId AND fd.[TransactionId] = [Transactions].[Id]
                                 AND (fd.[FromCurrencyId] = @CurrencyId OR fd.[ToCurrencyId] = @CurrencyId
                                      OR fd.[CommissionCurrencyId] = @CurrencyId
                                      OR fd.[AgentCommissionCurrencyId] = @CurrencyId)))
                    ),
                    [FilteredHawalas] AS
                    (
                        SELECT h.*
                        FROM [dbo].[Hawalas] h
                        INNER JOIN [dbo].[Users] u
                            ON u.[TenantId] = @TenantId AND u.[Id] = h.[CreatedBy]
                        WHERE h.[TenantId] = @TenantId
                          AND h.[CreatedAt] >= @FromUtc
                          AND h.[CreatedAt] < @ToUtcExclusive
                          AND (@BranchId IS NULL OR u.[BranchId] = @BranchId)
                          AND h.[Status] <> N'Cancel'
                          AND (@CurrencyId IS NULL OR h.[FromCurrencyId] = @CurrencyId
                                                    OR h.[ToCurrencyId] = @CurrencyId
                                                    OR h.[CommissionCurrencyId] = @CurrencyId
                                                    OR h.[AgentCommissionCurrencyId] = @CurrencyId)
                    ),
                    [ActivityCounts] AS
                    (
                        SELECT [ReportDate], COUNT_BIG(*) AS [TotalTransactions]
                        FROM
                        (
                            SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, [CreatedAt]) AS date) AS [ReportDate]
                            FROM [FilteredTransactions]
                            UNION ALL
                            SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, [CreatedAt]) AS date)
                            FROM [FilteredHawalas]
                        ) a
                        GROUP BY [ReportDate]
                    ),
                    [TransactionAmounts] AS
                    (
                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, t.[CreatedAt]) AS date) AS [ReportDate],
                               SUM(CASE WHEN t.[TransactionType] = N'HawalaSend'
                                         AND (@CurrencyId IS NULL OR d.[FromCurrencyId] = @CurrencyId)
                                        THEN COALESCE(d.[FromAmount], 0) ELSE 0 END) AS [TotalSendAmount],
                               SUM(CASE WHEN t.[TransactionType] = N'HawalaReceive'
                                         AND (@CurrencyId IS NULL OR d.[ToCurrencyId] = @CurrencyId)
                                        THEN COALESCE(d.[ToAmount], 0) ELSE 0 END) AS [TotalReceiveAmount],
                               SUM(CASE WHEN @CurrencyId IS NULL OR d.[CommissionCurrencyId] = @CurrencyId
                                        THEN COALESCE(d.[CommissionAmount], 0) ELSE 0 END) AS [TotalCommission],
                               SUM(CASE WHEN @CurrencyId IS NULL OR d.[AgentCommissionCurrencyId] = @CurrencyId
                                        THEN COALESCE(d.[AgentCommissionAmount], 0) ELSE 0 END) AS [TotalAgentCommission]
                        FROM [FilteredTransactions] t
                        INNER JOIN [dbo].[TransactionDetails] d
                            ON d.[TenantId] = @TenantId AND d.[TransactionId] = t.[Id]
                        GROUP BY CAST(DATEADD(minute, @UtcOffsetMinutes, t.[CreatedAt]) AS date)
                    ),
                    [HawalaAmounts] AS
                    (
                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, [CreatedAt]) AS date) AS [ReportDate],
                               SUM(CASE WHEN [HawalaType] = N'HawalaSend'
                                         AND (@CurrencyId IS NULL OR [FromCurrencyId] = @CurrencyId)
                                        THEN [FromAmount] ELSE 0 END) AS [TotalSendAmount],
                               SUM(CASE WHEN [HawalaType] = N'HawalaReceive'
                                         AND (@CurrencyId IS NULL OR [ToCurrencyId] = @CurrencyId)
                                        THEN COALESCE([ToAmount], [FromAmount]) ELSE 0 END) AS [TotalReceiveAmount],
                               SUM(CASE WHEN @CurrencyId IS NULL OR [CommissionCurrencyId] = @CurrencyId
                                        THEN COALESCE([CommissionAmount], 0) ELSE 0 END) AS [TotalCommission],
                               SUM(CASE WHEN @CurrencyId IS NULL OR [AgentCommissionCurrencyId] = @CurrencyId
                                        THEN COALESCE([AgentCommissionAmount], 0) ELSE 0 END) AS [TotalAgentCommission]
                        FROM [FilteredHawalas]
                        GROUP BY CAST(DATEADD(minute, @UtcOffsetMinutes, [CreatedAt]) AS date)
                    ),
                    [PeriodicCommissionAmounts] AS
                    (
                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, b.[CreatedAt]) AS date) AS [ReportDate],
                               SUM(b.[TotalCommissionUsd]) AS [TotalCommission]
                        FROM [dbo].[CorrespondentCommissionBatches] b
                        WHERE b.[TenantId] = @TenantId
                          AND b.[CreatedAt] >= @FromUtc
                          AND b.[CreatedAt] < @ToUtcExclusive
                          AND b.[Status] = N'Posted'
                          AND @BranchId IS NULL
                          AND (@CurrencyId IS NULL OR @CurrencyId =
                              (SELECT TOP (1) [Id] FROM [dbo].[Currencies]
                               WHERE [TenantId] = @TenantId AND [Code] = N'USD'))
                        GROUP BY CAST(DATEADD(minute, @UtcOffsetMinutes, b.[CreatedAt]) AS date)
                    ),
                    [ExpenseAmounts] AS
                    (
                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, [ExpenseDate]) AS date) AS [ReportDate],
                               SUM([Amount]) AS [TotalExpenses]
                        FROM [dbo].[Expenses]
                        WHERE [TenantId] = @TenantId
                          AND [IsDeleted] = 0
                          AND [ExpenseDate] >= @FromUtc
                          AND [ExpenseDate] < @ToUtcExclusive
                          AND (@CurrencyId IS NULL OR [CurrencyId] = @CurrencyId)
                        GROUP BY CAST(DATEADD(minute, @UtcOffsetMinutes, [ExpenseDate]) AS date)
                    )
                    SELECT CAST(d.[ReportDate] AS datetime2) AS [ReportDate],
                           COALESCE(@BranchId, CAST(0 AS bigint)) AS [BranchId],
                           CASE WHEN @BranchId IS NULL THEN N'همه شعبه‌ها'
                                ELSE COALESCE((SELECT TOP (1) [Name]
                                               FROM [dbo].[Branches]
                                               WHERE [TenantId] = @TenantId AND [Id] = @BranchId), N'شعبه') END AS [BranchName],
                           @CurrencyId AS [CurrencyId],
                           CASE WHEN @CurrencyId IS NULL THEN NULL
                                ELSE (SELECT TOP (1) [Code] FROM [dbo].[Currencies]
                                      WHERE [TenantId] = @TenantId AND [Id] = @CurrencyId) END AS [CurrencyCode],
                           CAST(COALESCE(ac.[TotalTransactions], 0) AS int) AS [TotalTransactions],
                           CAST(COALESCE(ta.[TotalSendAmount], 0) + COALESCE(ha.[TotalSendAmount], 0) AS decimal(18,4)) AS [TotalSendAmount],
                           CAST(COALESCE(ta.[TotalReceiveAmount], 0) + COALESCE(ha.[TotalReceiveAmount], 0) AS decimal(18,4)) AS [TotalReceiveAmount],
                           CAST(COALESCE(ta.[TotalCommission], 0) + COALESCE(ha.[TotalCommission], 0)
                                + COALESCE(pc.[TotalCommission], 0) AS decimal(18,4)) AS [TotalCommission],
                           CAST(COALESCE(ta.[TotalAgentCommission], 0) + COALESCE(ha.[TotalAgentCommission], 0) AS decimal(18,4)) AS [TotalAgentCommission],
                           CAST(COALESCE(ea.[TotalExpenses], 0) AS decimal(18,4)) AS [TotalExpenses],
                           CAST(COALESCE(ta.[TotalCommission], 0) + COALESCE(ha.[TotalCommission], 0)
                                + COALESCE(pc.[TotalCommission], 0)
                                - COALESCE(ta.[TotalAgentCommission], 0) - COALESCE(ha.[TotalAgentCommission], 0)
                                - COALESCE(ea.[TotalExpenses], 0)
                               AS decimal(18,4)) AS [NetIncome]
                    FROM [Dates] d
                    LEFT JOIN [ActivityCounts] ac ON ac.[ReportDate] = d.[ReportDate]
                    LEFT JOIN [TransactionAmounts] ta ON ta.[ReportDate] = d.[ReportDate]
                    LEFT JOIN [HawalaAmounts] ha ON ha.[ReportDate] = d.[ReportDate]
                    LEFT JOIN [PeriodicCommissionAmounts] pc ON pc.[ReportDate] = d.[ReportDate]
                    LEFT JOIN [ExpenseAmounts] ea ON ea.[ReportDate] = d.[ReportDate]
                    ORDER BY d.[ReportDate]
                    OPTION (MAXRECURSION 32767);
                END;
GO
CREATE PROCEDURE [dbo].[usp_GetTransactionReport_v1]
                    @TenantId bigint,
                    @FromUtc datetime2,
                    @ToUtcExclusive datetime2,
                    @BranchId bigint = NULL,
                    @CurrencyId bigint = NULL,
                    @Status nvarchar(20) = NULL,
                    @CustomerId bigint = NULL,
                    @CorrespondentId bigint = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;

                    ;WITH [ReportRows] AS
                    (
                        SELECT t.[TransactionNo],
                               t.[TransactionType],
                               t.[CreatedAt],
                               COALESCE(NULLIF(t.[CustomerFullName], N''), N'N/A') AS [CustomerName],
                               COALESCE(NULLIF(d.[SenderName], N''), N'N/A') AS [SenderName],
                               COALESCE(NULLIF(d.[ReceiverName], N''), N'N/A') AS [ReceiverName],
                               COALESCE(fc.[Code], N'N/A') AS [FromCurrency],
                               CAST(COALESCE(d.[FromAmount], 0) AS decimal(18,4)) AS [FromAmount],
                               COALESCE(tc.[Code], N'N/A') AS [ToCurrency],
                               CAST(COALESCE(d.[ToAmount], 0) AS decimal(18,4)) AS [ToAmount],
                               CAST(COALESCE(d.[CommissionAmount], 0) AS decimal(18,4)) AS [Commission],
                               COALESCE(cc.[Code], N'N/A') AS [CommissionCurrency],
                               t.[Status],
                               t.[Id] AS [RowId]
                        FROM [dbo].[Transactions] t
                        OUTER APPLY
                        (
                            SELECT TOP (1) td.*
                            FROM [dbo].[TransactionDetails] td
                            WHERE td.[TenantId] = @TenantId
                              AND td.[TransactionId] = t.[Id]
                              AND (@CorrespondentId IS NULL OR td.[CorrespondentId] = @CorrespondentId)
                            ORDER BY td.[Id]
                        ) d
                        LEFT JOIN [dbo].[Currencies] fc
                            ON fc.[TenantId] = @TenantId AND fc.[Id] = d.[FromCurrencyId]
                        LEFT JOIN [dbo].[Currencies] tc
                            ON tc.[TenantId] = @TenantId AND tc.[Id] = d.[ToCurrencyId]
                        LEFT JOIN [dbo].[Currencies] cc
                            ON cc.[TenantId] = @TenantId AND cc.[Id] = d.[CommissionCurrencyId]
                        WHERE t.[TenantId] = @TenantId
                          AND t.[CreatedAt] >= @FromUtc
                          AND t.[CreatedAt] < @ToUtcExclusive
                          AND (@BranchId IS NULL OR t.[BranchId] = @BranchId)
                          AND (@Status IS NULL OR t.[Status] = @Status)
                          AND (@CustomerId IS NULL OR t.[CustomerId] = @CustomerId)
                          AND (@CorrespondentId IS NULL OR d.[Id] IS NOT NULL)
                          AND (@CurrencyId IS NULL OR d.[FromCurrencyId] = @CurrencyId
                                                    OR d.[ToCurrencyId] = @CurrencyId
                                                    OR d.[CommissionCurrencyId] = @CurrencyId
                                                    OR d.[AgentCommissionCurrencyId] = @CurrencyId)

                        UNION ALL

                        SELECT CONVERT(nvarchar(50), h.[Number]),
                               h.[HawalaType],
                               h.[CreatedAt],
                               N'N/A',
                               COALESCE(NULLIF(h.[SenderName], N''), N'N/A'),
                               COALESCE(NULLIF(h.[ReceiverName], N''), N'N/A'),
                               COALESCE(hfc.[Code], N'N/A'),
                               CAST(h.[FromAmount] AS decimal(18,4)),
                               COALESCE(htc.[Code], N'N/A'),
                               CAST(COALESCE(h.[ToAmount], h.[FromAmount]) AS decimal(18,4)),
                               CAST(COALESCE(h.[CommissionAmount], 0) AS decimal(18,4)),
                               COALESCE(hcc.[Code], N'N/A'),
                               h.[Status],
                               h.[Id]
                        FROM [dbo].[Hawalas] h
                        INNER JOIN [dbo].[Users] u
                            ON u.[TenantId] = @TenantId AND u.[Id] = h.[CreatedBy]
                        LEFT JOIN [dbo].[Currencies] hfc
                            ON hfc.[TenantId] = @TenantId AND hfc.[Id] = h.[FromCurrencyId]
                        LEFT JOIN [dbo].[Currencies] htc
                            ON htc.[TenantId] = @TenantId AND htc.[Id] = h.[ToCurrencyId]
                        LEFT JOIN [dbo].[Currencies] hcc
                            ON hcc.[TenantId] = @TenantId AND hcc.[Id] = h.[CommissionCurrencyId]
                        WHERE h.[TenantId] = @TenantId
                          AND h.[CreatedAt] >= @FromUtc
                          AND h.[CreatedAt] < @ToUtcExclusive
                          AND (@BranchId IS NULL OR u.[BranchId] = @BranchId)
                          AND (@Status IS NULL OR h.[Status] = @Status)
                          AND @CustomerId IS NULL
                          AND (@CorrespondentId IS NULL OR h.[CorrespondentId] = @CorrespondentId)
                          AND (@CurrencyId IS NULL OR h.[FromCurrencyId] = @CurrencyId
                                                    OR h.[ToCurrencyId] = @CurrencyId
                                                    OR h.[CommissionCurrencyId] = @CurrencyId
                                                    OR h.[AgentCommissionCurrencyId] = @CurrencyId)
                    )
                    SELECT [TransactionNo], [TransactionType], [CreatedAt], [CustomerName],
                           [SenderName], [ReceiverName], [FromCurrency], [FromAmount],
                           [ToCurrency], [ToAmount], [Commission], [CommissionCurrency], [Status]
                    FROM [ReportRows]
                    ORDER BY [CreatedAt] DESC, [RowId] DESC;
                END;
GO
CREATE PROCEDURE [dbo].[usp_GetCommissionReport_v1]
                    @TenantId bigint,
                    @FromUtc datetime2,
                    @ToUtcExclusive datetime2,
                    @BranchId bigint = NULL,
                    @CurrencyId bigint = NULL,
                    @UtcOffsetMinutes int
                AS
                BEGIN
                    SET NOCOUNT ON;

                    ;WITH [Dates] AS
                    (
                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, @FromUtc) AS date) AS [ReportDate]
                        UNION ALL
                        SELECT DATEADD(day, 1, [ReportDate])
                        FROM [Dates]
                        WHERE [ReportDate] < DATEADD(day, -1,
                            CAST(DATEADD(minute, @UtcOffsetMinutes, @ToUtcExclusive) AS date))
                    ),
                    [CommissionRows] AS
                    (
                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, t.[CreatedAt]) AS date) AS [ReportDate],
                               CASE WHEN @CurrencyId IS NULL OR d.[CommissionCurrencyId] = @CurrencyId
                                    THEN COALESCE(d.[CommissionAmount], 0) ELSE 0 END AS [CommissionAmount],
                               CASE WHEN @CurrencyId IS NULL OR d.[AgentCommissionCurrencyId] = @CurrencyId
                                    THEN COALESCE(d.[AgentCommissionAmount], 0) ELSE 0 END AS [AgentCommissionAmount]
                        FROM [dbo].[Transactions] t
                        INNER JOIN [dbo].[TransactionDetails] d
                            ON d.[TenantId] = @TenantId AND d.[TransactionId] = t.[Id]
                        WHERE t.[TenantId] = @TenantId
                          AND t.[CreatedAt] >= @FromUtc
                          AND t.[CreatedAt] < @ToUtcExclusive
                          AND (@BranchId IS NULL OR t.[BranchId] = @BranchId)
                          AND t.[Status] <> N'Cancel'

                        UNION ALL

                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, h.[CreatedAt]) AS date),
                               CASE WHEN @CurrencyId IS NULL OR h.[CommissionCurrencyId] = @CurrencyId
                                    THEN COALESCE(h.[CommissionAmount], 0) ELSE 0 END,
                               CASE WHEN @CurrencyId IS NULL OR h.[AgentCommissionCurrencyId] = @CurrencyId
                                    THEN COALESCE(h.[AgentCommissionAmount], 0) ELSE 0 END
                        FROM [dbo].[Hawalas] h
                        INNER JOIN [dbo].[Users] u
                            ON u.[TenantId] = @TenantId AND u.[Id] = h.[CreatedBy]
                        WHERE h.[TenantId] = @TenantId
                          AND h.[CreatedAt] >= @FromUtc
                          AND h.[CreatedAt] < @ToUtcExclusive
                          AND (@BranchId IS NULL OR u.[BranchId] = @BranchId)
                          AND h.[Status] <> N'Cancel'

                        UNION ALL

                        SELECT CAST(DATEADD(minute, @UtcOffsetMinutes, b.[CreatedAt]) AS date),
                               b.[TotalCommissionUsd],
                               CAST(0 AS decimal(18,4))
                        FROM [dbo].[CorrespondentCommissionBatches] b
                        WHERE b.[TenantId] = @TenantId
                          AND b.[CreatedAt] >= @FromUtc
                          AND b.[CreatedAt] < @ToUtcExclusive
                          AND b.[Status] = N'Posted'
                          AND @BranchId IS NULL
                          AND (@CurrencyId IS NULL OR @CurrencyId =
                              (SELECT TOP (1) [Id] FROM [dbo].[Currencies]
                               WHERE [TenantId] = @TenantId AND [Code] = N'USD'))
                    ),
                    [CommissionAmounts] AS
                    (
                        SELECT [ReportDate],
                               SUM([CommissionAmount]) AS [TotalCommission],
                               SUM([AgentCommissionAmount]) AS [TotalAgentCommission]
                        FROM [CommissionRows]
                        GROUP BY [ReportDate]
                    )
                    SELECT CAST(d.[ReportDate] AS datetime2) AS [ReportDate],
                           COALESCE(@BranchId, CAST(0 AS bigint)) AS [BranchId],
                           CASE WHEN @BranchId IS NULL THEN N'همه شعبه‌ها'
                                ELSE COALESCE((SELECT TOP (1) [Name]
                                               FROM [dbo].[Branches]
                                               WHERE [TenantId] = @TenantId AND [Id] = @BranchId), N'شعبه') END AS [BranchName],
                           @CurrencyId AS [CurrencyId],
                           CASE WHEN @CurrencyId IS NULL THEN NULL
                                ELSE (SELECT TOP (1) [Code] FROM [dbo].[Currencies]
                                      WHERE [TenantId] = @TenantId AND [Id] = @CurrencyId) END AS [CurrencyCode],
                           CAST(COALESCE(ca.[TotalCommission], 0) AS decimal(18,4)) AS [TotalCommission],
                           CAST(COALESCE(ca.[TotalAgentCommission], 0) AS decimal(18,4)) AS [TotalAgentCommission],
                           CAST(COALESCE(ca.[TotalCommission], 0) - COALESCE(ca.[TotalAgentCommission], 0)
                               AS decimal(18,4)) AS [NetCommission]
                    FROM [Dates] d
                    LEFT JOIN [CommissionAmounts] ca ON ca.[ReportDate] = d.[ReportDate]
                    ORDER BY d.[ReportDate]
                    OPTION (MAXRECURSION 32767);
                END;
GO
CREATE TYPE [dbo].[CommissionRateTableType_v1] AS TABLE
                (
                    [CurrencyId] bigint NOT NULL PRIMARY KEY,
                    [SourceToAfnRate] decimal(18,8) NOT NULL
                );
GO
CREATE PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]
                    @Mode nvarchar(10),
                    @TenantId bigint,
                    @CurrentUserId bigint,
                    @CorrespondentId bigint,
                    @HawalaType nvarchar(20),
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
                    IF @HawalaType NOT IN (N'HawalaReceive', N'HawalaSend')
                        THROW 50015, N'نوع حواله برای محاسبه کمیشن معتبر نیست.', 1;
                    IF @PeriodTo < @PeriodFrom
                        THROW 50002, N'تاریخ پایان نمی‌تواند قبل از تاریخ آغاز باشد.', 1;
                    IF @CommissionPerLakhAfn <= 0
                        THROW 50003, N'کمیشن هر لک باید بزرگ‌تر از صفر باشد.', 1;
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
                        DECLARE @UsdCurrencyId bigint =
                        (
                            SELECT TOP (1) [Id] FROM [dbo].[Currencies]
                            WHERE [TenantId] = @TenantId AND [Code] = N'USD' AND [IsActive] = 1
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
                            [CommissionAfn] decimal(38,8) NOT NULL,
                            [SourceCorrespondentId] bigint NULL
                        );

                        INSERT INTO #Eligible
                        (
                            [HawalaId], [HawalaNumber], [HawalaDate], [CurrencyId],
                            [CurrencyCode], [SourceAmount], [SourceToAfnRate],
                            [AfnEquivalent], [CommissionAfn], [SourceCorrespondentId]
                        )
                        SELECT h.[Id], h.[Number], h.[CreatedAt], currencyData.[CurrencyId], c.[Code],
                               CAST(currencyData.[SourceAmount] AS decimal(18,4)),
                               CAST(CASE WHEN currencyData.[CurrencyId] = @UsdCurrencyId THEN 1
                                         ELSE h.[CommissionUsdToAfnRate] END AS decimal(18,8)),
                               CAST(CASE WHEN @HawalaType = N'HawalaReceive'
                                         THEN h.[CommissionBaseUsdAmount]
                                         WHEN currencyData.[CurrencyId] = @UsdCurrencyId
                                         THEN ROUND(currencyData.[SourceAmount] / 100000 * @CommissionPerLakhAfn, 0)
                                         ELSE ROUND(ROUND(currencyData.[SourceAmount] / 100000 * @CommissionPerLakhAfn, 0)
                                                    / h.[CommissionUsdToAfnRate], 0) END AS decimal(38,8)),
                               CAST(CASE WHEN @HawalaType = N'HawalaReceive'
                                         THEN h.[CommissionBaseUsdAmount] / 100000 * @CommissionPerLakhAfn
                                         ELSE ROUND(currencyData.[SourceAmount] / 100000 * @CommissionPerLakhAfn, 0)
                                    END AS decimal(38,8)),
                               sourceHawala.[CorrespondentId]
                        FROM [dbo].[Hawalas] h WITH (UPDLOCK, HOLDLOCK)
                        CROSS APPLY
                        (
                            SELECT CASE WHEN @HawalaType = N'HawalaSend' THEN h.[ToCurrencyId]
                                        ELSE h.[FromCurrencyId] END AS [CurrencyId],
                                   CASE WHEN @HawalaType = N'HawalaSend' THEN COALESCE(h.[ToAmount], h.[FromAmount])
                                        ELSE h.[FromAmount] END AS [SourceAmount]
                        ) currencyData
                        INNER JOIN [dbo].[Currencies] c
                            ON c.[TenantId] = @TenantId AND c.[Id] = currencyData.[CurrencyId]
                        LEFT JOIN [dbo].[Hawalas] sourceHawala
                            ON sourceHawala.[TenantId] = @TenantId AND sourceHawala.[Id] = h.[SourceHawalaId]
                        WHERE h.[TenantId] = @TenantId
                          AND h.[CorrespondentId] = @CorrespondentId
                          AND h.[HawalaType] = @HawalaType
                          AND h.[Status] <> N'Cancel'
                          AND h.[CreatedAt] >= @FromUtc
                          AND h.[CreatedAt] < @ToUtcExclusive
                          AND ((@HawalaType = N'HawalaReceive' AND (h.[CommissionAmount] IS NULL OR h.[CommissionAmount] = 0))
                               OR (@HawalaType = N'HawalaSend' AND (h.[AgentCommissionAmount] IS NULL OR h.[AgentCommissionAmount] = 0)))
                          AND currencyData.[CurrencyId] IN (@AfnCurrencyId, @UsdCurrencyId)
                          AND h.[CommissionBaseUsdAmount] IS NOT NULL
                          AND NOT EXISTS
                          (
                              SELECT 1 FROM [dbo].[CorrespondentCommissionBatchItems] bi WITH (UPDLOCK, HOLDLOCK)
                              WHERE bi.[TenantId] = @TenantId AND bi.[HawalaId] = h.[Id] AND bi.[IsActive] = 1
                          );

                        IF @HawalaType = N'HawalaSend' AND
                           EXISTS (SELECT 1 FROM #Eligible WHERE [SourceToAfnRate] <= 0)
                        BEGIN
                            DECLARE @MissingCurrencies nvarchar(2000) =
                            (
                                SELECT STRING_AGG([CurrencyCode], N'، ')
                                FROM (SELECT DISTINCT [CurrencyCode] FROM #Eligible WHERE [SourceToAfnRate] <= 0) missing
                            );
                            DECLARE @MissingRateMessage nvarchar(2048) =
                                N'نرخ پایان روز برای ' + @MissingCurrencies + N' در روزنامچه ثبت نشده است.';
                            THROW 50007, @MissingRateMessage, 1;
                        END;

                        IF @HawalaType = N'HawalaSend' AND
                           EXISTS (SELECT 1 FROM #Eligible WHERE [SourceCorrespondentId] IS NULL)
                            THROW 50017, N'حواله ارسالی به حواله دریافتی و نمایندگی فرستنده مرتبط نیست.', 1;

                        DECLARE @HawalaCount int = (SELECT COUNT(*) FROM #Eligible);
                        DECLARE @TotalBaseAfn decimal(18,4) =
                            CAST(COALESCE((SELECT SUM([AfnEquivalent]) FROM #Eligible), 0) AS decimal(18,4));
                        DECLARE @CalculatedCommission decimal(18,4) =
                            CAST(ROUND(@TotalBaseAfn / 100000 * @CommissionPerLakhAfn, 0) AS decimal(18,4));
                        DECLARE @TotalCommissionAfn decimal(18,4) =
                            CASE WHEN @HawalaType = N'HawalaSend'
                                 THEN CAST(COALESCE((SELECT SUM([CommissionAfn]) FROM #Eligible WHERE [CurrencyId] = @AfnCurrencyId), 0) AS decimal(18,4))
                                 ELSE 0 END;
                        DECLARE @TotalCommissionUsd decimal(18,4) =
                            CASE WHEN @HawalaType = N'HawalaReceive' THEN @CalculatedCommission
                                 ELSE CAST(COALESCE((SELECT SUM([CommissionAfn]) FROM #Eligible WHERE [CurrencyId] = @UsdCurrencyId), 0) AS decimal(18,4)) END;
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
                        IF (@HawalaType = N'HawalaReceive' AND @TotalCommissionUsd <= 0) OR
                           (@HawalaType = N'HawalaSend' AND @TotalBaseAfn <= 0)
                            THROW 50009, N'کمیشن نهایی پس از گردکردن قابل ثبت نیست.', 1;

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

                        DECLARE @ClearingAccountId bigint;
                        IF @HawalaType = N'HawalaSend'
                        BEGIN
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
                                  AND ([IsArchived] = 1 OR [AccountType] <> N'CurrencyConversionClearing')
                            )
                                THROW 50016, N'حساب واسط تبدیل ارز باید فعال و از نوع صحیح باشد.', 1;

                            IF EXISTS
                            (
                                SELECT 1
                                FROM #Eligible e
                                WHERE NOT EXISTS
                                (
                                    SELECT 1 FROM [dbo].[Accounts] a
                                    WHERE a.[TenantId] = @TenantId
                                      AND a.[CorrespondentId] = e.[SourceCorrespondentId]
                                      AND a.[IsArchived] = 0
                                )
                            )
                                THROW 50018, N'حساب فعال نمایندگی فرستنده حواله یافت نشد.', 1;
                        END;

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
                        DECLARE @Remarks nvarchar(1000) =
                            CASE WHEN @HawalaType = N'HawalaSend' THEN N'کمیشن دوره‌ای حواله‌های ارسالی نمایندگی '
                                 ELSE N'کمیشن دوره‌ای حواله‌های دریافتی نمایندگی ' END + @CorrespondentName
                            + N' از ' + CONVERT(nvarchar(10), @PeriodFrom, 23)
                            + N' تا ' + CONVERT(nvarchar(10), @PeriodTo, 23);

                        INSERT INTO [dbo].[Transactions]
                            ([TenantId], [TransactionNo], [TransactionType], [BranchId], [Status],
                             [Remarks], [CreatedBy], [CreatedAt])
                        VALUES
                            (@TenantId, @TransactionNo,
                             CASE WHEN @HawalaType = N'HawalaSend' THEN N'PeriodicOutgoingCommission'
                                  ELSE N'PeriodicCorrespondentCommission' END, @BranchId,
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

                        DECLARE @Description nvarchar(500) =
                            CASE WHEN @HawalaType = N'HawalaSend' THEN N'کمیشن حواله‌های ارسالی نمایندگی '
                                 ELSE N'کمیشن حواله‌های دریافتی نمایندگی ' END
                            + @CorrespondentName + N'، ' + CONVERT(nvarchar(20), @HawalaCount) + N' حواله';
                        DECLARE @PostingAmount decimal(18,4) =
                            CASE WHEN @HawalaType = N'HawalaSend' THEN @TotalBaseAfn ELSE @TotalCommissionUsd END;

                        IF @HawalaType = N'HawalaReceive'
                        BEGIN
                            INSERT INTO [dbo].[LedgerEntries]
                                ([TenantId], [TransactionId], [AccountId], [CurrencyId],
                                 [TalabKar], [BadehKar], [Description], [CreatedAt])
                            VALUES
                                (@TenantId, @TransactionId, @CorrespondentAccountId, @UsdCurrencyId,
                                 0, @TotalCommissionUsd, @Description, @Now),
                                (@TenantId, @TransactionId, @IncomeAccountId, @UsdCurrencyId,
                                 @TotalCommissionUsd, 0, @Description, @Now);
                        END
                        ELSE
                        BEGIN
                            IF @TotalCommissionUsd > 0
                                INSERT INTO [dbo].[LedgerEntries]
                                    ([TenantId], [TransactionId], [AccountId], [CurrencyId],
                                     [TalabKar], [BadehKar], [Description], [CreatedAt])
                                VALUES
                                    (@TenantId, @TransactionId, @CorrespondentAccountId, @UsdCurrencyId,
                                     @TotalCommissionUsd, 0, @Description, @Now);

                            IF @TotalCommissionAfn > 0
                            BEGIN
                                INSERT INTO [dbo].[LedgerEntries]
                                    ([TenantId], [TransactionId], [AccountId], [CurrencyId],
                                     [TalabKar], [BadehKar], [Description], [CreatedAt])
                                VALUES
                                    (@TenantId, @TransactionId, @CorrespondentAccountId, @AfnCurrencyId,
                                     @TotalCommissionAfn, 0, @Description, @Now),
                                    (@TenantId, @TransactionId, @ClearingAccountId, @AfnCurrencyId,
                                     0, @TotalCommissionAfn, @Description, @Now);

                                INSERT INTO [dbo].[LedgerEntries]
                                    ([TenantId], [TransactionId], [AccountId], [CurrencyId],
                                     [TalabKar], [BadehKar], [Description], [CreatedAt])
                                SELECT @TenantId, @TransactionId, @ClearingAccountId, @UsdCurrencyId,
                                       CAST(SUM([AfnEquivalent]) AS decimal(18,4)), 0, @Description, @Now
                                FROM #Eligible WHERE [CurrencyId] = @AfnCurrencyId;
                            END;

                            INSERT INTO [dbo].[LedgerEntries]
                                ([TenantId], [TransactionId], [AccountId], [CurrencyId],
                                 [TalabKar], [BadehKar], [Description], [CreatedAt])
                            SELECT @TenantId, @TransactionId, a.[Id], @UsdCurrencyId,
                                   0, CAST(SUM(e.[AfnEquivalent]) AS decimal(18,4)),
                                   @Description, @Now
                            FROM #Eligible e
                            CROSS APPLY
                            (
                                SELECT TOP (1) account.[Id]
                                FROM [dbo].[Accounts] account
                                WHERE account.[TenantId] = @TenantId
                                  AND account.[CorrespondentId] = e.[SourceCorrespondentId]
                                  AND account.[IsArchived] = 0
                                ORDER BY account.[Id]
                            ) a
                            GROUP BY a.[Id];
                        END;

                        INSERT INTO [dbo].[AuditLogs]
                            ([TenantId], [UserId], [ProcessId], [Action], [TableName], [RecordId],
                             [NewValue], [CreatedAt])
                        VALUES
                            (@TenantId, @CurrentUserId, NEWID(), N'POST_PERIODIC_COMMISSION',
                             N'CorrespondentCommissionBatches', @BatchId,
                             N'کمیشن ' + CONVERT(nvarchar(20), @HawalaCount) + N' حواله به مبلغ '
                                 + CONVERT(nvarchar(50), @PostingAmount)
                                 + N' USD'
                                 + N' ثبت شد.', @Now);

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
GO
CREATE TYPE [dbo].[IdTableType_v1] AS TABLE
                (
                    [Id] bigint NOT NULL PRIMARY KEY
                );
GO
CREATE TYPE [dbo].[SettlementRateTableType_v1] AS TABLE
                (
                    [HawalaId] bigint NOT NULL,
                    [SourceCurrencyId] bigint NOT NULL,
                    [Rate] decimal(18,8) NOT NULL,
                    PRIMARY KEY ([HawalaId], [SourceCurrencyId])
                );
GO
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_GetAccountBalances_v1]
                    @TenantId bigint,
                    @AccountId bigint = NULL,
                    @CustomerId bigint = NULL,
                    @CorrespondentId bigint = NULL,
                    @OwnerType nvarchar(20) = NULL,
                    @AccountType nvarchar(50) = NULL,
                    @AsOfDate datetime2(7) = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF @TenantId <= 0
                        THROW 51000, 'A valid TenantId is required.', 1;

                    IF @OwnerType IS NOT NULL AND @OwnerType NOT IN (N'Customer', N'Correspondent')
                        THROW 51001, 'OwnerType must be Customer or Correspondent.', 1;

                    -- Current balances are maintained transactionally from LedgerEntries.
                    -- Historical/as-of queries continue to use the immutable ledger below.
                    IF @AsOfDate IS NULL
                    BEGIN
                        SELECT
                            account.[Id] AS [AccountId],
                            account.[AccountName],
                            account.[AccountType],
                            account.[CustomerId],
                            account.[CorrespondentId],
                            balance.[CurrencyId],
                            currency.[Code] AS [CurrencyCode],
                            CAST(balance.[Balance] AS decimal(18, 2)) AS [Balance]
                        FROM [dbo].[AccountCurrencyBalances] balance
                        INNER JOIN [dbo].[Accounts] account
                            ON account.[TenantId] = balance.[TenantId]
                           AND account.[Id] = balance.[AccountId]
                        INNER JOIN [dbo].[Currencies] currency
                            ON currency.[TenantId] = balance.[TenantId]
                           AND currency.[Id] = balance.[CurrencyId]
                        WHERE account.[TenantId] = @TenantId
                          AND balance.[Balance] <> 0
                          AND (@AccountId IS NULL OR account.[Id] = @AccountId)
                          AND (@CustomerId IS NULL OR account.[CustomerId] = @CustomerId)
                          AND (@CorrespondentId IS NULL OR account.[CorrespondentId] = @CorrespondentId)
                          AND (@AccountType IS NULL OR account.[AccountType] = @AccountType)
                          AND (@OwnerType IS NULL
                               OR (@OwnerType = N'Customer' AND account.[CustomerId] IS NOT NULL)
                               OR (@OwnerType = N'Correspondent' AND account.[CorrespondentId] IS NOT NULL))
                        ORDER BY account.[Id], currency.[Code];
                        RETURN;
                    END;

                    SELECT
                        account.[Id] AS [AccountId],
                        account.[AccountName],
                        account.[AccountType],
                        account.[CustomerId],
                        account.[CorrespondentId],
                        entry.[CurrencyId],
                        currency.[Code] AS [CurrencyCode],
                        CAST(SUM(entry.[TalabKar] - entry.[BadehKar]) AS decimal(18, 2)) AS [Balance]
                    FROM [dbo].[Accounts] account
                    INNER JOIN [dbo].[LedgerEntries] entry
                        ON entry.[TenantId] = account.[TenantId]
                       AND entry.[AccountId] = account.[Id]
                    INNER JOIN [dbo].[Currencies] currency
                        ON currency.[TenantId] = entry.[TenantId]
                       AND currency.[Id] = entry.[CurrencyId]
                    WHERE account.[TenantId] = @TenantId
                      AND (@AccountId IS NULL OR account.[Id] = @AccountId)
                      AND (@CustomerId IS NULL OR account.[CustomerId] = @CustomerId)
                      AND (@CorrespondentId IS NULL OR account.[CorrespondentId] = @CorrespondentId)
                      AND (@AccountType IS NULL OR account.[AccountType] = @AccountType)
                      AND (@OwnerType IS NULL
                           OR (@OwnerType = N'Customer' AND account.[CustomerId] IS NOT NULL)
                           OR (@OwnerType = N'Correspondent' AND account.[CorrespondentId] IS NOT NULL))
                      AND (@AsOfDate IS NULL OR entry.[CreatedAt] <= @AsOfDate)
                    GROUP BY account.[Id], account.[AccountName], account.[AccountType],
                             account.[CustomerId], account.[CorrespondentId],
                             entry.[CurrencyId], currency.[Code]
                    HAVING SUM(entry.[TalabKar] - entry.[BadehKar]) <> 0
                    ORDER BY account.[Id], currency.[Code];
                END;
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_GetDashboardData_v1]
                @TenantId bigint,
                @FromDate datetime2,
                @ToDateExclusive datetime2
            AS
            BEGIN
                SET NOCOUNT ON;

                ;WITH Activities AS
                (
                    SELECT CONVERT(date, [CreatedAt]) AS [ActivityDate], [CreatedAt] AS [ActivityAt], 1 AS [HawalaCount], 0 AS [ExchangeCount], 0 AS [AccountOperationCount], 0 AS [ExpenseCount], 0 AS [CapitalInvestmentCount]
                    FROM [dbo].[Hawalas]
                    WHERE [TenantId] = @TenantId AND [CreatedAt] >= @FromDate AND [CreatedAt] < @ToDateExclusive AND [Status] <> N'Cancel'
                    UNION ALL
                    SELECT CONVERT(date, [ExchangeDate]), [ExchangeDate], 0, 1, 0, 0, 0
                    FROM [dbo].[MoneyExchangeOperations]
                    WHERE [TenantId] = @TenantId AND [ExchangeDate] >= @FromDate AND [ExchangeDate] < @ToDateExclusive AND [IsDeleted] = 0
                    UNION ALL
                    SELECT CONVERT(date, [OperationDate]), [OperationDate], 0, 0, 1, 0, 0
                    FROM [dbo].[AccountMoneyOperations]
                    WHERE [TenantId] = @TenantId AND [OperationDate] >= @FromDate AND [OperationDate] < @ToDateExclusive AND [IsDeleted] = 0
                    UNION ALL
                    SELECT CONVERT(date, [ExpenseDate]), [ExpenseDate], 0, 0, 0, 1, 0
                    FROM [dbo].[Expenses]
                    WHERE [TenantId] = @TenantId AND [ExpenseDate] >= @FromDate AND [ExpenseDate] < @ToDateExclusive AND [IsDeleted] = 0
                    UNION ALL
                    SELECT CONVERT(date, [InvestmentDate]), [InvestmentDate], 0, 0, 0, 0, 1
                    FROM [dbo].[CapitalInvestments]
                    WHERE [TenantId] = @TenantId AND [InvestmentDate] >= @FromDate AND [InvestmentDate] < @ToDateExclusive AND [IsDeleted] = 0
                )
                SELECT [ActivityDate], MAX([ActivityAt]) AS [LastActivityAt],
                       SUM([HawalaCount]) AS [HawalaCount],
                       SUM([ExchangeCount]) AS [ExchangeCount],
                       SUM([AccountOperationCount]) AS [AccountOperationCount],
                       SUM([ExpenseCount]) AS [ExpenseCount],
                       SUM([CapitalInvestmentCount]) AS [CapitalInvestmentCount]
                FROM Activities
                GROUP BY [ActivityDate]
                ORDER BY [ActivityDate];

                SELECT le.[AccountId], a.[AccountCode], a.[AccountName], a.[AccountType],
                       le.[CurrencyId], c.[Code] AS [CurrencyCode],
                       SUM(le.[TalabKar]) AS [TalabKar], SUM(le.[BadehKar]) AS [BadehKar]
                FROM [dbo].[LedgerEntries] le
                INNER JOIN [dbo].[Accounts] a ON a.[TenantId] = le.[TenantId] AND a.[Id] = le.[AccountId]
                INNER JOIN [dbo].[Currencies] c ON c.[TenantId] = le.[TenantId] AND c.[Id] = le.[CurrencyId]
                WHERE le.[TenantId] = @TenantId AND le.[CreatedAt] < @ToDateExclusive
                GROUP BY le.[AccountId], a.[AccountCode], a.[AccountName], a.[AccountType], le.[CurrencyId], c.[Code];

                SELECT CONVERT(date, le.[CreatedAt]) AS [EntryDate],
                       le.[AccountId], a.[AccountCode], a.[AccountName], a.[AccountType],
                       le.[CurrencyId], c.[Code] AS [CurrencyCode],
                       SUM(le.[TalabKar]) AS [TalabKar], SUM(le.[BadehKar]) AS [BadehKar]
                FROM [dbo].[LedgerEntries] le
                INNER JOIN [dbo].[Accounts] a ON a.[TenantId] = le.[TenantId] AND a.[Id] = le.[AccountId]
                INNER JOIN [dbo].[Currencies] c ON c.[TenantId] = le.[TenantId] AND c.[Id] = le.[CurrencyId]
                WHERE le.[TenantId] = @TenantId AND le.[CreatedAt] >= @FromDate AND le.[CreatedAt] < @ToDateExclusive
                GROUP BY CONVERT(date, le.[CreatedAt]), le.[AccountId], a.[AccountCode], a.[AccountName], a.[AccountType], le.[CurrencyId], c.[Code]
                ORDER BY [EntryDate];
            END
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_GetDailyJournal_v1]
                @TenantId bigint,
                @FromDate datetime2,
                @ToDateExclusive datetime2
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT le.[Id], le.[CreatedAt], le.[HawalaId], le.[CapitalInvestmentId],
                       le.[ExpenseId], le.[AccountMoneyOperationId], le.[MoneyExchangeOperationId],
                       le.[TransferId], le.[TransactionId], le.[AccountId],
                       a.[AccountCode], a.[AccountName], a.[AccountType],
                       le.[CurrencyId], c.[Code] AS [CurrencyCode],
                       le.[TalabKar], le.[BadehKar], le.[Description]
                FROM [dbo].[LedgerEntries] le
                INNER JOIN [dbo].[Accounts] a ON a.[TenantId] = le.[TenantId] AND a.[Id] = le.[AccountId]
                INNER JOIN [dbo].[Currencies] c ON c.[TenantId] = le.[TenantId] AND c.[Id] = le.[CurrencyId]
                WHERE le.[TenantId] = @TenantId
                  AND le.[CreatedAt] >= @FromDate
                  AND le.[CreatedAt] < @ToDateExclusive
                ORDER BY le.[CreatedAt] DESC, le.[Id] DESC;
            END
GO
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_ValidateHawalaImportStaging_v1]
                @TenantId bigint,
                @BatchId bigint
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE @CorrespondentId bigint, @ExpectedRows int, @Status nvarchar(30),
                        @CurrentPeriodStart datetime2;
                SELECT @CorrespondentId = [CorrespondentId], @ExpectedRows = [RowCount], @Status = [Status]
                FROM [dbo].[HawalaImportBatches] WITH (UPDLOCK, HOLDLOCK)
                WHERE [TenantId] = @TenantId AND [Id] = @BatchId;

                IF @CorrespondentId IS NULL
                    THROW 51000, N'پیش‌نمایش آپلود پیدا نشد.', 1;
                IF @Status <> N'Preview'
                    THROW 51000, N'این پیش‌نمایش دیگر قابل اعتبارسنجی نیست.', 1;
                IF (SELECT COUNT(*) FROM [dbo].[HawalaImportRows] WHERE [TenantId] = @TenantId AND [BatchId] = @BatchId) <> @ExpectedRows
                    THROW 51000, N'تعداد ردیف‌های جدول آماده‌سازی با فایل برابر نیست.', 1;

                SELECT @CurrentPeriodStart = COALESCE(MAX([PeriodTo]), CONVERT(datetime2, '0001-01-01'))
                FROM [dbo].[CorrespondentAccountPeriods]
                WHERE [TenantId] = @TenantId AND [CorrespondentId] = @CorrespondentId;

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
                         AND h.[HawalaType] = N'HawalaReceive' AND h.[Number] = r.[HawalaNumber]
                         AND h.[CreatedAt] >= @CurrentPeriodStart);

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
                         AND h.[HawalaType] = N'HawalaReceive' AND h.[ReferenceNumber] = r.[ReferenceNumber]
                         AND h.[CreatedAt] >= @CurrentPeriodStart);

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
GO
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
GO
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_ProcessAedDeal_v1]
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_CreateAedDeal_v1]
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_ConvertAedDeal_v1]
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_ReverseAedDealConversion_v1]
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_CancelAedDeal_v1]
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
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_GetAccountOperationsPage_v1]
                @TenantId bigint,
                @AccountId bigint,
                @PageNumber int = 1,
                @PageSize int = 20,
                @SearchTerm nvarchar(200) = NULL,
                @SourceType nvarchar(50) = NULL,
                @CurrencyCode nvarchar(20) = NULL,
                @FromDate datetime2(7) = NULL,
                @ToDateExclusive datetime2(7) = NULL,
                @CorrespondentId bigint = NULL,
                @IncludeStatusHeader bit = 0,
                @IncludeBalances bit = 0
            AS
            BEGIN
                SET NOCOUNT ON;

                IF @TenantId <= 0
                    THROW 51000, 'A valid tenant identifier is required.', 1;
                IF @PageNumber < 1 OR @PageSize < 1 OR @PageSize > 100
                    THROW 51001, 'PageNumber and PageSize are outside the supported range.', 1;

                IF @IncludeStatusHeader = 1
                BEGIN
                    IF @CorrespondentId IS NULL
                        THROW 51003, 'A correspondent identifier is required for the status page.', 1;

                    SELECT TOP (1) @AccountId = account.[Id]
                    FROM [dbo].[Accounts] account
                    WHERE account.[TenantId] = @TenantId
                      AND account.[CorrespondentId] = @CorrespondentId
                      AND account.[IsArchived] = 0
                    ORDER BY account.[Id];

                    SELECT correspondent.[Id], correspondent.[Code], correspondent.[Name],
                           correspondent.[Country], correspondent.[City], correspondent.[PhoneNumber],
                           correspondent.[Address], correspondent.[IsArchived], correspondent.[Remarks],
                           correspondent.[SettlementCurrencyId], currency.[Code] AS [SettlementCurrencyCode],
                           correspondent.[CommissionMethod], correspondent.[CreatedAt], @AccountId AS [AccountId]
                    FROM [dbo].[Correspondents] correspondent
                    LEFT JOIN [dbo].[Currencies] currency
                      ON currency.[TenantId] = @TenantId
                     AND currency.[Id] = correspondent.[SettlementCurrencyId]
                    WHERE correspondent.[TenantId] = @TenantId
                      AND correspondent.[Id] = @CorrespondentId;

                    IF @@ROWCOUNT = 0
                        RETURN;

                    IF @AccountId IS NULL
                    BEGIN
                        SELECT CAST(NULL AS bigint) AS [CurrencyId], CAST(NULL AS nvarchar(20)) AS [CurrencyCode],
                               CAST(NULL AS decimal(18,2)) AS [Balance] WHERE 1 = 0;
                        SELECT CAST(0 AS bigint) AS [TotalCount];
                        SELECT CAST(NULL AS nvarchar(100)) AS [OperationKey], CAST(NULL AS datetime2) AS [CreatedAt],
                               CAST(NULL AS nvarchar(50)) AS [SourceType], CAST(NULL AS bigint) AS [SourceId],
                               CAST(NULL AS nvarchar(100)) AS [DocumentNumber], CAST(NULL AS nvarchar(500)) AS [Description],
                               CAST(NULL AS nvarchar(200)) AS [AccountName], CAST(NULL AS bigint) AS [CurrencyId],
                               CAST(NULL AS nvarchar(20)) AS [CurrencyCode], CAST(NULL AS decimal(18,2)) AS [TotalTalabKar],
                               CAST(NULL AS decimal(18,2)) AS [TotalBadehKar] WHERE 1 = 0;
                        SELECT CAST(NULL AS nvarchar(20)) AS [Code] WHERE 1 = 0;
                        RETURN;
                    END
                END

                IF @AccountId IS NULL OR @AccountId <= 0
                    THROW 51000, 'A valid account identifier is required.', 1;
                IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[Accounts]
                    WHERE [TenantId] = @TenantId AND [Id] = @AccountId)
                    THROW 51002, 'The account does not belong to the current tenant.', 1;

                IF @IncludeBalances = 1
                BEGIN
                    SELECT currency.[Id] AS [CurrencyId], currency.[Code] AS [CurrencyCode],
                           CAST(balance.[Balance] AS decimal(18,2)) AS [Balance]
                    FROM [dbo].[AccountCurrencyBalances] balance
                    INNER JOIN [dbo].[Currencies] currency
                      ON currency.[TenantId] = balance.[TenantId] AND currency.[Id] = balance.[CurrencyId]
                    WHERE balance.[TenantId] = @TenantId AND balance.[AccountId] = @AccountId
                      AND balance.[Balance] <> 0
                    ORDER BY currency.[Code];
                END

                SET @SearchTerm = NULLIF(LTRIM(RTRIM(@SearchTerm)), N'');
                SET @SourceType = NULLIF(LTRIM(RTRIM(@SourceType)), N'');
                SET @CurrencyCode = NULLIF(LTRIM(RTRIM(@CurrencyCode)), N'');

                SELECT
                    entry.[Id], entry.[CreatedAt], entry.[CurrencyId],
                    entry.[TalabKar], entry.[BadehKar], entry.[Description],
                    CASE
                        WHEN entry.[HawalaId] IS NOT NULL THEN N'Hawala'
                        WHEN entry.[CapitalInvestmentId] IS NOT NULL THEN N'CapitalInvestment'
                        WHEN entry.[ExpenseId] IS NOT NULL THEN N'Expense'
                        WHEN entry.[AccountMoneyOperationId] IS NOT NULL THEN N'AccountMoneyOperation'
                        WHEN entry.[MoneyExchangeOperationId] IS NOT NULL THEN N'MoneyExchangeOperation'
                        WHEN entry.[TransferId] IS NOT NULL THEN N'Transfer'
                        WHEN entry.[TransactionId] IS NOT NULL THEN N'Transaction'
                        ELSE N'Manual'
                    END AS [SourceKind],
                    COALESCE(entry.[HawalaId], entry.[CapitalInvestmentId], entry.[ExpenseId],
                             entry.[AccountMoneyOperationId], entry.[MoneyExchangeOperationId],
                             entry.[TransferId], entry.[TransactionId], entry.[Id]) AS [OperationId]
                INTO #AccountEntries
                FROM [dbo].[LedgerEntries] entry
                WHERE entry.[TenantId] = @TenantId
                  AND entry.[AccountId] = @AccountId;

                CREATE CLUSTERED INDEX [IX_AccountEntries_Operation]
                    ON #AccountEntries ([SourceKind], [OperationId], [CurrencyId]);

                SELECT
                    CONCAT(source.[SourceKind], N':', CONVERT(nvarchar(30), source.[OperationId])) AS [OperationKey],
                    source.[SourceKind], source.[OperationId],
                    MIN(source.[CreatedAt]) AS [CreatedAt],
                    MAX(NULLIF(LTRIM(RTRIM(source.[Description])), N'')) AS [Description],
                    CONVERT(nvarchar(100), source.[OperationId]) AS [DocumentNumber]
                INTO #Operations
                FROM #AccountEntries source
                GROUP BY source.[SourceKind], source.[OperationId];

                UPDATE operation SET [DocumentNumber] = CONVERT(nvarchar(100), hawala.[Number])
                FROM #Operations operation
                INNER JOIN [dbo].[Hawalas] hawala
                    ON hawala.[TenantId] = @TenantId
                   AND operation.[SourceKind] = N'Hawala'
                   AND hawala.[Id] = operation.[OperationId];

                UPDATE operation SET [DocumentNumber] = COALESCE(NULLIF(transfer.[ReferenceNumber], N''), CONVERT(nvarchar(100), transfer.[Id]))
                FROM #Operations operation
                INNER JOIN [dbo].[Transfers] transfer
                    ON transfer.[TenantId] = @TenantId
                   AND operation.[SourceKind] = N'Transfer'
                   AND transfer.[Id] = operation.[OperationId];

                UPDATE operation SET [DocumentNumber] = transactionRow.[TransactionNo]
                FROM #Operations operation
                INNER JOIN [dbo].[Transactions] transactionRow
                    ON transactionRow.[TenantId] = @TenantId
                   AND operation.[SourceKind] = N'Transaction'
                   AND transactionRow.[Id] = operation.[OperationId];

                SELECT operation.*
                INTO #FilteredOperations
                FROM #Operations operation
                WHERE (@SourceType IS NULL
                       OR (@SourceType = N'Other' AND operation.[SourceKind] NOT IN
                           (N'MoneyExchangeOperation', N'Transfer', N'AccountMoneyOperation'))
                       OR @SourceType = CASE operation.[SourceKind]
                            WHEN N'Hawala' THEN N'حواله'
                            WHEN N'CapitalInvestment' THEN N'ثبت سرمایه'
                            WHEN N'Expense' THEN N'مصرف'
                            WHEN N'AccountMoneyOperation' THEN N'واریز / برداشت / پرداخت'
                            WHEN N'MoneyExchangeOperation' THEN N'تبدیل پول'
                            WHEN N'Transfer' THEN N'انتقال'
                            WHEN N'Transaction' THEN N'تراکنش'
                            ELSE N'ثبت دستی' END)
                  AND (@FromDate IS NULL OR operation.[CreatedAt] >= @FromDate)
                  AND (@ToDateExclusive IS NULL OR operation.[CreatedAt] < @ToDateExclusive)
                  AND (@SearchTerm IS NULL
                       OR operation.[DocumentNumber] LIKE N'%' + @SearchTerm + N'%'
                       OR operation.[Description] LIKE N'%' + @SearchTerm + N'%'
                       OR operation.[OperationKey] LIKE N'%' + @SearchTerm + N'%')
                  AND (@CurrencyCode IS NULL OR EXISTS (
                        SELECT 1
                        FROM #AccountEntries accountEntry
                        INNER JOIN [dbo].[Currencies] currency
                            ON currency.[TenantId] = @TenantId
                           AND currency.[Id] = accountEntry.[CurrencyId]
                        WHERE accountEntry.[SourceKind] = operation.[SourceKind]
                          AND accountEntry.[OperationId] = operation.[OperationId]
                          AND currency.[Code] = @CurrencyCode));

                SELECT COUNT_BIG(1) AS [TotalCount] FROM #FilteredOperations;

                ;WITH Numbered AS
                (
                    SELECT operation.*,
                           ROW_NUMBER() OVER (ORDER BY operation.[CreatedAt] DESC, operation.[OperationId] DESC) AS [RowNumber]
                    FROM #FilteredOperations operation
                )
                SELECT * INTO #PageOperations
                FROM Numbered
                WHERE [RowNumber] BETWEEN ((@PageNumber - 1) * @PageSize) + 1 AND @PageNumber * @PageSize;

                SELECT
                    operation.[OperationKey], operation.[CreatedAt],
                    CASE operation.[SourceKind]
                        WHEN N'Hawala' THEN N'حواله'
                        WHEN N'CapitalInvestment' THEN N'ثبت سرمایه'
                        WHEN N'Expense' THEN N'مصرف'
                        WHEN N'AccountMoneyOperation' THEN N'واریز / برداشت / پرداخت'
                        WHEN N'MoneyExchangeOperation' THEN N'تبدیل پول'
                        WHEN N'Transfer' THEN N'انتقال'
                        WHEN N'Transaction' THEN N'تراکنش'
                        ELSE N'ثبت دستی' END AS [SourceType],
                    CASE WHEN operation.[SourceKind] = N'Manual' THEN NULL ELSE operation.[OperationId] END AS [SourceId],
                    operation.[DocumentNumber], COALESCE(operation.[Description], N'-') AS [Description],
                    account.[AccountName], currency.[Id] AS [CurrencyId], currency.[Code] AS [CurrencyCode],
                    CAST(SUM(entry.[TalabKar]) AS decimal(18, 2)) AS [TotalTalabKar],
                    CAST(SUM(entry.[BadehKar]) AS decimal(18, 2)) AS [TotalBadehKar]
                FROM #PageOperations operation
                INNER JOIN #AccountEntries entry
                    ON entry.[SourceKind] = operation.[SourceKind]
                   AND entry.[OperationId] = operation.[OperationId]
                INNER JOIN [dbo].[Currencies] currency
                    ON currency.[TenantId] = @TenantId AND currency.[Id] = entry.[CurrencyId]
                INNER JOIN [dbo].[Accounts] account
                    ON account.[TenantId] = @TenantId AND account.[Id] = @AccountId
                GROUP BY operation.[OperationKey], operation.[CreatedAt], operation.[SourceKind],
                         operation.[OperationId], operation.[DocumentNumber], operation.[Description],
                         operation.[RowNumber], account.[AccountName], currency.[Id], currency.[Code]
                ORDER BY operation.[RowNumber], currency.[Code];

                SELECT DISTINCT currency.[Code]
                FROM #AccountEntries entry
                INNER JOIN [dbo].[Currencies] currency
                    ON currency.[TenantId] = @TenantId AND currency.[Id] = entry.[CurrencyId]
                ORDER BY currency.[Code];
            END;

