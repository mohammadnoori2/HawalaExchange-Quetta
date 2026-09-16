using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardProcedure : Migration
    {
        /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
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
            """);

        migrationBuilder.Sql("""
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
            """);
        }

        /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetDashboardData_v1];");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetDailyJournal_v1];");
    }
}
}
