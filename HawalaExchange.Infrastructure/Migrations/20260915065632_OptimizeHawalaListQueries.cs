using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeHawalaListQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_FromCurrencyId",
                table: "Hawalas");

            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_HawalaType_Status",
                table: "Hawalas");

            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_PaymentLocationId",
                table: "Hawalas");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CorrespondentId_HawalaType_CreatedAt",
                table: "Hawalas",
                columns: new[] { "TenantId", "CorrespondentId", "HawalaType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_FromCurrencyId_CreatedAt",
                table: "Hawalas",
                columns: new[] { "TenantId", "FromCurrencyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_HawalaType_Status_CreatedAt",
                table: "Hawalas",
                columns: new[] { "TenantId", "HawalaType", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_Number",
                table: "Hawalas",
                columns: new[] { "TenantId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_PaymentLocationId_HawalaType_CreatedAt",
                table: "Hawalas",
                columns: new[] { "TenantId", "PaymentLocationId", "HawalaType", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_CorrespondentId_HawalaType_CreatedAt",
                table: "Hawalas");

            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_FromCurrencyId_CreatedAt",
                table: "Hawalas");

            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_HawalaType_Status_CreatedAt",
                table: "Hawalas");

            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_Number",
                table: "Hawalas");

            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_PaymentLocationId_HawalaType_CreatedAt",
                table: "Hawalas");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_FromCurrencyId",
                table: "Hawalas",
                columns: new[] { "TenantId", "FromCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_HawalaType_Status",
                table: "Hawalas",
                columns: new[] { "TenantId", "HawalaType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_PaymentLocationId",
                table: "Hawalas",
                columns: new[] { "TenantId", "PaymentLocationId" });
        }
    }
}
