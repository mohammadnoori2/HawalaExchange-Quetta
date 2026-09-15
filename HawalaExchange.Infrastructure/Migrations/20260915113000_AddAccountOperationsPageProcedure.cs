using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915113000_AddAccountOperationsPageProcedure")]
public sealed class AddAccountOperationsPageProcedure : Migration
{
    internal const string ProcedureSql = """
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
                           CAST(SUM(entry.[TalabKar] - entry.[BadehKar]) AS decimal(18,2)) AS [Balance]
                    FROM [dbo].[LedgerEntries] entry
                    INNER JOIN [dbo].[Currencies] currency
                      ON currency.[TenantId] = @TenantId AND currency.[Id] = entry.[CurrencyId]
                    WHERE entry.[TenantId] = @TenantId AND entry.[AccountId] = @AccountId
                    GROUP BY currency.[Id], currency.[Code]
                    HAVING SUM(entry.[TalabKar] - entry.[BadehKar]) <> 0
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
            """;

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ProcedureSql);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetAccountOperationsPage_v1];");
}
