using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class CommissionMigrationUpgradeTests
{
    [Fact]
    public async Task Upgrade_preserves_old_journals_and_blocks_duplicate_historical_commissions()
    {
        // A separate disposable database, never the user's configured application database.
        var database = new SqlServerPerformanceFixture();
        try
        {
            await database.InitializeDatabaseAsync("20260930130000_SplitOriginDestinationCommissions");
            await using var context = database.CreateContext();
            using var bypass = context.BypassSubscriptionEnforcement();
            var day = new DateTime(2039, 1, 10);
            var source = new Hawala
            {
                Number = 99_190_001, HawalaType = "HawalaReceive", CorrespondentId = database.SourceCorrespondent.Id,
                FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 100_000m, ToAmount = 100_000m,
                PaymentLocationId = database.RemoteLocation.Id, CreatedBy = database.UserId,
                CreatedAt = day.AddHours(10).ToUniversalTime(), Status = "Paid"
            };
            context.Hawalas.Add(source);
            await context.SaveChangesAsync();
            context.Hawalas.Add(new Hawala
            {
                Number = 99_190_002, HawalaType = "HawalaSend", CorrespondentId = database.DestinationCorrespondent.Id,
                SourceHawalaId = source.Id, FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 50_000m, ToAmount = 50_000m,
                PaymentLocationId = database.RemoteLocation.Id, CreatedBy = database.UserId,
                CreatedAt = day.AddHours(10).ToUniversalTime(), Status = "Paid"
            });
            var destination = await context.Correspondents.SingleAsync(x => x.Id == database.DestinationCorrespondent.Id);
            destination.CommissionMethod = "PeriodicPerLakh";
            context.CompanySettings.Add(new CompanySetting { CompanyName = "Upgrade test", OwnPaymentLocationId = database.OwnLocation.Id });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var service = database.CreateCommissionService(context);
            var incoming = new CorrespondentCommissionPreviewRequestDto
            {
                CorrespondentId = database.SourceCorrespondent.Id, PeriodFrom = day, PeriodTo = day,
                CommissionScope = "Standard", HawalaType = "HawalaReceive", CommissionPerLakhAfn = 200m
            };
            var outgoing = new CorrespondentCommissionPreviewRequestDto
            {
                CorrespondentId = database.DestinationCorrespondent.Id, PeriodFrom = day, PeriodTo = day,
                CommissionScope = "Standard", HawalaType = "HawalaSend", CommissionPerLakhAfn = 200m
            };
            var oldIncoming = await service.PostAsync(incoming);
            var oldOutgoing = await service.PostAsync(outgoing);
            var before = await context.LedgerEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AccountId, x.CurrencyId, x.TalabKar, x.BadehKar }).ToListAsync();

            // Emulate the exact pre-upgrade shape. Only these three new columns/defaults
            // are removed from this test-owned, validated disposable database.
            await context.Database.ExecuteSqlRawAsync("""
                DECLARE @ddl nvarchar(max);
                SELECT @ddl = STRING_AGG(CAST(N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
                    + N' DROP CONSTRAINT ' + QUOTENAME(d.name) + N';' AS nvarchar(max)), CHAR(10))
                FROM sys.default_constraints d JOIN sys.tables t ON t.object_id = d.parent_object_id
                JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = d.parent_column_id
                WHERE (t.name = N'CorrespondentCommissionBatches' AND c.name = N'AccountingVersion')
                   OR (t.name = N'CorrespondentCommissionBatchItems' AND c.name = N'CommissionScope');
                EXEC sp_executesql @ddl;
                ALTER TABLE [dbo].[CorrespondentCommissionBatches] DROP COLUMN [AccountingVersion];
                ALTER TABLE [dbo].[CorrespondentCommissionBatchItems] DROP COLUMN [CommissionScope], [ValuationDate];
                """);
            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();
            var after = await context.LedgerEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AccountId, x.CurrencyId, x.TalabKar, x.BadehKar }).ToListAsync();
            Assert.Equal(before, after);
            Assert.Equal(1, (await service.GetDetailsAsync(oldIncoming.Id)).AccountingVersion);
            Assert.Equal(1, (await service.GetDetailsAsync(oldOutgoing.Id)).AccountingVersion);
            Assert.Contains((await service.GetDetailsAsync(oldOutgoing.Id)).LedgerEntries,
                x => x.AccountId == database.SourceAccount.Id && x.BadehKar == 100m);
            incoming.CommissionScope = "Incoming";
            outgoing.CommissionScope = "Destination";
            Assert.Empty((await service.PreviewAsync(incoming)).Items);
            Assert.Empty((await service.PreviewAsync(outgoing)).Items);
            var forwarding = new CorrespondentCommissionPreviewRequestDto
            {
                CorrespondentId = database.SourceCorrespondent.Id, PeriodFrom = day, PeriodTo = day,
                CommissionScope = "Forwarding", HawalaType = "HawalaSend", CommissionPerLakhAfn = 400m,
                PaymentLocationRates = [new() { PaymentLocationId = database.RemoteLocation.Id, PerLakhRate = 400m }]
            };
            Assert.Equal(200m, (await service.PostAsync(forwarding)).TotalCommissionUsd);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
