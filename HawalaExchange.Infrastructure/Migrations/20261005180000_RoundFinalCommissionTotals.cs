using System.Text;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261005180000_RoundFinalCommissionTotals")]
public sealed class RoundFinalCommissionTotals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No existing batch, share, period closing or journal is rewritten.
        const string resource = "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
        using var stream = typeof(RoundFinalCommissionTotals).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resource}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var procedure = Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(x => x.Contains("PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]", StringComparison.OrdinalIgnoreCase));
        migrationBuilder.Sql(Regex.Replace(procedure.Trim(), @"^\s*CREATE\s+(?:OR\s+ALTER\s+)?PROCEDURE",
            "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a verified backup to downgrade commission rounding.");
}
