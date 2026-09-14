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
public sealed class PeriodicCommissionPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Procedure_calculates_posts_prevents_duplicates_and_reverses()
    {
        var period = new DateTime(2032, 3, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        context.Hawalas.AddRange(
            NewHawala(94_000_001, 2, 1_000m, period, "Paid"),
            NewHawala(94_000_002, 1, 50_000m, period.AddDays(2), "Pending"),
            NewHawala(94_000_003, 2, 9_999m, period.AddDays(3), "Cancel"),
            NewHawala(94_000_004, 2, 9_999m, period.AddDays(4), "Paid", 10m));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var request = NewRequest(period, period.AddDays(6), 70m,
            new CorrespondentCommissionRateDto { CurrencyId = 2, SourceToAfnRate = 70m });
        var service = fixture.CreateCommissionService(context);

        var preview = await service.PreviewAsync(request);

        Assert.Equal(2, preview.HawalaCount);
        Assert.Equal(120_000m, preview.TotalBaseAfn);
        Assert.Equal(240m, preview.TotalCommissionAfn);
        Assert.Equal(3m, preview.TotalCommissionUsd);
        Assert.Equal(2, preview.Rates.Count);
        Assert.Equal(2, preview.Items.Count);
        Assert.Equal(140m, preview.Items.Single(x => x.HawalaNumber == 94_000_001).CommissionAfn);
        Assert.Equal(100m, preview.Items.Single(x => x.HawalaNumber == 94_000_002).CommissionAfn);

        var posted = await service.PostAsync(request);
        context.ChangeTracker.Clear();
        var batch = await context.CorrespondentCommissionBatches.AsNoTracking()
            .Include(x => x.Items).SingleAsync(x => x.Id == posted.Id);
        var ledger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == batch.PostingTransactionId).ToListAsync();

        Assert.Equal("Posted", batch.Status);
        Assert.Equal(2, batch.Items.Count);
        Assert.All(batch.Items, item => Assert.True(item.IsActive));
        Assert.Equal(2, ledger.Count);
        Assert.Equal(ledger.Sum(x => x.TalabKar), ledger.Sum(x => x.BadehKar));
        Assert.Equal(3m, ledger.Sum(x => x.TalabKar));
        await Assert.ThrowsAsync<SqlException>(() => service.PostAsync(request));

        await service.ReverseAsync(batch.Id, "آزمایش برگشت مرحله چهارم");
        context.ChangeTracker.Clear();
        var reversed = await context.CorrespondentCommissionBatches.AsNoTracking()
            .Include(x => x.Items).SingleAsync(x => x.Id == batch.Id);
        var original = await context.Transactions.AsNoTracking()
            .SingleAsync(x => x.Id == reversed.PostingTransactionId);
        var reversalLedger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == reversed.ReversalTransactionId).ToListAsync();

        Assert.Equal("Reversed", reversed.Status);
        Assert.All(reversed.Items, item => Assert.False(item.IsActive));
        Assert.Equal("Cancel", original.Status);
        Assert.Equal(ledger.Sum(x => x.BadehKar), reversalLedger.Sum(x => x.TalabKar));
        Assert.Equal(ledger.Sum(x => x.TalabKar), reversalLedger.Sum(x => x.BadehKar));
        Assert.Equal(2, (await service.PreviewAsync(request)).HawalaCount);
    }

    [Fact]
    public async Task Procedure_rounds_half_away_from_zero_and_rolls_back_when_rate_is_missing()
    {
        var roundingPeriod = new DateTime(2032, 4, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        context.Hawalas.Add(NewHawala(95_000_001, 1, 125_000m, roundingPeriod, "Paid"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateCommissionService(context);
        var roundingRequest = NewRequest(roundingPeriod, roundingPeriod, 100m);

        var roundingPreview = await service.PreviewAsync(roundingRequest);

        Assert.Equal(250m, roundingPreview.TotalCommissionAfn);
        Assert.Equal(3m, roundingPreview.TotalCommissionUsd);

        var missingRatePeriod = new DateTime(2032, 5, 10);
        context.Hawalas.Add(NewHawala(95_000_002, 3, 1_000m, missingRatePeriod, "Paid"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var missingRateRequest = NewRequest(missingRatePeriod, missingRatePeriod, 70m);
        var beforeTransactions = await context.Transactions.CountAsync();
        var beforeBatches = await context.CorrespondentCommissionBatches.CountAsync();
        var beforeLedger = await context.LedgerEntries.CountAsync();

        var missingRatePreview = await service.PreviewAsync(missingRateRequest);
        var exception = await Assert.ThrowsAsync<SqlException>(() => service.PostAsync(missingRateRequest));

        Assert.Equal(0m, missingRatePreview.Rates.Single().SourceToAfnRate);
        Assert.Contains("EUR", exception.Message);
        Assert.Equal(beforeTransactions, await context.Transactions.CountAsync());
        Assert.Equal(beforeBatches, await context.CorrespondentCommissionBatches.CountAsync());
        Assert.Equal(beforeLedger, await context.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Concurrent_posts_can_create_only_one_active_commission_batch()
    {
        var period = new DateTime(2032, 6, 10);
        await using (var seedContext = fixture.CreateContext())
        {
            using var bypass = seedContext.BypassSubscriptionEnforcement();
            seedContext.Hawalas.Add(NewHawala(95_000_003, 2, 1_000m, period, "Paid"));
            await seedContext.SaveChangesAsync();
        }

        var request = NewRequest(period, period, 70m,
            new CorrespondentCommissionRateDto { CurrencyId = 2, SourceToAfnRate = 70m });
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        using var firstBypass = firstContext.BypassSubscriptionEnforcement();
        using var secondBypass = secondContext.BypassSubscriptionEnforcement();

        async Task<object> TryPostAsync(HawalaExchange.Infrastructure.Services.CorrespondentCommissionService service)
        {
            try { return await service.PostAsync(request); }
            catch (Exception exception) { return exception; }
        }

        var results = await Task.WhenAll(
            TryPostAsync(fixture.CreateCommissionService(firstContext)),
            TryPostAsync(fixture.CreateCommissionService(secondContext)));

        Assert.Single(results.OfType<CorrespondentCommissionBatchDto>());
        Assert.Single(results.OfType<SqlException>());
        await using var verifyContext = fixture.CreateContext();
        Assert.Equal(1, await verifyContext.CorrespondentCommissionBatchItems
            .CountAsync(x => x.Hawala.Number == 95_000_003 && x.IsActive));
    }

    [Fact]
    public async Task Measure_periodic_commission_procedure_for_10000_hawalas()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Periodic commission performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int rowCount = 10_000;
        var period = new DateTime(2033, 1, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var rows = new List<Hawala>(rowCount);
        for (var index = 0; index < rowCount; index++)
            rows.Add(NewHawala(96_000_000 + index, 2, 1_000m + index, period, "Paid"));
        context.Hawalas.AddRange(rows);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateCommissionService(context);
        var request = NewRequest(period, period,
            70m, new CorrespondentCommissionRateDto { CurrencyId = 2, SourceToAfnRate = 70m });

        var previewWatch = Stopwatch.StartNew();
        var preview = await service.PreviewAsync(request);
        previewWatch.Stop();
        var postWatch = Stopwatch.StartNew();
        var batch = await service.PostAsync(request);
        postWatch.Stop();

        Assert.Equal(rowCount, preview.HawalaCount);
        Assert.Equal(rowCount, batch.HawalaCount);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            inputRows = rowCount,
            previewMilliseconds = Math.Round(previewWatch.Elapsed.TotalMilliseconds, 2),
            postMilliseconds = Math.Round(postWatch.Elapsed.TotalMilliseconds, 2)
        }));
    }

    private CorrespondentCommissionPreviewRequestDto NewRequest(
        DateTime from,
        DateTime to,
        decimal usdToAfnRate,
        params CorrespondentCommissionRateDto[] rates) => new()
    {
        CorrespondentId = fixture.SourceCorrespondent.Id,
        PeriodFrom = from,
        PeriodTo = to,
        CommissionPerLakhAfn = 200m,
        UsdToAfnRate = usdToAfnRate,
        Rates = [.. rates]
    };

    private Hawala NewHawala(
        long number,
        long currencyId,
        decimal amount,
        DateTime date,
        string status,
        decimal? perTransactionCommission = null) => new()
    {
        Number = number,
        HawalaType = "HawalaReceive",
        CorrespondentId = fixture.SourceCorrespondent.Id,
        PaymentLocationId = fixture.OwnLocation.Id,
        SenderName = "Periodic sender",
        ReceiverName = "Periodic receiver",
        FromCurrencyId = currencyId,
        FromAmount = amount,
        ToCurrencyId = currencyId,
        ToAmount = amount,
        ExchangeRate = 1,
        CommissionAmount = perTransactionCommission,
        CommissionCurrencyId = perTransactionCommission.HasValue ? currencyId : null,
        Status = status,
        CreatedBy = fixture.UserId,
        CreatedAt = date.AddHours(10).ToUniversalTime()
    };
}
