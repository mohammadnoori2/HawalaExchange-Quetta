-- Recognition is metadata only: closing a period never reposts commission journal entries.
CREATE OR ALTER FUNCTION [dbo].[ufn_PendingCorrespondentCommissions_v1]
(@TenantId bigint, @AccountId bigint, @AsOfDate datetime2(7))
RETURNS TABLE
AS RETURN
(
    SELECT [AccountId], [CurrencyId],
           CAST(SUM(CASE WHEN [Net] < 0 THEN -[Net] ELSE 0 END) AS decimal(18,4)) AS [PendingCommissionDebit],
           CAST(SUM(CASE WHEN [Net] > 0 THEN [Net] ELSE 0 END) AS decimal(18,4)) AS [PendingCommissionCredit]
    FROM
    (
        SELECT e.[AccountId], e.[CurrencyId], COALESCE(t.[ReversedTransactionId], t.[Id]) AS [CommissionDocumentId],
               SUM(e.[TalabKar] - e.[BadehKar]) AS [Net]
        FROM [dbo].[Transactions] t
        JOIN [dbo].[LedgerEntries] e ON e.[TenantId] = t.[TenantId] AND e.[TransactionId] = t.[Id]
        JOIN [dbo].[Accounts] a ON a.[TenantId] = e.[TenantId] AND a.[Id] = e.[AccountId]
        WHERE t.[TenantId] = @TenantId AND a.[CorrespondentId] IS NOT NULL
          AND t.[TransactionType] IN (N'PeriodicCorrespondentCommission', N'PeriodicOutgoingCommission',
                                     N'PeriodicForwardingCommission', N'PeriodicCorrespondentCommissionReversal')
          AND (@AccountId IS NULL OR e.[AccountId] = @AccountId)
          AND (@AsOfDate IS NULL OR e.[CreatedAt] <= @AsOfDate)
          AND NOT EXISTS
          (
              SELECT 1 FROM [dbo].[CorrespondentCommissionRecognitions] r
              JOIN [dbo].[CorrespondentAccountPeriods] p ON p.[TenantId] = r.[TenantId] AND p.[Id] = r.[PeriodId]
              WHERE r.[TenantId] = e.[TenantId] AND r.[AccountId] = e.[AccountId] AND r.[TransactionId] = e.[TransactionId]
                AND (@AsOfDate IS NULL OR p.[PeriodTo] <= @AsOfDate)
          )
        GROUP BY e.[AccountId], e.[CurrencyId], COALESCE(t.[ReversedTransactionId], t.[Id])
    ) pending
    GROUP BY [AccountId], [CurrencyId]
);
GO
CREATE OR ALTER FUNCTION [dbo].[ufn_AccountOperationalBalances_v1]
(@TenantId bigint, @AccountId bigint, @AsOfDate datetime2(7))
RETURNS TABLE
AS RETURN
(
    WITH rawBalances AS
    (
        SELECT [AccountId], [CurrencyId], [Balance]
        FROM [dbo].[AccountCurrencyBalances]
        WHERE @AsOfDate IS NULL AND [TenantId] = @TenantId AND (@AccountId IS NULL OR [AccountId] = @AccountId)
        UNION ALL
        SELECT [AccountId], [CurrencyId], CAST(SUM([TalabKar] - [BadehKar]) AS decimal(18,4))
        FROM [dbo].[LedgerEntries]
        WHERE @AsOfDate IS NOT NULL AND [TenantId] = @TenantId AND [CreatedAt] <= @AsOfDate
          AND (@AccountId IS NULL OR [AccountId] = @AccountId)
        GROUP BY [AccountId], [CurrencyId]
    )
    SELECT COALESCE(raw.[AccountId], pending.[AccountId]) AS [AccountId],
           COALESCE(raw.[CurrencyId], pending.[CurrencyId]) AS [CurrencyId],
           CAST(COALESCE(raw.[Balance], 0) - COALESCE(pending.[PendingCommissionCredit], 0)
                + COALESCE(pending.[PendingCommissionDebit], 0) AS decimal(18,4)) AS [Balance],
           COALESCE(pending.[PendingCommissionDebit], 0) AS [PendingCommissionDebit],
           COALESCE(pending.[PendingCommissionCredit], 0) AS [PendingCommissionCredit],
           COALESCE(raw.[Balance], 0) AS [TotalIncludingCommission]
    FROM rawBalances raw
    FULL OUTER JOIN [dbo].[ufn_PendingCorrespondentCommissions_v1](@TenantId, @AccountId, @AsOfDate) pending
      ON pending.[AccountId] = raw.[AccountId] AND pending.[CurrencyId] = raw.[CurrencyId]
);
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_GetDeferredAccountBalances_v1]
    @TenantId bigint, @AccountId bigint = NULL, @CustomerId bigint = NULL, @CorrespondentId bigint = NULL,
    @OwnerType nvarchar(20) = NULL, @AccountType nvarchar(50) = NULL, @AsOfDate datetime2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT a.[Id] AS [AccountId], a.[AccountName], a.[AccountType], a.[CustomerId], a.[CorrespondentId],
           b.[CurrencyId], c.[Code] AS [CurrencyCode], CAST(b.[Balance] AS decimal(18,2)) AS [Balance],
           CAST(b.[PendingCommissionDebit] AS decimal(18,2)) AS [PendingCommissionDebit],
           CAST(b.[PendingCommissionCredit] AS decimal(18,2)) AS [PendingCommissionCredit]
    FROM [dbo].[ufn_AccountOperationalBalances_v1](@TenantId, @AccountId, @AsOfDate) b
    JOIN [dbo].[Accounts] a ON a.[TenantId] = @TenantId AND a.[Id] = b.[AccountId]
    JOIN [dbo].[Currencies] c ON c.[TenantId] = @TenantId AND c.[Id] = b.[CurrencyId]
    WHERE (@CustomerId IS NULL OR a.[CustomerId] = @CustomerId)
      AND (@CorrespondentId IS NULL OR a.[CorrespondentId] = @CorrespondentId)
      AND (@AccountType IS NULL OR a.[AccountType] = @AccountType)
      AND (@OwnerType IS NULL OR (@OwnerType = N'Customer' AND a.[CustomerId] IS NOT NULL)
                             OR (@OwnerType = N'Correspondent' AND a.[CorrespondentId] IS NOT NULL))
      AND (b.[Balance] <> 0 OR b.[PendingCommissionDebit] <> 0 OR b.[PendingCommissionCredit] <> 0)
    ORDER BY a.[Id], c.[Code];
END;
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_GetCorrespondentBalanceSummary_v1]
    @TenantId bigint, @AccountId bigint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.[CurrencyId], c.[Code] AS [CurrencyCode], CAST(b.[Balance] AS decimal(18,2)) AS [Balance],
           CAST(b.[PendingCommissionDebit] AS decimal(18,2)) AS [PendingCommissionDebit],
           CAST(b.[PendingCommissionCredit] AS decimal(18,2)) AS [PendingCommissionCredit]
    FROM [dbo].[ufn_AccountOperationalBalances_v1](@TenantId, @AccountId, NULL) b
    JOIN [dbo].[Currencies] c ON c.[TenantId] = @TenantId AND c.[Id] = b.[CurrencyId]
    WHERE b.[Balance] <> 0 OR b.[PendingCommissionDebit] <> 0 OR b.[PendingCommissionCredit] <> 0
    ORDER BY c.[Code];
END;
GO
CREATE OR ALTER PROCEDURE [dbo].[usp_GetDeferredSettlementBalances_v1]
    @TenantId bigint, @AccountId bigint, @TargetCurrencyId bigint,
    @Rates [dbo].[SettlementRateTableType_v1] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.[CurrencyId], c.[Code], c.[QuotationPriority],
           CAST(CASE WHEN b.[Balance] > 0 THEN b.[Balance] ELSE 0 END AS decimal(18,4)),
           CAST(CASE WHEN b.[Balance] < 0 THEN -b.[Balance] ELSE 0 END AS decimal(18,4)),
           r.[Rate], CAST(0 AS decimal(18,4)), CAST(0 AS decimal(18,4))
    FROM [dbo].[ufn_AccountOperationalBalances_v1](@TenantId, @AccountId, NULL) b
    JOIN [dbo].[Currencies] c ON c.[TenantId] = @TenantId AND c.[Id] = b.[CurrencyId]
    JOIN @Rates r ON r.[SourceCurrencyId] = b.[CurrencyId] AND r.[HawalaId] = 0 AND r.[Rate] > 0
    WHERE b.[CurrencyId] <> @TargetCurrencyId AND b.[Balance] <> 0;
END;
