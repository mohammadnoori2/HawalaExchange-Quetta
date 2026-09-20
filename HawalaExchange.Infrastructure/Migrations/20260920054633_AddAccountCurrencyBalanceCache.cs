using System;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCurrencyBalanceCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountCurrencyBalances",
                columns: table => new
                {
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountCurrencyBalances", x => new { x.TenantId, x.AccountId, x.CurrencyId });
                    table.ForeignKey(
                        name: "FK_AccountCurrencyBalances_Accounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountCurrencyBalances_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountCurrencyBalances_TenantId_CurrencyId",
                table: "AccountCurrencyBalances",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.Sql("""
                INSERT INTO [dbo].[AccountCurrencyBalances]
                    ([TenantId], [AccountId], [CurrencyId], [Balance], [UpdatedAt])
                SELECT [TenantId], [AccountId], [CurrencyId],
                       CAST(SUM([TalabKar] - [BadehKar]) AS decimal(18,4)), SYSUTCDATETIME()
                FROM [dbo].[LedgerEntries]
                GROUP BY [TenantId], [AccountId], [CurrencyId]
                HAVING SUM([TalabKar] - [BadehKar]) <> 0;
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER [dbo].[trg_LedgerEntries_AccountCurrencyBalances_v1]
                ON [dbo].[LedgerEntries]
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    ;WITH [Changes] AS
                    (
                        SELECT [TenantId], [AccountId], [CurrencyId], SUM([Delta]) AS [Delta]
                        FROM
                        (
                            SELECT [TenantId], [AccountId], [CurrencyId],
                                   CAST([TalabKar] - [BadehKar] AS decimal(18,4)) AS [Delta]
                            FROM inserted
                            UNION ALL
                            SELECT [TenantId], [AccountId], [CurrencyId],
                                   CAST(-([TalabKar] - [BadehKar]) AS decimal(18,4)) AS [Delta]
                            FROM deleted
                        ) source
                        GROUP BY [TenantId], [AccountId], [CurrencyId]
                    )
                    MERGE [dbo].[AccountCurrencyBalances] WITH (HOLDLOCK) AS target
                    USING [Changes] AS source
                       ON target.[TenantId] = source.[TenantId]
                      AND target.[AccountId] = source.[AccountId]
                      AND target.[CurrencyId] = source.[CurrencyId]
                    WHEN MATCHED THEN
                        UPDATE SET target.[Balance] = target.[Balance] + source.[Delta],
                                   target.[UpdatedAt] = SYSUTCDATETIME()
                    WHEN NOT MATCHED BY TARGET AND source.[Delta] <> 0 THEN
                        INSERT ([TenantId], [AccountId], [CurrencyId], [Balance], [UpdatedAt])
                        VALUES (source.[TenantId], source.[AccountId], source.[CurrencyId],
                                source.[Delta], SYSUTCDATETIME());

                    DELETE balance
                    FROM [dbo].[AccountCurrencyBalances] balance
                    INNER JOIN
                    (
                        SELECT [TenantId], [AccountId], [CurrencyId] FROM inserted
                        UNION
                        SELECT [TenantId], [AccountId], [CurrencyId] FROM deleted
                    ) changed
                      ON changed.[TenantId] = balance.[TenantId]
                     AND changed.[AccountId] = balance.[AccountId]
                     AND changed.[CurrencyId] = balance.[CurrencyId]
                    WHERE balance.[Balance] = 0;
                END;
                """);

            migrationBuilder.Sql(LoadProcedure("usp_GetAccountBalances_v1"));
            migrationBuilder.Sql(LoadProcedure("usp_GetAccountOperationsPage_v1"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[trg_LedgerEntries_AccountCurrencyBalances_v1];");
            migrationBuilder.DropTable(
                name: "AccountCurrencyBalances");
        }

        private static string LoadProcedure(string procedureName)
        {
            const string resourceName =
                "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
            using var stream = typeof(AddAccountCurrencyBalanceCache).Assembly
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded SQL resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Single(x => x.Contains(
                    $"PROCEDURE [dbo].[{procedureName}]",
                    StringComparison.OrdinalIgnoreCase))
                .Trim();
        }
    }
}
