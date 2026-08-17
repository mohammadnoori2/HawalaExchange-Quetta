using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerHawalaSettlementRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SettlementHawalaItemId",
                table: "LedgerEntries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversionHawalaItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ConversionId = table.Column<long>(type: "bigint", nullable: false),
                    HawalaId = table.Column<long>(type: "bigint", nullable: false),
                    SourceCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    SourceTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SourceBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TargetTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TargetBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversionHawalaItems", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentSettlementConversionHawalaItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalaItems_CorrespondentSettlementConversions_TenantId_ConversionId",
                        columns: x => new { x.TenantId, x.ConversionId },
                        principalTable: "CorrespondentSettlementConversions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalaItems_Currencies_TenantId_SourceCurrencyId",
                        columns: x => new { x.TenantId, x.SourceCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalaItems_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_SettlementHawalaItemId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "SettlementHawalaItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalaItems_TenantId_ConversionId",
                table: "CorrespondentSettlementConversionHawalaItems",
                columns: new[] { "TenantId", "ConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalaItems_TenantId_HawalaId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionHawalaItems",
                columns: new[] { "TenantId", "HawalaId", "SourceCurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalaItems_TenantId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionHawalaItems",
                columns: new[] { "TenantId", "SourceCurrencyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntries_CorrespondentSettlementConversionHawalaItems_TenantId_SettlementHawalaItemId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "SettlementHawalaItemId" },
                principalTable: "CorrespondentSettlementConversionHawalaItems",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntries_CorrespondentSettlementConversionHawalaItems_TenantId_SettlementHawalaItemId",
                table: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversionHawalaItems");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_TenantId_SettlementHawalaItemId",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "SettlementHawalaItemId",
                table: "LedgerEntries");
        }
    }
}
