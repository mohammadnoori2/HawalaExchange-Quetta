using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace HawalaExchange.Infrastructure.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261110180000_CashExecutedHawalas")]
public sealed class CashExecutedHawalas : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer") return;
        using var stream = typeof(CashExecutedHawalas).Assembly.GetManifestResourceStream("HawalaExchange.Infrastructure.Sql.CashExecutedHawalas.sql")!;
        using var reader = new StreamReader(stream); builder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder builder)
    { if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer") builder.Sql("DROP FUNCTION IF EXISTS dbo.ufn_CashExecutedHawalas_v1;"); }
}
