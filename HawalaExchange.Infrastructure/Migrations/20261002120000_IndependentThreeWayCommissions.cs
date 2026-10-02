using System.Text;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261002120000_IndependentThreeWayCommissions")]
public sealed class IndependentThreeWayCommissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.CorrespondentCommissionBatches', N'AccountingVersion') IS NULL
                ALTER TABLE [dbo].[CorrespondentCommissionBatches] ADD [AccountingVersion] int NOT NULL DEFAULT 1;
            IF COL_LENGTH(N'dbo.CorrespondentCommissionBatchItems', N'CommissionScope') IS NULL
                ALTER TABLE [dbo].[CorrespondentCommissionBatchItems] ADD [CommissionScope] nvarchar(20) NOT NULL DEFAULT N'Standard';
            IF COL_LENGTH(N'dbo.CorrespondentCommissionBatchItems', N'ValuationDate') IS NULL
                ALTER TABLE [dbo].[CorrespondentCommissionBatchItems] ADD [ValuationDate] datetime2 NULL;
            """);
        // Label historical items without changing their amounts, journal entries or batch scope.
        migrationBuilder.Sql("""
            UPDATE i SET i.[CommissionScope] = CASE
                WHEN b.[CommissionScope] = N'Standard' AND h.[HawalaType] = N'HawalaSend'
                THEN N'Destination' ELSE b.[CommissionScope] END
            FROM [dbo].[CorrespondentCommissionBatchItems] i
            JOIN [dbo].[CorrespondentCommissionBatches] b ON b.[TenantId] = i.[TenantId] AND b.[Id] = i.[BatchId]
            JOIN [dbo].[Hawalas] h ON h.[TenantId] = i.[TenantId] AND h.[Id] = i.[HawalaId];

            DROP INDEX IF EXISTS [IX_CorrespondentCommissionBatchItems_TenantId_HawalaId_IsActive]
                ON [dbo].[CorrespondentCommissionBatchItems];
            CREATE UNIQUE INDEX [IX_CorrespondentCommissionBatchItems_TenantId_HawalaId_CommissionScope]
                ON [dbo].[CorrespondentCommissionBatchItems] ([TenantId], [HawalaId], [CommissionScope])
                WHERE [IsActive] = 1;
            """);

        const string resource = "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
        using var stream = typeof(IndependentThreeWayCommissions).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resource}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var procedure = Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(x => x.Contains("PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]", StringComparison.OrdinalIgnoreCase));
        migrationBuilder.Sql(Regex.Replace(procedure.Trim(), @"^\s*CREATE\s+PROCEDURE",
            "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Accounting history cannot be downgraded automatically; restore a verified backup instead.");
}
