using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations;

public partial class AddPaymentLocationCorrespondentAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PaymentLocationCorrespondentAssignments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PaymentLocationId = table.Column<long>(type: "bigint", nullable: false),
                CorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentLocationCorrespondentAssignments", x => x.Id);
                table.UniqueConstraint("AK_PaymentLocationCorrespondentAssignments_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey(
                    name: "FK_PaymentLocationCorrespondentAssignments_Correspondents_TenantId_CorrespondentId",
                    columns: x => new { x.TenantId, x.CorrespondentId },
                    principalTable: "Correspondents",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PaymentLocationCorrespondentAssignments_PaymentLocations_TenantId_PaymentLocationId",
                    columns: x => new { x.TenantId, x.PaymentLocationId },
                    principalTable: "PaymentLocations",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentLocationCorrespondentAssignments_TenantId_CorrespondentId_EffectiveFrom",
            table: "PaymentLocationCorrespondentAssignments",
            columns: new[] { "TenantId", "CorrespondentId", "EffectiveFrom" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentLocationCorrespondentAssignments_TenantId_PaymentLocationId_EffectiveFrom",
            table: "PaymentLocationCorrespondentAssignments",
            columns: new[] { "TenantId", "PaymentLocationId", "EffectiveFrom" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "PaymentLocationCorrespondentAssignments");
}
