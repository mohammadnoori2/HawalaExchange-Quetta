using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalReportProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
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
                """);

            migrationBuilder.Sql(
                """
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
                """);

            migrationBuilder.Sql(
                """
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetCommissionReport_v1];");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetTransactionReport_v1];");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetDailyOperationalReport_v1];");
        }
    }
}
