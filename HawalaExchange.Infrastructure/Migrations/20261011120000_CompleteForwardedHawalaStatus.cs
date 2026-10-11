using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261011120000_CompleteForwardedHawalaStatus")]
public sealed class CompleteForwardedHawalaStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE sent
        SET Status = 'Paid',
            PaidAt = COALESCE(sent.PaidAt, source.PaidAt, sent.CreatedAt),
            PaidBy = COALESCE(sent.PaidBy, source.PaidBy, sent.CreatedBy)
        FROM dbo.Hawalas sent
        INNER JOIN dbo.Hawalas source ON source.Id = sent.SourceHawalaId AND source.TenantId = sent.TenantId
        INNER JOIN dbo.Accounts destination ON destination.Id = sent.PaidFromAccountId AND destination.TenantId = sent.TenantId
        WHERE sent.IsSystemGenerated = 1 AND sent.HawalaType = 'HawalaSend' AND sent.Status = 'Pending'
          AND source.HawalaType = 'HawalaReceive' AND source.Status = 'Paid'
          AND destination.AccountType = 'Correspondent' AND destination.CorrespondentId = sent.CorrespondentId
          AND EXISTS (SELECT 1 FROM dbo.LedgerEntries entry
                      WHERE entry.TenantId = sent.TenantId AND entry.HawalaId = sent.Id
                        AND entry.AccountId = destination.Id AND entry.TalabKar > 0);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not reopen settled transfers or alter their accounting on rollback.
    }
}
