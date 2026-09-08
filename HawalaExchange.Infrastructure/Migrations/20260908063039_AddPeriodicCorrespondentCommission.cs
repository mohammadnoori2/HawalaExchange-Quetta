using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicCorrespondentCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommissionMethod",
                table: "Correspondents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PerTransaction");

            migrationBuilder.CreateTable(
                name: "CorrespondentCommissionBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommissionPerLakhAfn = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UsdToAfnRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TotalBaseAfn = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalCommissionAfn = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalCommissionUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Posted"),
                    PostingTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    ReversalTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReversedBy = table.Column<long>(type: "bigint", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentCommissionBatches", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentCommissionBatches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_CorrespondentCommissionBatches_Status_Valid", "[Status] IN ('Posted', 'Reversed')");
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatches_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatches_Transactions_TenantId_PostingTransactionId",
                        columns: x => new { x.TenantId, x.PostingTransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatches_Transactions_TenantId_ReversalTransactionId",
                        columns: x => new { x.TenantId, x.ReversalTransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatches_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatches_Users_TenantId_ReversedBy",
                        columns: x => new { x.TenantId, x.ReversedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentCommissionBatchItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    BatchId = table.Column<long>(type: "bigint", nullable: false),
                    HawalaId = table.Column<long>(type: "bigint", nullable: false),
                    SourceCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    SourceAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SourceToAfnRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    AfnEquivalent = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CommissionAfn = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentCommissionBatchItems", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentCommissionBatchItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatchItems_CorrespondentCommissionBatches_TenantId_BatchId",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "CorrespondentCommissionBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatchItems_Currencies_TenantId_SourceCurrencyId",
                        columns: x => new { x.TenantId, x.SourceCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentCommissionBatchItems_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Correspondents_CommissionMethod_Valid",
                table: "Correspondents",
                sql: "[CommissionMethod] IN ('PerTransaction', 'PeriodicPerLakh')");

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatches_TenantId_CorrespondentId_CreatedAt",
                table: "CorrespondentCommissionBatches",
                columns: new[] { "TenantId", "CorrespondentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatches_TenantId_CreatedBy",
                table: "CorrespondentCommissionBatches",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatches_TenantId_PostingTransactionId",
                table: "CorrespondentCommissionBatches",
                columns: new[] { "TenantId", "PostingTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatches_TenantId_ReversalTransactionId",
                table: "CorrespondentCommissionBatches",
                columns: new[] { "TenantId", "ReversalTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatches_TenantId_ReversedBy",
                table: "CorrespondentCommissionBatches",
                columns: new[] { "TenantId", "ReversedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatchItems_TenantId_BatchId",
                table: "CorrespondentCommissionBatchItems",
                columns: new[] { "TenantId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatchItems_TenantId_HawalaId_IsActive",
                table: "CorrespondentCommissionBatchItems",
                columns: new[] { "TenantId", "HawalaId", "IsActive" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentCommissionBatchItems_TenantId_SourceCurrencyId",
                table: "CorrespondentCommissionBatchItems",
                columns: new[] { "TenantId", "SourceCurrencyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrespondentCommissionBatchItems");

            migrationBuilder.DropTable(
                name: "CorrespondentCommissionBatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Correspondents_CommissionMethod_Valid",
                table: "Correspondents");

            migrationBuilder.DropColumn(
                name: "CommissionMethod",
                table: "Correspondents");
        }
    }
}
