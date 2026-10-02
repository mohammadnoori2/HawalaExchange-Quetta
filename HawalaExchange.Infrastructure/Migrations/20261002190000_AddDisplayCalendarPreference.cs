using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261002190000_AddDisplayCalendarPreference")]
public sealed class AddDisplayCalendarPreference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(
        name: "UsePersianCalendar", table: "CompanySettings", type: "bit", nullable: false, defaultValue: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "UsePersianCalendar", table: "CompanySettings");
}
