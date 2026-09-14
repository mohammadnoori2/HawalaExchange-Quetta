using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPairedHawalaBulkImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AgentCommissionAmount",
                table: "HawalaImportRows",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentCommissionCurrencyCode",
                table: "HawalaImportRows",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AgentCommissionCurrencyId",
                table: "HawalaImportRows",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DestinationCorrespondentId",
                table: "HawalaImportRows",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GeneratedSendHawalaId",
                table: "HawalaImportRows",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OwnPaymentLocationId",
                table: "HawalaImportBatches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OwnPaymentLocationId",
                table: "CompanySettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportRows_TenantId_AgentCommissionCurrencyId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "AgentCommissionCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportRows_TenantId_DestinationCorrespondentId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "DestinationCorrespondentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportRows_TenantId_GeneratedSendHawalaId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "GeneratedSendHawalaId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_TenantId_OwnPaymentLocationId",
                table: "CompanySettings",
                columns: new[] { "TenantId", "OwnPaymentLocationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_PaymentLocations_TenantId_OwnPaymentLocationId",
                table: "CompanySettings",
                columns: new[] { "TenantId", "OwnPaymentLocationId" },
                principalTable: "PaymentLocations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HawalaImportRows_Correspondents_TenantId_DestinationCorrespondentId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "DestinationCorrespondentId" },
                principalTable: "Correspondents",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HawalaImportRows_Currencies_TenantId_AgentCommissionCurrencyId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "AgentCommissionCurrencyId" },
                principalTable: "Currencies",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HawalaImportRows_Hawalas_TenantId_GeneratedSendHawalaId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "GeneratedSendHawalaId" },
                principalTable: "Hawalas",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanySettings_PaymentLocations_TenantId_OwnPaymentLocationId",
                table: "CompanySettings");

            migrationBuilder.DropForeignKey(
                name: "FK_HawalaImportRows_Correspondents_TenantId_DestinationCorrespondentId",
                table: "HawalaImportRows");

            migrationBuilder.DropForeignKey(
                name: "FK_HawalaImportRows_Currencies_TenantId_AgentCommissionCurrencyId",
                table: "HawalaImportRows");

            migrationBuilder.DropForeignKey(
                name: "FK_HawalaImportRows_Hawalas_TenantId_GeneratedSendHawalaId",
                table: "HawalaImportRows");

            migrationBuilder.DropIndex(
                name: "IX_HawalaImportRows_TenantId_AgentCommissionCurrencyId",
                table: "HawalaImportRows");

            migrationBuilder.DropIndex(
                name: "IX_HawalaImportRows_TenantId_DestinationCorrespondentId",
                table: "HawalaImportRows");

            migrationBuilder.DropIndex(
                name: "IX_HawalaImportRows_TenantId_GeneratedSendHawalaId",
                table: "HawalaImportRows");

            migrationBuilder.DropIndex(
                name: "IX_CompanySettings_TenantId_OwnPaymentLocationId",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "AgentCommissionAmount",
                table: "HawalaImportRows");

            migrationBuilder.DropColumn(
                name: "AgentCommissionCurrencyCode",
                table: "HawalaImportRows");

            migrationBuilder.DropColumn(
                name: "AgentCommissionCurrencyId",
                table: "HawalaImportRows");

            migrationBuilder.DropColumn(
                name: "DestinationCorrespondentId",
                table: "HawalaImportRows");

            migrationBuilder.DropColumn(
                name: "GeneratedSendHawalaId",
                table: "HawalaImportRows");

            migrationBuilder.DropColumn(
                name: "OwnPaymentLocationId",
                table: "HawalaImportBatches");

            migrationBuilder.DropColumn(
                name: "OwnPaymentLocationId",
                table: "CompanySettings");
        }
    }
}
