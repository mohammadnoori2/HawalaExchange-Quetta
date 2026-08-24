using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkPaymentLocationsToCorrespondents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CorrespondentId",
                table: "PaymentLocations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_TenantId_CorrespondentId",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "CorrespondentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentLocations_Correspondents_TenantId_CorrespondentId",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "CorrespondentId" },
                principalTable: "Correspondents",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentLocations_Correspondents_TenantId_CorrespondentId",
                table: "PaymentLocations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentLocations_TenantId_CorrespondentId",
                table: "PaymentLocations");

            migrationBuilder.DropColumn(
                name: "CorrespondentId",
                table: "PaymentLocations");
        }
    }
}
