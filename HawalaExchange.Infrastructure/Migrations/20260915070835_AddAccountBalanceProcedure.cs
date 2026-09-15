using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountBalanceProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_TenantId_AccountId_CurrencyId",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_AccountId_CurrencyId_CreatedAt",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "AccountId", "CurrencyId", "CreatedAt" });

            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetAccountBalances_v1];");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_TenantId_AccountId_CurrencyId_CreatedAt",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_AccountId_CurrencyId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "AccountId", "CurrencyId" });
        }
    }
}
