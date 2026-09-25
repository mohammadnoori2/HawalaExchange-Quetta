using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
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
    public async Task Source_daily_rate_is_saved_without_changing_journal_rate()
    {
        var day = DateTime.Today.AddYears(-7).AddDays(-41);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var service = new CorrespondentDailyRateService(context);

        var saved = await service.SaveAsync(fixture.SourceCorrespondent.Id, day, 66.25m);
        Assert.Equal(66.25m, saved.UsdToAfnRate);
        Assert.Null(await context.DailyCommissionRates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RateDate == day));
        Assert.Equal(66.25m, (await service.GetAsync(fixture.SourceCorrespondent.Id, day)).UsdToAfnRate);
    }

    [Fact]
    public async Task Outgoing_afn_requires_source_rate_even_when_global_rate_exists()
    {
        var day = new DateTime(2031, 8, 14);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var source = NewHawala(98_710_001, 1, 100_000m, day, "Paid");
        context.Hawalas.Add(source);
        context.DailyCommissionRates.Add(NewDailyRate(day, 70m));
        await context.SaveChangesAsync();
        context.Hawalas.Add(NewOutgoingHawala(98_710_002, 1, 100_000m, day, source.Id));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var request = NewRequest(day, day, 70m);
        request.CorrespondentId = fixture.DestinationCorrespondent.Id;
        request.HawalaType = "HawalaSend";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateCommissionService(context).PreviewAsync(request));
        Assert.Contains("نمایندگی مبدأ", error.Message);
        Assert.Contains("2031-08-14", error.Message);
    }

    [Fact]
    public async Task Saving_daily_rate_persists_usd_equivalent_for_afn_and_usd_hawalas()
    {
        var rateDate = DateTime.Today.AddYears(-8).AddDays(-17);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        context.Hawalas.AddRange(
            NewHawala(93_900_001, 1, 70_000m, rateDate, "Paid"),
            NewHawala(93_900_002, 2, 500m, rateDate, "Paid"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await fixture.CreateJournalService(context)
            .SaveDailyCommissionRateAsync(rateDate, 70m);
        context.ChangeTracker.Clear();
        var valued = await context.Hawalas.AsNoTracking()
            .Where(x => x.Number == 93_900_001 || x.Number == 93_900_002)
            .OrderBy(x => x.Number)
            .ToListAsync();

        Assert.Equal(1, result.AfnHawalaCount);
        Assert.Equal(1, result.UsdHawalaCount);
        Assert.Equal(2, result.ValuedHawalaCount);
        Assert.Equal(1_000m, valued[0].CommissionBaseUsdAmount);
        Assert.Equal(70m, valued[0].CommissionUsdToAfnRate);
        Assert.Equal(500m, valued[1].CommissionBaseUsdAmount);
        Assert.Null(valued[1].CommissionUsdToAfnRate);
        Assert.All(valued, x => Assert.Equal(rateDate.Date, x.CommissionValuationDate));
    }

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
        context.DailyCommissionRates.Add(NewDailyRate(period.AddDays(2), 70m));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var request = NewRequest(period, period.AddDays(6), 70m,
            new CorrespondentCommissionRateDto { CurrencyId = 2, SourceToAfnRate = 70m });
        var service = fixture.CreateCommissionService(context);

        var preview = await service.PreviewAsync(request);

        Assert.Equal(2, preview.HawalaCount);
        Assert.Equal(1_714.2857m, preview.TotalBaseAfn);
        Assert.Equal(0m, preview.TotalCommissionAfn);
        Assert.Equal(3m, preview.TotalCommissionUsd);
        Assert.Equal(2, preview.Rates.Count);
        Assert.Equal(2, preview.Items.Count);
        Assert.Equal(2m, decimal.Round(preview.Items.Single(x => x.HawalaNumber == 94_000_001).CommissionAfn, 0, MidpointRounding.AwayFromZero));
        Assert.Equal(1m, decimal.Round(preview.Items.Single(x => x.HawalaNumber == 94_000_002).CommissionAfn, 0, MidpointRounding.AwayFromZero));
        var valuedAfnHawala = await context.Hawalas.AsNoTracking()
            .SingleAsync(x => x.Number == 94_000_002);
        Assert.Equal(714.28571429m, valuedAfnHawala.CommissionBaseUsdAmount);
        Assert.Equal(70m, valuedAfnHawala.CommissionUsdToAfnRate);

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
        var reversedDetails = await service.GetDetailsAsync(batch.Id);
        Assert.Equal("Reversed", reversedDetails.Status);
        Assert.Equal("آزمایش برگشت مرحله چهارم", reversedDetails.ReversalReason);
        Assert.False(string.IsNullOrWhiteSpace(reversedDetails.ReversalTransactionNo));
        Assert.Equal(4, reversedDetails.LedgerEntries.Count);
        Assert.All(reversedDetails.Items, item => Assert.False(item.IsActive));
    }

    [Fact]
    public async Task Procedure_rounds_half_away_from_zero_and_rolls_back_when_rate_is_missing()
    {
        var roundingPeriod = new DateTime(2032, 4, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        context.Hawalas.Add(NewHawala(95_000_001, 2, 1_250m, roundingPeriod, "Paid"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateCommissionService(context);
        var roundingRequest = NewRequest(roundingPeriod, roundingPeriod, 100m);

        var roundingPreview = await service.PreviewAsync(roundingRequest);

        Assert.Equal(0m, roundingPreview.TotalCommissionAfn);
        Assert.Equal(3m, roundingPreview.TotalCommissionUsd);

        var missingRatePeriod = new DateTime(2032, 5, 10);
        context.Hawalas.Add(NewHawala(95_000_002, 1, 1_000m, missingRatePeriod, "Paid"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var missingRateRequest = NewRequest(missingRatePeriod, missingRatePeriod, 70m);
        var beforeTransactions = await context.Transactions.CountAsync();
        var beforeBatches = await context.CorrespondentCommissionBatches.CountAsync();
        var beforeLedger = await context.LedgerEntries.CountAsync();

        var previewException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PreviewAsync(missingRateRequest));
        var postException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PostAsync(missingRateRequest));

        Assert.Contains(missingRatePeriod.ToString("yyyy-MM-dd"), previewException.Message);
        Assert.Contains(missingRatePeriod.ToString("yyyy-MM-dd"), postException.Message);
        Assert.Equal(beforeTransactions, await context.Transactions.CountAsync());
        Assert.Equal(beforeBatches, await context.CorrespondentCommissionBatches.CountAsync());
        Assert.Equal(beforeLedger, await context.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Outgoing_commission_credits_destination_in_original_currency_and_debits_source_in_usd()
    {
        var period = new DateTime(2032, 7, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var destination = await context.Correspondents
            .SingleAsync(x => x.Id == fixture.DestinationCorrespondent.Id);
        var originalMethod = destination.CommissionMethod;
        destination.CommissionMethod = "PeriodicPerLakh";

        var usdSource = NewHawala(98_700_001, 2, 100_000m, period, "Paid");
        var firstAfnSource = NewHawala(98_700_002, 1, 300_000m, period, "Paid");
        var secondAfnSource = NewHawala(98_700_003, 1, 200_000m, period.AddDays(1), "Paid");
        var commissionedSource = NewHawala(98_700_007, 2, 500_000m, period, "Paid");
        context.Hawalas.AddRange(usdSource, firstAfnSource, secondAfnSource, commissionedSource);
        context.DailyCommissionRates.AddRange(
            NewDailyRate(period, 70m),
            NewDailyRate(period.AddDays(1), 70m));
        context.CorrespondentDailyCommissionRates.AddRange(
            NewSourceDailyRate(fixture.SourceCorrespondent.Id, period, 66m),
            NewSourceDailyRate(fixture.SourceCorrespondent.Id, period.AddDays(1), 67m));
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(
            NewOutgoingHawala(98_700_004, 2, 100_000m, period, usdSource.Id),
            NewOutgoingHawala(98_700_005, 1, 300_000m, period, firstAfnSource.Id),
            NewOutgoingHawala(98_700_006, 1, 200_000m, period.AddDays(1), secondAfnSource.Id),
            NewOutgoingHawala(98_700_008, 2, 500_000m, period, commissionedSource.Id, 0m),
            NewOutgoingHawala(98_700_009, 2, 50_000m, period, null));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var request = NewRequest(period, period.AddDays(1), 66m);
        request.CorrespondentId = fixture.DestinationCorrespondent.Id;
        request.HawalaType = "HawalaSend";
        var service = fixture.CreateCommissionService(context);
        var preview = await service.PreviewAsync(request);

        Assert.Equal(4, preview.HawalaCount);
        Assert.Equal(1_000m, preview.TotalCommissionAfn);
        Assert.Equal(300m, preview.TotalCommissionUsd);
        Assert.Equal(315m, preview.TotalBaseAfn);
        Assert.Equal(9m, preview.Items.Single(x => x.HawalaNumber == 98_700_005).AfnEquivalent);
        Assert.Equal(6m, preview.Items.Single(x => x.HawalaNumber == 98_700_006).AfnEquivalent);
        Assert.Equal(66m, preview.Items.Single(x => x.HawalaNumber == 98_700_005).SourceToAfnRate);
        Assert.Equal(67m, preview.Items.Single(x => x.HawalaNumber == 98_700_006).SourceToAfnRate);

        var posted = await service.PostAsync(request);
        context.ChangeTracker.Clear();
        var batch = await context.CorrespondentCommissionBatches.AsNoTracking()
            .SingleAsync(x => x.Id == posted.Id);
        var ledger = await context.LedgerEntries.AsNoTracking()
            .Include(x => x.Account)
            .Where(x => x.TransactionId == batch.PostingTransactionId)
            .ToListAsync();

        Assert.Equal(300m, ledger.Single(x => x.AccountId == fixture.DestinationAccount.Id && x.CurrencyId == 2).TalabKar);
        Assert.Equal(1_000m, ledger.Single(x => x.AccountId == fixture.DestinationAccount.Id && x.CurrencyId == 1).TalabKar);
        Assert.Equal(215m, ledger.Single(x => x.AccountId == fixture.SourceAccount.Id && x.CurrencyId == 2).BadehKar);
        Assert.Equal(100m, ledger.Single(x => x.Account!.AccountCode == "5002" && x.CurrencyId == 2).BadehKar);
        Assert.Equal(ledger.Where(x => x.CurrencyId == 2).Sum(x => x.TalabKar),
            ledger.Where(x => x.CurrencyId == 2).Sum(x => x.BadehKar));
        Assert.Equal(ledger.Where(x => x.CurrencyId == 1).Sum(x => x.TalabKar),
            ledger.Where(x => x.CurrencyId == 1).Sum(x => x.BadehKar));
        var details = await service.GetDetailsAsync(batch.Id);
        Assert.Equal(4, details.Items.Count);
        Assert.Equal(6, details.LedgerEntries.Count);
        Assert.Equal(1_000m, details.TotalCommissionAfn);
        Assert.Equal(300m, details.TotalCommissionUsd);
        Assert.Equal("صرافی خود ما", details.Items.Single(x => x.HawalaNumber == 98_700_009).SourceName);
        Assert.False(string.IsNullOrWhiteSpace(details.PostingTransactionNo));
        Assert.False(string.IsNullOrWhiteSpace(details.CreatedByName));

        destination = await context.Correspondents.SingleAsync(x => x.Id == fixture.DestinationCorrespondent.Id);
        destination.CommissionMethod = originalMethod;
        await context.SaveChangesAsync();
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
        Assert.Single(results.OfType<Exception>());
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

    private Hawala NewOutgoingHawala(
        long number,
        long currencyId,
        decimal amount,
        DateTime date,
        long? sourceHawalaId,
        decimal? agentCommissionAmount = null) => new()
    {
        Number = number,
        HawalaType = "HawalaSend",
        CorrespondentId = fixture.DestinationCorrespondent.Id,
        PaymentLocationId = fixture.RemoteLocation.Id,
        SenderName = "Outgoing sender",
        ReceiverName = "Outgoing receiver",
        FromCurrencyId = currencyId,
        FromAmount = amount,
        ToCurrencyId = currencyId,
        ToAmount = amount,
        ExchangeRate = 1,
        AgentCommissionAmount = agentCommissionAmount,
        AgentCommissionCurrencyId = agentCommissionAmount.HasValue ? currencyId : null,
        SourceHawalaId = sourceHawalaId,
        IsSystemGenerated = true,
        Status = "Paid",
        CreatedBy = fixture.UserId,
        CreatedAt = date.AddHours(10).ToUniversalTime()
    };

    private DailyCommissionRate NewDailyRate(DateTime date, decimal usdToAfnRate) => new()
    {
        RateDate = date.Date,
        UsdToAfnRate = usdToAfnRate,
        CreatedBy = fixture.UserId,
        CreatedAt = DateTime.UtcNow
    };

    private CorrespondentDailyCommissionRate NewSourceDailyRate(
        long correspondentId, DateTime date, decimal usdToAfnRate) => new()
    {
        CorrespondentId = correspondentId,
        RateDate = date.Date,
        UsdToAfnRate = usdToAfnRate,
        CreatedBy = fixture.UserId,
        CreatedAt = DateTime.UtcNow
    };
}
