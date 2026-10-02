using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261002210000_AddOwnerWithdrawals")]
public sealed class AddOwnerWithdrawals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsWithdrawal", "CapitalInvestments", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>("CancelledAt", "CapitalInvestments", type: "datetime2", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("CancelledAt", "CapitalInvestments");
        migrationBuilder.DropColumn("IsWithdrawal", "CapitalInvestments");
    }
}
