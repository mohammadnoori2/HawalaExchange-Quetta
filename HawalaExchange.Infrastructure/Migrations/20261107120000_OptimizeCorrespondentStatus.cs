using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261107120000_OptimizeCorrespondentStatus")]
public sealed class OptimizeCorrespondentStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            Apply(migrationBuilder, "AccountOperationsOptimized.sql");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            Apply(migrationBuilder, "HawalaPostingHistory.sql");
    }

    private static void Apply(MigrationBuilder builder, string file)
    {
        using var stream = typeof(OptimizeCorrespondentStatus).Assembly.GetManifestResourceStream("HawalaExchange.Infrastructure.Sql." + file)!;
        using var reader = new StreamReader(stream);
        foreach (var batch in Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
            builder.Sql(batch);
    }
}
