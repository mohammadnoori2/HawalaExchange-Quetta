using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHawalaBulkImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HawalaImportBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Preview"),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HawalaImportBatches", x => x.Id);
                    table.UniqueConstraint("AK_HawalaImportBatches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_HawalaImportBatches_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HawalaImportBatches_Users_TenantId_ConfirmedBy",
                        columns: x => new { x.TenantId, x.ConfirmedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HawalaImportBatches_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HawalaImportRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    BatchId = table.Column<long>(type: "bigint", nullable: false),
                    ExcelRowNumber = table.Column<int>(type: "int", nullable: false),
                    HawalaNumber = table.Column<long>(type: "bigint", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SenderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentLocationText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentLocationId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    ValidationErrors = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HawalaId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HawalaImportRows", x => x.Id);
                    table.UniqueConstraint("AK_HawalaImportRows_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_HawalaImportRows_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HawalaImportRows_HawalaImportBatches_TenantId_BatchId",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "HawalaImportBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HawalaImportRows_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HawalaImportRows_PaymentLocations_TenantId_PaymentLocationId",
                        columns: x => new { x.TenantId, x.PaymentLocationId },
                        principalTable: "PaymentLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportBatches_TenantId_ConfirmedBy",
                table: "HawalaImportBatches",
                columns: new[] { "TenantId", "ConfirmedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportBatches_TenantId_CorrespondentId",
                table: "HawalaImportBatches",
                columns: new[] { "TenantId", "CorrespondentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportBatches_TenantId_CreatedBy",
                table: "HawalaImportBatches",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportBatches_TenantId_FileHash",
                table: "HawalaImportBatches",
                columns: new[] { "TenantId", "FileHash" },
                unique: true,
                filter: "[Status] = 'Posted'");

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportBatches_TenantId_FileHash_Status",
                table: "HawalaImportBatches",
                columns: new[] { "TenantId", "FileHash", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportRows_TenantId_BatchId_ExcelRowNumber",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "BatchId", "ExcelRowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportRows_TenantId_CurrencyId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportRows_TenantId_HawalaId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "HawalaId" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportRows_TenantId_PaymentLocationId",
                table: "HawalaImportRows",
                columns: new[] { "TenantId", "PaymentLocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HawalaImportRows");

            migrationBuilder.DropTable(
                name: "HawalaImportBatches");
        }
    }
}
