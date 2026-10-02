using System.Text;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261002140000_DestinationCommissionLocationRates")]
public sealed class DestinationCommissionLocationRates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Only update calculation logic; previously saved rates and journals stay unchanged.
        const string resource = "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
        using var stream = typeof(DestinationCommissionLocationRates).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resource}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var procedure = Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(x => x.Contains("PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]", StringComparison.OrdinalIgnoreCase));
        migrationBuilder.Sql(Regex.Replace(procedure.Trim(), @"^\s*CREATE\s+PROCEDURE",
            "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a verified backup to downgrade commission calculation logic.");
}
