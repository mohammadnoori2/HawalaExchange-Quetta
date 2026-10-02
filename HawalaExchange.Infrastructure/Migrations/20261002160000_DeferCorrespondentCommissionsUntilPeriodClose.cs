using System.Text;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261002160000_DeferCorrespondentCommissionsUntilPeriodClose")]
public sealed class DeferCorrespondentCommissionsUntilPeriodClose : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Database-only recognition metadata; no journal amount or historical closing balance is rewritten.
        migrationBuilder.Sql("""
            CREATE TABLE [dbo].[CorrespondentCommissionRecognitions]
            (
                [TenantId] bigint NOT NULL, [AccountId] bigint NOT NULL, [TransactionId] bigint NOT NULL,
                [PeriodId] bigint NOT NULL,
                CONSTRAINT [PK_CorrespondentCommissionRecognitions] PRIMARY KEY ([TenantId], [AccountId], [TransactionId]),
                CONSTRAINT [FK_CommissionRecognition_Account] FOREIGN KEY ([TenantId], [AccountId])
                    REFERENCES [dbo].[Accounts] ([TenantId], [Id]),
                CONSTRAINT [FK_CommissionRecognition_Transaction] FOREIGN KEY ([TenantId], [TransactionId])
                    REFERENCES [dbo].[Transactions] ([TenantId], [Id]),
                CONSTRAINT [FK_CommissionRecognition_Period] FOREIGN KEY ([TenantId], [PeriodId])
                    REFERENCES [dbo].[CorrespondentAccountPeriods] ([TenantId], [Id])
            );
            CREATE INDEX [IX_Transactions_TenantId_CommissionType] ON [dbo].[Transactions] ([TenantId], [TransactionType], [Id]);

            INSERT INTO [dbo].[CorrespondentCommissionRecognitions] ([TenantId], [AccountId], [TransactionId], [PeriodId])
            SELECT journal.[TenantId], journal.[AccountId], journal.[TransactionId], closed.[Id]
            FROM
            (
                SELECT e.[TenantId], e.[AccountId], e.[TransactionId], a.[CorrespondentId], MAX(e.[CreatedAt]) AS [EntryAt]
                FROM [dbo].[LedgerEntries] e JOIN [dbo].[Accounts] a ON a.[TenantId] = e.[TenantId] AND a.[Id] = e.[AccountId]
                JOIN [dbo].[Transactions] t ON t.[TenantId] = e.[TenantId] AND t.[Id] = e.[TransactionId]
                WHERE a.[CorrespondentId] IS NOT NULL AND t.[TransactionType] IN
                    (N'PeriodicCorrespondentCommission', N'PeriodicOutgoingCommission', N'PeriodicForwardingCommission', N'PeriodicCorrespondentCommissionReversal')
                GROUP BY e.[TenantId], e.[AccountId], e.[TransactionId], a.[CorrespondentId]
            ) journal
            CROSS APPLY
            (
                SELECT TOP (1) p.[Id] FROM [dbo].[CorrespondentAccountPeriods] p
                WHERE p.[TenantId] = journal.[TenantId] AND p.[CorrespondentId] = journal.[CorrespondentId]
                  AND p.[PeriodTo] >= journal.[EntryAt] ORDER BY p.[PeriodTo], p.[Id]
            ) closed;
            """);
        foreach (var batch in LoadBatches("DeferredCorrespondentCommissions.sql"))
            migrationBuilder.Sql(batch);
        foreach (var name in new[] { "usp_GetAccountBalances_v1", "usp_GetAccountOperationsPage_v1", "usp_ProcessCorrespondentSettlement_v1", "usp_ProcessPeriodicCommission_v1" })
        {
            var procedure = LoadBatches("ConsolidatedDatabaseObjects.sql").Single(x =>
                x.Contains($"PROCEDURE [dbo].[{name}]", StringComparison.OrdinalIgnoreCase));
            migrationBuilder.Sql(Regex.Replace(procedure, @"^\s*CREATE\s+(?:OR\s+ALTER\s+)?PROCEDURE",
                "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase));
        }
    }

    private static IEnumerable<string> LoadBatches(string name)
    {
        using var stream = typeof(DeferCorrespondentCommissionsUntilPeriodClose).Assembly.GetManifestResourceStream($"HawalaExchange.Infrastructure.Sql.{name}")
            ?? throw new InvalidOperationException($"Embedded SQL resource {name} was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a verified backup to downgrade commission recognition.");
}
