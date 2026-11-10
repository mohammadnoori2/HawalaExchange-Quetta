using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261029120000_HawalaPostingHistory")]
public sealed class HawalaPostingHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(HawalaPostingHistory).Assembly.GetManifestResourceStream("HawalaExchange.Infrastructure.Sql.HawalaPostingHistory.sql")!;
        using var reader = new StreamReader(stream);
        foreach (var batch in Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
            migrationBuilder.Sql(batch);
    }
    // Read-only reporting objects; ledger history and balances are never rewritten.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
