using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261031190000_CorrespondentOpeningBalanceChanges")]
public sealed class CorrespondentOpeningBalanceChanges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "CorrespondentOpeningBalanceChanges", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            OriginalLedgerEntryId = table.Column<long>(type: "bigint", nullable: false),
            ReversalTransactionId = table.Column<long>(type: "bigint", nullable: false),
            ReplacementTransactionId = table.Column<long>(type: "bigint", nullable: true),
            ChangedBy = table.Column<long>(type: "bigint", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            Reason = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CorrespondentOpeningBalanceChanges", x => x.Id);
            table.UniqueConstraint("AK_CorrespondentOpeningBalanceChanges_TenantId_Id", x => new { x.TenantId, x.Id });
            table.ForeignKey("FK_CorrespondentOpeningBalanceChanges_LedgerEntries_TenantId_OriginalLedgerEntryId", x => new { x.TenantId, x.OriginalLedgerEntryId }, "LedgerEntries", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CorrespondentOpeningBalanceChanges_Transactions_TenantId_ReversalTransactionId", x => new { x.TenantId, x.ReversalTransactionId }, "Transactions", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CorrespondentOpeningBalanceChanges_Transactions_TenantId_ReplacementTransactionId", x => new { x.TenantId, x.ReplacementTransactionId }, "Transactions", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_CorrespondentOpeningBalanceChanges_TenantId_OriginalLedgerEntryId", "CorrespondentOpeningBalanceChanges", new[] { "TenantId", "OriginalLedgerEntryId" }, unique: true);
        migrationBuilder.CreateIndex("IX_CorrespondentOpeningBalanceChanges_TenantId_ReversalTransactionId", "CorrespondentOpeningBalanceChanges", new[] { "TenantId", "ReversalTransactionId" });
        migrationBuilder.CreateIndex("IX_CorrespondentOpeningBalanceChanges_TenantId_ReplacementTransactionId", "CorrespondentOpeningBalanceChanges", new[] { "TenantId", "ReplacementTransactionId" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("CorrespondentOpeningBalanceChanges");
}
