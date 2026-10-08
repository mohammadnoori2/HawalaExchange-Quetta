using System.Text;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261007190000_AccountOperationAmountFilters")]
public sealed class AccountOperationAmountFilters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string resource = "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
        using var stream = typeof(AccountOperationAmountFilters).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Embedded SQL resource was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var procedure = Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(x => x.Contains("PROCEDURE [dbo].[usp_GetAccountOperationsPage_v1]", StringComparison.OrdinalIgnoreCase));
        migrationBuilder.Sql(procedure.Trim());
    }
    // Optional parameters remain backward compatible with older callers. No
    // accounting records, schema columns or stored data need to be downgraded.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
