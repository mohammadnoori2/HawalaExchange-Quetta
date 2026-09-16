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
public sealed class HawalaQueryPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task List_query_honors_cancellation_before_database_work()
    {
        await using var context = fixture.CreateContext();
        var service = fixture.CreateService(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Commands.Reset();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetHawalasAsync(new HawalaFilterDto
            {
                PageNumber = 1,
                PageSize = 10
            }, cancellation.Token));

        Assert.InRange(fixture.Commands.Count, 0, 1);
    }

    [Fact]
    public async Task List_query_preserves_filters_paging_and_display_fields_without_tracking()
    {
        const long numberBase = 81_000_000;
        var createdAt = DateTime.UtcNow.AddHours(-1);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();

        var rows = Enumerable.Range(0, 12).Select(index => new Hawala
        {
            Number = numberBase + index,
            HawalaType = index % 2 == 0 ? "HawalaReceive" : "HawalaSend",
            CorrespondentId = fixture.SourceCorrespondent.Id,
            PaymentLocationId = fixture.RemoteLocation.Id,
            SenderName = $"List sender {index}",
            ReceiverName = $"List receiver {index}",
            FromCurrencyId = 2,
            FromAmount = 10_000m + index,
            ToCurrencyId = 2,
            ToAmount = 10_000m + index,
            ExchangeRate = 1,
            ReferenceNumber = $"LIST-REF-{numberBase + index}",
            Status = index % 3 == 0 ? "Paid" : "Pending",
            PaidFromAccountId = index == 0 ? fixture.DestinationAccount.Id : null,
            CreatedAt = createdAt.AddMinutes(index),
            CreatedBy = fixture.UserId
        }).ToArray();
        context.Hawalas.AddRange(rows);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var service = fixture.CreateService(context);
        fixture.Commands.Reset();
        var result = await service.GetHawalasAsync(new HawalaFilterDto
        {
            SearchTerm = $"LIST-REF-{numberBase}",
            HawalaType = "HawalaReceive",
            Status = "Paid",
            CorrespondentId = fixture.SourceCorrespondent.Id,
            PaymentLocationId = fixture.RemoteLocation.Id,
            CurrencyId = 2,
            FromDate = createdAt.Date,
            ToDate = createdAt.Date,
            PageNumber = 1,
            PageSize = 5,
            SortColumn = "CreatedAt",
            SortDirection = "desc"
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(numberBase, item.Number);
        Assert.Equal($"LIST-REF-{numberBase}", item.ReferenceNumber);
        Assert.Equal(fixture.SourceCorrespondent.Name, item.CorrespondentName);
        Assert.Equal(fixture.RemoteLocation.Name, item.PaymentLocationName);
        Assert.Equal(fixture.DestinationAccount.AccountName, item.PaidFromAccountName);
        Assert.Equal(fixture.DestinationAccount.AccountType, item.PaidFromAccountType);
        Assert.Equal("USD", item.FromCurrencyCode);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.InRange(fixture.Commands.Count, 1, 2);

        var empty = await service.GetHawalasAsync(new HawalaFilterDto
        {
            SearchTerm = "DOES-NOT-EXIST",
            PageSize = 0
        });
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalPages);
    }

    [Fact]
    public async Task List_query_is_tenant_scoped()
    {
        const long sharedNumber = 82_000_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var secondTenant = new Tenant { Name = "Query tenant 2", IsActive = true };
        context.Tenants.Add(secondTenant);
        await context.SaveChangesAsync();

        context.Hawalas.Add(CreateTenantRow(sharedNumber, fixture.UserId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var tenantOneResult = await fixture.CreateService(context).GetHawalasAsync(new HawalaFilterDto
        {
            Number = sharedNumber,
            PageNumber = 1,
            PageSize = 10
        });
        Assert.Single(tenantOneResult.Items);
        Assert.Equal(1, tenantOneResult.TotalCount);

        using (context.UseTenantScope(secondTenant.Id))
        {
            var tenantTwoResult = await fixture.CreateService(context).GetHawalasAsync(new HawalaFilterDto
            {
                Number = sharedNumber,
                PageNumber = 1,
                PageSize = 10
            });
            Assert.Empty(tenantTwoResult.Items);
            Assert.Equal(0, tenantTwoResult.TotalCount);
        }
    }

    [Fact]
    public async Task Migration_installs_indexes_for_the_supported_hawala_filters()
    {
        await using var context = fixture.CreateContext();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [name] FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Hawalas]') AND [name] IS NOT NULL",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        Assert.Contains("IX_Hawalas_TenantId_Number", names);
        Assert.Contains("IX_Hawalas_TenantId_HawalaType_Status_CreatedAt", names);
        Assert.Contains("IX_Hawalas_TenantId_CorrespondentId_HawalaType_CreatedAt", names);
        Assert.Contains("IX_Hawalas_TenantId_PaymentLocationId_HawalaType_CreatedAt", names);
        Assert.Contains("IX_Hawalas_TenantId_FromCurrencyId_CreatedAt", names);
    }

    [Fact]
    public async Task Measure_projected_list_against_legacy_include_query()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Hawala list performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int rowCount = 10_000;
        const int pageSize = 100;
        const long numberBase = 83_000_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var service = fixture.CreateService(context);
        await service.CreateHawalasAsync(BuildBulkItems(rowCount, numberBase));
        context.ChangeTracker.Clear();

        var legacyDurations = new List<double>();
        var projectedDurations = new List<double>();
        for (var run = 0; run < 3; run++)
        {
            context.ChangeTracker.Clear();
            var legacyStopwatch = Stopwatch.StartNew();
            var legacyQuery = context.Hawalas
                .AsNoTracking()
                .Include(h => h.Correspondent).ThenInclude(c => c!.SettlementCurrency)
                .Include(h => h.SettlementConversionLinks).ThenInclude(x => x.Conversion)
                .Include(h => h.SettlementConversionItems).ThenInclude(x => x.SourceCurrency)
                .Include(h => h.SettlementConversionItems).ThenInclude(x => x.Conversion).ThenInclude(x => x.TargetCurrency)
                .Include(h => h.FromCurrency)
                .Include(h => h.ToCurrency)
                .Include(h => h.CommissionCurrency)
                .Include(h => h.AgentCommissionCurrency)
                .Include(h => h.PaymentLocation)
                .Include(h => h.PaidFromAccount)
                .Where(h => h.CorrespondentId == fixture.SourceCorrespondent.Id &&
                            h.HawalaType == "HawalaReceive")
                .OrderByDescending(h => h.CreatedAt);
            var legacyCount = await legacyQuery.CountAsync();
            var legacyItems = await legacyQuery.Take(pageSize).ToListAsync();
            legacyStopwatch.Stop();
            legacyDurations.Add(legacyStopwatch.Elapsed.TotalMilliseconds);

            context.ChangeTracker.Clear();
            var projectedStopwatch = Stopwatch.StartNew();
            var projected = await service.GetHawalasAsync(new HawalaFilterDto
            {
                CorrespondentId = fixture.SourceCorrespondent.Id,
                HawalaType = "HawalaReceive",
                PageNumber = 1,
                PageSize = pageSize,
                SortColumn = "CreatedAt",
                SortDirection = "desc"
            });
            projectedStopwatch.Stop();
            projectedDurations.Add(projectedStopwatch.Elapsed.TotalMilliseconds);

            Assert.Equal(legacyCount, projected.TotalCount);
            Assert.Equal(legacyItems.Select(x => x.Id), projected.Items.Select(x => x.Id));
        }

        legacyDurations.Sort();
        projectedDurations.Sort();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            rowsInTenant = rowCount,
            pageSize,
            legacyMedianMilliseconds = Math.Round(legacyDurations[1], 2),
            projectedMedianMilliseconds = Math.Round(projectedDurations[1], 2),
            improvementPercent = Math.Round((1 - projectedDurations[1] / legacyDurations[1]) * 100, 2)
        }));
    }

    private static Hawala CreateTenantRow(long number, long userId) => new()
    {
        Number = number,
        HawalaType = "HawalaReceive",
        SenderName = "Tenant sender",
        ReceiverName = "Tenant receiver",
        FromCurrencyId = 2,
        FromAmount = 1_000,
        ToCurrencyId = 2,
        ToAmount = 1_000,
        ExchangeRate = 1,
        ReferenceNumber = $"TENANT-{number}",
        Status = "Pending",
        CreatedAt = DateTime.UtcNow,
        CreatedBy = userId
    };

    private IReadOnlyList<CreateHawalaDto> BuildBulkItems(int rowCount, long numberBase) =>
        Enumerable.Range(0, rowCount).Select(index => new CreateHawalaDto
        {
            Number = numberBase + index,
            HawalaType = "HawalaReceive",
            CorrespondentId = fixture.SourceCorrespondent.Id,
            PaymentLocationId = fixture.OwnLocation.Id,
            SenderName = $"Query sender {index}",
            ReceiverName = $"Query receiver {index}",
            FromCurrencyId = 2,
            FromAmount = 1_000m + index,
            ToCurrencyId = 2,
            ToAmount = 1_000m + index,
            ExchangeRate = 1,
            Status = "Pending",
            ReferenceNumber = $"QUERY-{numberBase + index}"
        }).ToArray();
}
