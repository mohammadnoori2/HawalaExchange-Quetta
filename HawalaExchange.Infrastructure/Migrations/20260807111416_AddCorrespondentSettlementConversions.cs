using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrespondentSettlementConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SettlementCurrencyId",
                table: "Correspondents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                    TargetCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversions", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentSettlementConversions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Currencies_TenantId_TargetCurrencyId",
                        columns: x => new { x.TenantId, x.TargetCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversionHawalas",
                columns: table => new
                {
                    ConversionId = table.Column<long>(type: "bigint", nullable: false),
                    HawalaId = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversionHawalas", x => new { x.ConversionId, x.HawalaId });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalas_CorrespondentSettlementConversions_TenantId_ConversionId",
                        columns: x => new { x.TenantId, x.ConversionId },
                        principalTable: "CorrespondentSettlementConversions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalas_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversionItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ConversionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    SourceTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SourceBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TargetTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TargetBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversionItems", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentSettlementConversionItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionItems_CorrespondentSettlementConversions_TenantId_ConversionId",
                        columns: x => new { x.TenantId, x.ConversionId },
                        principalTable: "CorrespondentSettlementConversions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionItems_Currencies_TenantId_SourceCurrencyId",
                        columns: x => new { x.TenantId, x.SourceCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Correspondents_TenantId_SettlementCurrencyId",
                table: "Correspondents",
                columns: new[] { "TenantId", "SettlementCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalas_TenantId_ConversionId",
                table: "CorrespondentSettlementConversionHawalas",
                columns: new[] { "TenantId", "ConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalas_TenantId_HawalaId",
                table: "CorrespondentSettlementConversionHawalas",
                columns: new[] { "TenantId", "HawalaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionItems_TenantId_ConversionId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionItems",
                columns: new[] { "TenantId", "ConversionId", "SourceCurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionItems_TenantId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionItems",
                columns: new[] { "TenantId", "SourceCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_CorrespondentId_CreatedAt",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "CorrespondentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_CreatedBy",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_TargetCurrencyId",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "TargetCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_TransactionId",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Correspondents_Currencies_TenantId_SettlementCurrencyId",
                table: "Correspondents",
                columns: new[] { "TenantId", "SettlementCurrencyId" },
                principalTable: "Currencies",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Correspondents_Currencies_TenantId_SettlementCurrencyId",
                table: "Correspondents");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversionHawalas");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversionItems");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversions");

            migrationBuilder.DropIndex(
                name: "IX_Correspondents_TenantId_SettlementCurrencyId",
                table: "Correspondents");

            migrationBuilder.DropColumn(
                name: "SettlementCurrencyId",
                table: "Correspondents");
        }
    }
}
