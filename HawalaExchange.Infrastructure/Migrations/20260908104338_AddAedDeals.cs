using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAedDeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AedDeals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    DealNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceCorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                    DubaiCorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                    SourceCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ConvertedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalFinalUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalProfitUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AedPerUsdRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    RoundingDecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HoldingTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    ReversalTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AedDeals", x => x.Id);
                    table.UniqueConstraint("AK_AedDeals_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_AedDeals_Amount_Valid", "[OriginalAmount] > 0 AND [ConvertedAmount] >= 0 AND [ConvertedAmount] <= [OriginalAmount]");
                    table.CheckConstraint("CK_AedDeals_Correspondents_Different", "[SourceCorrespondentId] <> [DubaiCorrespondentId]");
                    table.CheckConstraint("CK_AedDeals_Rounding_Valid", "[RoundingDecimalPlaces] BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_AedDeals_Status_Valid", "[Status] IN ('Held', 'PartiallyConverted', 'Converted', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_AedDeals_Correspondents_TenantId_DubaiCorrespondentId",
                        columns: x => new { x.TenantId, x.DubaiCorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDeals_Correspondents_TenantId_SourceCorrespondentId",
                        columns: x => new { x.TenantId, x.SourceCorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDeals_Currencies_TenantId_SourceCurrencyId",
                        columns: x => new { x.TenantId, x.SourceCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDeals_Transactions_TenantId_HoldingTransactionId",
                        columns: x => new { x.TenantId, x.HoldingTransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDeals_Transactions_TenantId_ReversalTransactionId",
                        columns: x => new { x.TenantId, x.ReversalTransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDeals_Users_TenantId_CancelledBy",
                        columns: x => new { x.TenantId, x.CancelledBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDeals_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AedDealConversions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AedDealId = table.Column<long>(type: "bigint", nullable: false),
                    SourceAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AedPerUsdRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    ActualMarker = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DeclaredMarker = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ActualAdjustmentSource = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DeclaredAdjustmentSource = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    FinalUsdAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DeclaredUsdAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ProfitUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PostingTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    ReversalTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AedDealConversions", x => x.Id);
                    table.UniqueConstraint("AK_AedDealConversions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_AedDealConversions_Amount_Valid", "[SourceAmount] > 0");
                    table.CheckConstraint("CK_AedDealConversions_Status_Valid", "[Status] IN ('Posted', 'Reversed')");
                    table.ForeignKey(
                        name: "FK_AedDealConversions_AedDeals_TenantId_AedDealId",
                        columns: x => new { x.TenantId, x.AedDealId },
                        principalTable: "AedDeals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AedDealConversions_Transactions_TenantId_PostingTransactionId",
                        columns: x => new { x.TenantId, x.PostingTransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDealConversions_Transactions_TenantId_ReversalTransactionId",
                        columns: x => new { x.TenantId, x.ReversalTransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDealConversions_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AedDealConversions_Users_TenantId_ReversedBy",
                        columns: x => new { x.TenantId, x.ReversedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AedDealConversions_TenantId_AedDealId_CreatedAt",
                table: "AedDealConversions",
                columns: new[] { "TenantId", "AedDealId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDealConversions_TenantId_CreatedBy",
                table: "AedDealConversions",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDealConversions_TenantId_PostingTransactionId",
                table: "AedDealConversions",
                columns: new[] { "TenantId", "PostingTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDealConversions_TenantId_ReversalTransactionId",
                table: "AedDealConversions",
                columns: new[] { "TenantId", "ReversalTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDealConversions_TenantId_ReversedBy",
                table: "AedDealConversions",
                columns: new[] { "TenantId", "ReversedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_CancelledBy",
                table: "AedDeals",
                columns: new[] { "TenantId", "CancelledBy" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_CreatedBy",
                table: "AedDeals",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_DealNumber",
                table: "AedDeals",
                columns: new[] { "TenantId", "DealNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_DubaiCorrespondentId_CreatedAt",
                table: "AedDeals",
                columns: new[] { "TenantId", "DubaiCorrespondentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_HoldingTransactionId",
                table: "AedDeals",
                columns: new[] { "TenantId", "HoldingTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_ReversalTransactionId",
                table: "AedDeals",
                columns: new[] { "TenantId", "ReversalTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_SourceCorrespondentId_CreatedAt",
                table: "AedDeals",
                columns: new[] { "TenantId", "SourceCorrespondentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AedDeals_TenantId_SourceCurrencyId",
                table: "AedDeals",
                columns: new[] { "TenantId", "SourceCurrencyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AedDealConversions");

            migrationBuilder.DropTable(
                name: "AedDeals");
        }
    }
}
