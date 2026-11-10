using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261031180000_HawalaCancellationWorkflow")]
public sealed class HawalaCancellationWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "CancellationAccountingMode", table: "Hawalas", type: "nvarchar(24)", maxLength: 24, nullable: true);
        migrationBuilder.AddColumn<long>(name: "CompensatesHawalaId", table: "Hawalas", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Hawalas_TenantId_CompensatesHawalaId", table: "Hawalas",
            columns: new[] { "TenantId", "CompensatesHawalaId" }, unique: true, filter: "[CompensatesHawalaId] IS NOT NULL");
        migrationBuilder.AddForeignKey(name: "FK_Hawalas_Hawalas_TenantId_CompensatesHawalaId", table: "Hawalas",
            columns: new[] { "TenantId", "CompensatesHawalaId" }, principalTable: "Hawalas", principalColumns: new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Hawalas_Hawalas_TenantId_CompensatesHawalaId", "Hawalas");
        migrationBuilder.DropIndex("IX_Hawalas_TenantId_CompensatesHawalaId", "Hawalas");
        migrationBuilder.DropColumn("CompensatesHawalaId", "Hawalas");
        migrationBuilder.DropColumn("CancellationAccountingMode", "Hawalas");
    }
}
