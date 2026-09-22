using System.Text;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260918123000_SeparateOutgoingPeriodicCommission")]
public sealed class SeparateOutgoingPeriodicCommission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(LoadProcedure());

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Procedure-only migration; schema rollback is not required.
    }

    private static string LoadProcedure()
    {
        const string resourceName =
            "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
        using var stream = typeof(SeparateOutgoingPeriodicCommission).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var procedure = Regex.Split(
                reader.ReadToEnd(), @"^\s*GO\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(x => x.Contains(
                "CREATE PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]",
                StringComparison.OrdinalIgnoreCase));
        var createOrAlter = Regex.Replace(
            procedure.Trim(), @"^\s*CREATE\s+PROCEDURE",
            "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase);
        return "IF COL_LENGTH(N'dbo.Hawalas', N'CommissionBaseUsdAmount') IS NOT NULL " +
               $"EXEC(N'{createOrAlter.Replace("'", "''")}');";
    }
}
