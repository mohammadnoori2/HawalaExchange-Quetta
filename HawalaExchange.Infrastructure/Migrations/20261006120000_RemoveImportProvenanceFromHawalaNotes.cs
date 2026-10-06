using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261006120000_RemoveImportProvenanceFromHawalaNotes")]
public sealed class RemoveImportProvenanceFromHawalaNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE h SET Notes = NULL
        FROM dbo.Hawalas h
        WHERE EXISTS (
            SELECT 1 FROM dbo.HawalaImportRows r
            INNER JOIN dbo.HawalaImportBatches b ON b.Id = r.BatchId AND b.TenantId = r.TenantId
            WHERE r.TenantId = h.TenantId
              AND (r.HawalaId = h.Id OR r.GeneratedSendHawalaId = h.Id)
              AND h.Notes COLLATE Latin1_General_100_BIN2 =
                  (N'آپلود گروهی از فایل ' + b.FileName) COLLATE Latin1_General_100_BIN2
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Provenance remains recoverable from batches/rows. Do not overwrite user notes on rollback.
    }
}
