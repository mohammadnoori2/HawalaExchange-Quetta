using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class CommissionMigrationUpgradeTests
{
    [Fact]
    public async Task Deferred_upgrade_recognizes_already_closed_commissions_without_changing_old_journals()
    {
        var database = new SqlServerPerformanceFixture();
        try
        {
            await database.InitializeDatabaseAsync("20261002140000_DestinationCommissionLocationRates");
            await using var context = database.CreateContext();
            using var bypass = context.BypassSubscriptionEnforcement();
            var now = DateTime.UtcNow;
            var beforeClose = new Transaction { TransactionNo = "DEF-UPGRADE-OLD", TransactionType = "PeriodicCorrespondentCommission", Status = "Paid", BranchId = 1, CreatedBy = database.UserId, CreatedAt = now.AddMinutes(-10) };
            var afterClose = new Transaction { TransactionNo = "DEF-UPGRADE-NEW", TransactionType = "PeriodicCorrespondentCommission", Status = "Paid", BranchId = 1, CreatedBy = database.UserId, CreatedAt = now.AddMinutes(-1) };
            context.Transactions.AddRange(beforeClose, afterClose);
            var period = new CorrespondentAccountPeriod { CorrespondentId = database.SourceCorrespondent.Id, PeriodNumber = 1,
                PeriodFrom = now.AddHours(-1), PeriodTo = now.AddMinutes(-5), ClosedAt = now.AddMinutes(-5), ClosedBy = database.UserId };
            context.CorrespondentAccountPeriods.Add(period);
            await context.SaveChangesAsync();
            context.LedgerEntries.AddRange(
                new LedgerEntry { AccountId = database.SourceAccount.Id, CurrencyId = 2, TalabKar = 1_000m, CreatedAt = now.AddMinutes(-15) },
                new LedgerEntry { AccountId = database.SourceAccount.Id, CurrencyId = 2, BadehKar = 100m, TransactionId = beforeClose.Id, CreatedAt = beforeClose.CreatedAt },
                new LedgerEntry { AccountId = database.SourceAccount.Id, CurrencyId = 2, BadehKar = 40m, TransactionId = afterClose.Id, CreatedAt = afterClose.CreatedAt });
            context.CorrespondentAccountPeriodBalances.Add(new() { PeriodId = period.Id, CurrencyId = 2, TalabKar = 900m });
            await context.SaveChangesAsync();
            var original = await context.LedgerEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AccountId, x.CurrencyId, x.TalabKar, x.BadehKar }).ToListAsync();
            await context.Database.MigrateAsync();
            Assert.Equal(original, await context.LedgerEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AccountId, x.CurrencyId, x.TalabKar, x.BadehKar }).ToListAsync());
            var current = Assert.Single(await database.CreateBalanceService(context).GetAccountBalanceAsync(database.SourceAccount.Id));
            Assert.Equal(900m, current.Balance);
            Assert.Equal(40m, current.PendingCommissionDebit);
            Assert.Equal(860m, current.TotalIncludingCommission);
            Assert.Equal(900m, (await context.CorrespondentAccountPeriodBalances.SingleAsync()).TalabKar);
        }
        finally { await database.DisposeAsync(); }
    }

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
