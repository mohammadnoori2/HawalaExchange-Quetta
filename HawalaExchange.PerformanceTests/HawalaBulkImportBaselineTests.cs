using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class HawalaBulkImportBaselineTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Bulk_import_preserves_hawala_links_and_accounting_balances()
    {
        const int rowCount = 100;
        const long numberBase = 10_000_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var service = fixture.CreateService(context);
        var items = BuildItems(rowCount, numberBase);

        fixture.Commands.Reset();
        var stopwatch = Stopwatch.StartNew();
        var result = await service.CreateHawalasAsync(items);
        stopwatch.Stop();
        var commandCount = fixture.Commands.Count;

        context.ChangeTracker.Clear();
        var received = await InRange(context.Hawalas.AsNoTracking(), "HawalaReceive", numberBase, rowCount)
            .ToListAsync();
        var receivedIds = received.Select(x => x.Id).ToArray();
        var generated = await context.Hawalas.AsNoTracking()
            .Where(x => x.IsSystemGenerated && x.SourceHawalaId.HasValue && receivedIds.Contains(x.SourceHawalaId.Value))
            .ToListAsync();
        var generatedIds = generated.Select(x => x.Id).ToArray();
        var allIds = receivedIds.Concat(generatedIds).ToArray();
        var ledger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.HawalaId.HasValue && allIds.Contains(x.HawalaId.Value))
            .ToListAsync();

        var remoteItems = items.Where(x => x.Status == "Paid").ToList();
        var commissionedItems = remoteItems.Where(x => x.GeneratedSendAgentCommissionAmount is > 0).ToList();
        var localItems = items.Where(x => x.Status != "Paid").ToList();
        var totalAmount = items.Sum(x => x.FromAmount);
        var remoteAmount = remoteItems.Sum(x => x.FromAmount);
        var localAmount = localItems.Sum(x => x.FromAmount);
        var commissionAmount = commissionedItems.Sum(x => x.GeneratedSendAgentCommissionAmount!.Value);
        var pendingAccountId = await context.Accounts
            .Where(x => x.AccountCode == "2102")
            .Select(x => x.Id)
            .SingleAsync();
        var commissionExpenseAccountId = await context.Accounts
            .Where(x => x.AccountCode == "4002")
            .Select(x => x.Id)
            .SingleAsync();

        Assert.Equal(rowCount, result.Count);
        Assert.Equal(rowCount, received.Count);
        Assert.Equal(remoteItems.Count, generated.Count);
        Assert.All(received, x =>
        {
            Assert.Null(x.CommissionAmount);
            Assert.Null(x.AgentCommissionAmount);
        });
        Assert.All(generated, x => Assert.Contains(x.SourceHawalaId!.Value, receivedIds));
        Assert.Equal(2 * rowCount + 2 * remoteItems.Count + 2 * commissionedItems.Count, ledger.Count);
        Assert.Equal(totalAmount, ledger.Where(x => x.AccountId == fixture.SourceAccount.Id).Sum(x => x.BadehKar));
        Assert.Equal(localAmount,
            ledger.Where(x => x.AccountId == pendingAccountId).Sum(x => x.TalabKar - x.BadehKar));
        Assert.Equal(remoteAmount + commissionAmount,
            ledger.Where(x => x.AccountId == fixture.DestinationAccount.Id).Sum(x => x.TalabKar));
        Assert.Equal(commissionAmount,
            ledger.Where(x => x.AccountId == commissionExpenseAccountId).Sum(x => x.BadehKar));

        WriteResult(rowCount, stopwatch.Elapsed, commandCount, received.Count, generated.Count, ledger.Count);
    }

    [Fact]
    public async Task Bulk_import_rolls_back_everything_when_generated_numbers_conflict()
    {
        const long numberBase = 20_000_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var service = fixture.CreateService(context);
        var beforeHawalas = await context.Hawalas.CountAsync();
        var beforeLedger = await context.LedgerEntries.CountAsync();
        var items = BuildItems(2, numberBase, forceRemote: true).ToList();
        items[0].GeneratedSendHawalaNumber = 29_999_999;
        items[1].GeneratedSendHawalaNumber = 29_999_999;

        await Assert.ThrowsAsync<SqlException>(() => service.CreateHawalasAsync(items));

        context.ChangeTracker.Clear();
        Assert.Equal(beforeHawalas, await context.Hawalas.CountAsync());
        Assert.Equal(beforeLedger, await context.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Bulk_import_rejects_entities_owned_by_another_tenant()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var secondTenant = new Tenant { Name = "Performance Tenant 2", IsActive = true };
        context.Tenants.Add(secondTenant);
        await context.SaveChangesAsync();

        Correspondent foreignCorrespondent;
        using (context.UseTenantScope(secondTenant.Id))
        {
            foreignCorrespondent = new Correspondent
            {
                Code = "PERF-FOREIGN",
                Name = "Foreign Correspondent",
                CommissionMethod = "PerTransaction"
            };
            context.Correspondents.Add(foreignCorrespondent);
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();
        Assert.False(await context.Correspondents.AnyAsync(x => x.Id == foreignCorrespondent.Id));
        using (context.UseTenantScope(secondTenant.Id))
            Assert.True(await context.Correspondents.AnyAsync(x => x.Id == foreignCorrespondent.Id));

        var item = BuildItems(1, 30_000_000).Single();
        item.CorrespondentId = foreignCorrespondent.Id;
        var beforeHawalas = await context.Hawalas.CountAsync();
        var service = fixture.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateHawalasAsync([item]));
        Assert.Equal(beforeHawalas, await context.Hawalas.CountAsync());
    }

    [Fact]
    public async Task Measure_bulk_import_for_100_1000_and_10000_rows()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Performance samples were not run. Set RUN_HAWALA_PERF=1 to enable them.");
            return;
        }

        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var service = fixture.CreateService(context);
        var samples = new[] { 100, 1_000, 10_000 };

        foreach (var rowCount in samples)
        {
            var numberBase = 40_000_000L + rowCount * 20L;
            var items = BuildItems(rowCount, numberBase);
            fixture.Commands.Reset();
            var stopwatch = Stopwatch.StartNew();
            await service.CreateHawalasAsync(items);
            stopwatch.Stop();
            var commandCount = fixture.Commands.Count;

            context.ChangeTracker.Clear();
            var received = await InRange(context.Hawalas.AsNoTracking(), "HawalaReceive", numberBase, rowCount)
                .CountAsync();
            var receivedIds = await InRange(context.Hawalas.AsNoTracking(), "HawalaReceive", numberBase, rowCount)
                .Select(x => x.Id)
                .ToArrayAsync();
            var generated = await context.Hawalas.AsNoTracking()
                .CountAsync(x => x.IsSystemGenerated && x.SourceHawalaId.HasValue && receivedIds.Contains(x.SourceHawalaId.Value));
            var ledger = await context.LedgerEntries.AsNoTracking()
                .CountAsync(x => x.HawalaId.HasValue &&
                    (receivedIds.Contains(x.HawalaId.Value) ||
                     context.Hawalas.Any(h => h.Id == x.HawalaId.Value && h.SourceHawalaId.HasValue && receivedIds.Contains(h.SourceHawalaId.Value))));

            WriteResult(rowCount, stopwatch.Elapsed, commandCount, received, generated, ledger);
        }
    }

    private IReadOnlyList<CreateHawalaDto> BuildItems(
        int rowCount,
        long numberBase,
        bool forceRemote = false)
    {
        var items = new List<CreateHawalaDto>(rowCount);
        for (var index = 0; index < rowCount; index++)
        {
            var remote = forceRemote || index % 5 != 0;
            var hasCommission = remote && index % 4 == 1;
            var amount = 1_000m + index;
            items.Add(new CreateHawalaDto
            {
                Number = numberBase + index,
                HawalaType = "HawalaReceive",
                CorrespondentId = fixture.SourceCorrespondent.Id,
                PaymentLocationId = remote ? fixture.RemoteLocation.Id : fixture.OwnLocation.Id,
                FromAccountId = remote ? fixture.DestinationAccount.Id : null,
                SenderName = $"Sender {index}",
                ReceiverName = $"Receiver {index}",
                FromCurrencyId = 2,
                FromAmount = amount,
                ToCurrencyId = 2,
                ToAmount = amount,
                ExchangeRate = 1,
                CommissionAmount = null,
                AgentCommissionAmount = null,
                Status = remote ? "Paid" : "Pending",
                ReferenceNumber = $"PERF-{numberBase + index}",
                GeneratedSendHawalaNumber = numberBase + rowCount + index,
                GeneratedSendAgentCommissionAmount = hasCommission ? 25m : null,
                GeneratedSendAgentCommissionCurrencyId = hasCommission ? 2 : null,
                GeneratedSendReferenceNumber = $"PERF-SEND-{numberBase + index}"
            });
        }

        return items;
    }

    private static IQueryable<Hawala> InRange(
        IQueryable<Hawala> query,
        string type,
        long numberBase,
        int rowCount) => query.Where(x =>
            x.HawalaType == type && x.Number >= numberBase && x.Number < numberBase + rowCount);

    private void WriteResult(
        int rows,
        TimeSpan elapsed,
        long commands,
        int received,
        int generated,
        int ledger)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            database = fixture.DatabaseName,
            rows,
            elapsedMilliseconds = Math.Round(elapsed.TotalMilliseconds, 2),
            rowsPerSecond = Math.Round(rows / elapsed.TotalSeconds, 2),
            efCommands = commands,
            received,
            generated,
            ledger
        }));
    }
}
