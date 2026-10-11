using ClosedXML.Excel;
using System.Diagnostics;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class BulkImportReferencePreparationTests(SqlServerPerformanceFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(40, false)]
    [InlineData(100, true)]
    [InlineData(500, true)]
    [InlineData(3700, true)]
    public async Task Many_new_locations_and_agents_are_prepared_with_bounded_queries_and_no_financial_entries(int count, bool newAgents)
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var request = await Preview(context, count, Guid.NewGuid().ToString("N"), newAgents);
        var beforeHawalas = await context.Hawalas.CountAsync();
        var beforeLedger = await context.LedgerEntries.CountAsync();
        var beforeLocations = await context.PaymentLocations.CountAsync();
        var beforeAgents = await context.Correspondents.CountAsync();
        var beforeAccounts = await context.Accounts.CountAsync();
        fixture.Commands.Reset();
        var watch = Stopwatch.StartNew();
        var prepared = await Service(context).PrepareLocationsAsync(request);
        watch.Stop();
        var commands = fixture.Commands.Count;
        output.WriteLine($"NewLocations={count}; NewAgents={(newAgents ? count : 0)}; PreparationSeconds={watch.Elapsed.TotalSeconds:F3}; EFCommands={commands}; bulk SQL calls <= 4");
        Assert.True(prepared.LocationsPrepared);
        Assert.Empty(prepared.MissingLocations);
        Assert.InRange(commands, 1, 40);
        Assert.Equal(beforeLocations + count, await context.PaymentLocations.CountAsync());
        Assert.Equal(beforeAgents + (newAgents ? count : 0), await context.Correspondents.CountAsync());
        Assert.Equal(beforeAccounts + (newAgents ? count : 0), await context.Accounts.CountAsync());
        Assert.Equal(beforeHawalas, await context.Hawalas.CountAsync());
        Assert.Equal(beforeLedger, await context.LedgerEntries.CountAsync());
        var remote = prepared.Rows.Where(x => x.RequiresOutgoingHawala).ToList();
        Assert.Equal(count, remote.Count);
        Assert.All(remote, x => { Assert.NotNull(x.PaymentLocationId); Assert.NotNull(x.DestinationCorrespondentId); });
        var locationIds = remote.Select(x => x.PaymentLocationId!.Value).ToArray();
        Assert.Equal(count, await context.PaymentLocationCorrespondentAssignments.CountAsync(x => locationIds.Contains(x.PaymentLocationId)));
        Assert.Equal(count, await context.AuditLogs.CountAsync(x => x.TableName == "PaymentLocations" && locationIds.Contains(x.RecordId) && x.Action == "CREATE"));
        if (newAgents)
        {
            var ids = remote.Select(x => x.DestinationCorrespondentId!.Value).ToArray();
            var agents = await context.Correspondents.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();
            Assert.Equal(count, agents.Select(x => x.Code).Distinct().Count());
            var accounts = await context.Accounts.AsNoTracking().Where(x => x.CorrespondentId.HasValue && ids.Contains(x.CorrespondentId.Value)).ToListAsync();
            Assert.Equal(count, accounts.Count);
            Assert.Equal(count, accounts.Select(x => x.AccountCode).Distinct().Count());
            Assert.All(accounts, x => Assert.Equal("Correspondent", x.AccountType));
            Assert.Equal(count, await context.AuditLogs.CountAsync(x => x.TableName == "Correspondents" && ids.Contains(x.RecordId) && x.Action == "CREATE"));
        }
        var repeated = await Service(context).PrepareLocationsAsync(request);
        Assert.True(repeated.LocationsPrepared);
        Assert.Equal(beforeLocations + count, await context.PaymentLocations.CountAsync());
    }

    [Fact]
    public async Task Bulk_preparation_without_subscription_cannot_bypass_write_access_checks()
    {
        await using var context = fixture.CreateContext();
        ConfirmHawalaImportDto request;
        using (context.BypassSubscriptionEnforcement())
            request = await Preview(context, 10, Guid.NewGuid().ToString("N"), true);
        await context.TenantSubscriptions.ExecuteDeleteAsync();
        var locations = await context.PaymentLocations.CountAsync();
        var agents = await context.Correspondents.CountAsync();
        var accounts = await context.Accounts.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).PrepareLocationsAsync(request));
        Assert.Equal(locations, await context.PaymentLocations.CountAsync());
        Assert.Equal(agents, await context.Correspondents.CountAsync());
        Assert.Equal(accounts, await context.Accounts.CountAsync());
        Assert.Null((await context.HawalaImportBatches.AsNoTracking().SingleAsync(x => x.Id == request.BatchId)).LocationsPreparedAt);
    }

    [Fact]
    public async Task Invalid_owner_rolls_back_created_locations_and_their_audit_logs()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var request = await Preview(context, 40, Guid.NewGuid().ToString("N"), false);
        request.LocationMappings[^1].CorrespondentId = long.MaxValue;
        var locations = await context.PaymentLocations.CountAsync();
        var logs = await context.AuditLogs.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).PrepareLocationsAsync(request));
        Assert.Equal(locations, await context.PaymentLocations.CountAsync());
        Assert.Equal(logs, await context.AuditLogs.CountAsync());
        Assert.Null((await context.HawalaImportBatches.AsNoTracking().SingleAsync(x => x.Id == request.BatchId)).LocationsPreparedAt);
    }

    [Fact]
    public async Task Two_users_preparing_same_new_places_do_not_duplicate_locations_or_assignments()
    {
        var key = Guid.NewGuid().ToString("N");
        ConfirmHawalaImportDto first, second;
        await using (var seed = fixture.CreateContext())
        {
            using var bypass = seed.BypassSubscriptionEnforcement();
            first = await Preview(seed, 10, key, false);
            second = await Preview(seed, 10, key, false);
        }
        async Task Prepare(ConfirmHawalaImportDto request)
        {
            await using var context = fixture.CreateContext();
            using var bypass = context.BypassSubscriptionEnforcement();
            await Service(context).PrepareLocationsAsync(request);
        }
        await Task.WhenAll(Prepare(first), Prepare(second));
        await using var verify = fixture.CreateContext();
        var prefix = $"BulkPlace-{key}-";
        var ids = await verify.PaymentLocations.Where(x => x.Name.StartsWith(prefix)).Select(x => x.Id).ToListAsync();
        Assert.Equal(10, ids.Count);
        Assert.Equal(10, await verify.PaymentLocationCorrespondentAssignments.CountAsync(x => ids.Contains(x.PaymentLocationId)));
    }

    private HawalaImportService Service(ApplicationDbContext context) => new(context, fixture.CreateService(context),
        Mock.Of<IPaymentLocationService>(), Mock.Of<ICorrespondentService>(), Mock.Of<IAuditLogService>());

    private async Task<ConfirmHawalaImportDto> Preview(ApplicationDbContext context, int count, string key, bool newAgents)
    {
        await context.CompanySettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.OwnPaymentLocationId, fixture.OwnLocation.Id));
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Hawalas");
        var number = Random.Shared.NextInt64(200_000_000, 900_000_000);
        var refKey = Guid.NewGuid().ToString("N");
        for (var row = 0; row <= count; row++)
        {
            object[] values = [number + row, $"BULK-{refKey}-{row}", "Sender", "Receiver",
                row == 0 ? fixture.OwnLocation.Name : $"BulkPlace-{key}-{row}", 100, "USD"];
            for (var column = 0; column < values.Length; column++) sheet.Cell(row + 1, column + 1).Value = XLCellValue.FromObject(values[column]);
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream); stream.Position = 0;
        var preview = await Service(context).PreviewAsync(stream, "many-new-places.xlsx", fixture.SourceCorrespondent.Id);
        Assert.Equal(0, preview.InvalidRowCount);
        return new ConfirmHawalaImportDto
        {
            BatchId = preview.BatchId, OwnPaymentLocationName = fixture.OwnLocation.Name,
            LocationsToCreate = preview.MissingLocations,
            LocationMappings = preview.PaymentLocations.Where(x => x.Name.StartsWith($"BulkPlace-{key}-"))
                .Select(x => new HawalaImportLocationMappingDto
                {
                    PaymentLocationName = x.Name, CorrespondentId = newAgents ? null : fixture.DestinationCorrespondent.Id,
                    CreateCorrespondent = newAgents, NewCorrespondentName = newAgents ? x.Name.Replace("BulkPlace-", "BulkAgent-") : null
                }).ToList()
        };
    }
}
