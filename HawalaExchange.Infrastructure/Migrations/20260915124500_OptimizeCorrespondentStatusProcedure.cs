using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915124500_OptimizeCorrespondentStatusProcedure")]
public sealed class OptimizeCorrespondentStatusProcedure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(AddAccountOperationsPageProcedure.ProcedureSql);

    // The earlier migration still owns the procedure when rolling back only this optimization.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
