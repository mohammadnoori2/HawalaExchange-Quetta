using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferCurrencyCostBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProfitCurrencyAmount",
                table: "Transfers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProfitCurrencyId",
                table: "Transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TenantId_ProfitCurrencyId",
                table: "Transfers",
                columns: new[] { "TenantId", "ProfitCurrencyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_Currencies_TenantId_ProfitCurrencyId",
                table: "Transfers",
                columns: new[] { "TenantId", "ProfitCurrencyId" },
                principalTable: "Currencies",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_Currencies_TenantId_ProfitCurrencyId",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_TenantId_ProfitCurrencyId",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ProfitCurrencyAmount",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ProfitCurrencyId",
                table: "Transfers");
        }
    }
}
