using System.Text;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930123000_OptimizeDeleteHawalaImportBatchProcedure")]
public sealed class OptimizeDeleteHawalaImportBatchProcedure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(LoadProcedure());

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Performance-only procedure revision; retaining the current procedure is safer.
    }

    private static string LoadProcedure()
    {
        const string resourceName =
            "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
        using var stream = typeof(OptimizeDeleteHawalaImportBatchProcedure).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Regex.Split(
                reader.ReadToEnd(), @"^\s*GO\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(x => x.Contains(
                "PROCEDURE [dbo].[usp_DeleteHawalaImportBatch_v1]",
                StringComparison.OrdinalIgnoreCase))
            .Trim();
    }
}
