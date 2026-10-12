using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261012120000_SettlementBasedCommissionWorkflow")]
public sealed class SettlementBasedCommissionWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ApplyResource(migrationBuilder, "SettlementCommissionWorkflow.sql");
        var offset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes;
        migrationBuilder.Sql($"""
            DECLARE @id bigint,@tenant bigint,@user bigint;
            DECLARE conversions CURSOR LOCAL FAST_FORWARD FOR
                SELECT c.Id,c.TenantId,c.CreatedBy FROM dbo.CorrespondentSettlementConversions c
                JOIN dbo.Transactions t ON t.TenantId=c.TenantId AND t.Id=c.TransactionId
                WHERE t.Status<>N'Cancel' ORDER BY c.CreatedAt,c.Id;
            OPEN conversions; FETCH NEXT FROM conversions INTO @id,@tenant,@user;
            WHILE @@FETCH_STATUS=0
            BEGIN
                EXEC dbo.usp_CaptureCorrespondentSettlement_v1 @tenant,@id,@user,NULL,{offset},1,1;
                FETCH NEXT FROM conversions INTO @id,@tenant,@user;
            END;
            CLOSE conversions; DEALLOCATE conversions;
            """);
        const string resource = "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
        using var stream = typeof(SettlementBasedCommissionWorkflow).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var procedure = Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(x => x.Contains("PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]", StringComparison.OrdinalIgnoreCase));
        migrationBuilder.Sql(Regex.Replace(procedure.Trim(), @"^\s*CREATE\s+PROCEDURE", "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase));
    }

    private static void ApplyResource(MigrationBuilder builder, string name)
    {
        using var stream = typeof(SettlementBasedCommissionWorkflow).Assembly.GetManifestResourceStream("HawalaExchange.Infrastructure.Sql." + name)!;
        using var reader = new StreamReader(stream);
        foreach (var batch in Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
            builder.Sql(batch);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Restore a verified backup to roll back settlement accounting snapshots.");
}
