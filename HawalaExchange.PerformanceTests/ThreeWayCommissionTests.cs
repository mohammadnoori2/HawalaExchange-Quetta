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
public sealed class ThreeWayCommissionTests(SqlServerPerformanceFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Three_commissions_are_independent_and_only_sent_amounts_debit_sender(bool forwardingFirst)
    {
        var day = new DateTime(2038, 1, forwardingFirst ? 12 : 10);
        var offset = forwardingFirst ? 20 : 0;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var afn = Receive(199_000_001 + offset, 1, 1_000_000m, day);
        afn.ReferenceNumber = "INCOMING-DAILY-REF";
        var usd = Receive(199_000_002 + offset, 2, 10_000m, day);
        context.Hawalas.AddRange(afn, usd);
        context.CorrespondentDailyCommissionRates.Add(Rate(day, 70m));
        await context.SaveChangesAsync();
        var afnSent = Send(199_000_003 + offset, afn, 500_000m, day);
        var usdSent = Send(199_000_004 + offset, usd, 6_000m, day);
        context.Hawalas.AddRange(afnSent, usdSent);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateCommissionService(context);
        var incoming = Request("Incoming", day);
        var forwarding = Request("Forwarding", day);
        var destination = Request("Destination", day);
        var incomingPreview = await service.PreviewAsync(incoming);
        Assert.Equal(97m, incomingPreview.TotalCommissionUsd);
        Assert.Equal(1_000_000m, incomingPreview.TotalSourceAfn);
        Assert.Equal(10_000m, incomingPreview.TotalSourceUsd);
        var incomingDisplay = incomingPreview.Items.Single(x => x.HawalaId == afn.Id);
        Assert.Equal(afn.SenderName, incomingDisplay.SenderName);
        Assert.Equal(afn.ReceiverName, incomingDisplay.ReceiverName);
        Assert.Equal(afn.ReferenceNumber, incomingDisplay.ReferenceNumber);
        Assert.Equal(fixture.OwnLocation.Name, incomingDisplay.PaymentLocationName);
        var forwarded = await service.PreviewAsync(forwarding);
        Assert.Equal(53m, forwarded.TotalCommissionUsd);
        Assert.Equal(500_000m, forwarded.TotalSourceAfn);
        Assert.Equal(6_000m, forwarded.TotalSourceUsd);
        Assert.All(forwarded.Items, item => Assert.Equal(day, item.ValuationDate));
        Assert.Equal(1_000m, (await service.PreviewAsync(destination)).TotalCommissionAfn);

        var first = await service.PostAsync(forwardingFirst ? forwarding : incoming);
        var second = await service.PostAsync(forwardingFirst ? incoming : forwarding);
        var payable = await service.PostAsync(destination);
        var destinationEntries = (await service.GetDetailsAsync(payable.Id)).LedgerEntries;
        Assert.DoesNotContain(destinationEntries, entry => entry.AccountId == fixture.SourceAccount.Id);
        Assert.Contains(destinationEntries, entry => entry.AccountId == fixture.DestinationAccount.Id && entry.CurrencyCode == "AFN" && entry.TalabKar == 1_000m);
        Assert.Contains(destinationEntries, entry => entry.AccountCode == "5002" && entry.CurrencyCode == "AFN" && entry.BadehKar == 1_000m);
        Assert.Contains(destinationEntries, entry => entry.AccountCode == "5002" && entry.CurrencyCode == "USD" && entry.BadehKar == 6m);
        Assert.DoesNotContain(destinationEntries, entry => entry.AccountCode == "SYS-SETTLEMENT-CLEARING");
        AssertBalanced(destinationEntries);
        foreach (var batch in new[] { first, second })
        {
            var details = await service.GetDetailsAsync(batch.Id);
            if (details.CommissionScope == "Incoming")
            {
                Assert.Equal(1_000_000m, details.TotalSourceAfn);
                Assert.Equal(10_000m, details.TotalSourceUsd);
                var displayed = details.Items.Single(x => x.HawalaId == afn.Id);
                Assert.Equal(afn.ReferenceNumber, displayed.ReferenceNumber);
                Assert.Equal(afn.SenderName, displayed.SenderName);
                Assert.Equal(afn.ReceiverName, displayed.ReceiverName);
                Assert.Equal(fixture.OwnLocation.Name, displayed.PaymentLocationName);
            }
            AssertBalanced(details.LedgerEntries);
            Assert.Contains(details.LedgerEntries, entry => entry.AccountId == fixture.SourceAccount.Id && entry.BadehKar == details.TotalCommissionUsd);
            Assert.Contains(details.LedgerEntries, entry => entry.AccountCode == (details.CommissionScope == "Incoming" ? "SYS-COMMISSION-INCOMING" : "SYS-COMMISSION-FORWARDING"));
        }
        Assert.Equal(150m, first.TotalCommissionUsd + second.TotalCommissionUsd);
        Assert.Equal(0, (await service.PreviewAsync(incoming)).HawalaCount);
        Assert.Equal(0, (await service.PreviewAsync(forwarding)).HawalaCount);
        Assert.Equal(0, (await service.PreviewAsync(destination)).HawalaCount);
        await Assert.ThrowsAsync<SqlException>(() => service.PostAsync(forwarding));
        await service.ReverseAsync(first.Id, "Verify independent reversal");
        var restored = await service.PreviewAsync(forwardingFirst ? forwarding : incoming);
        Assert.Equal(2, restored.HawalaCount);
        Assert.Equal(0, (await service.PreviewAsync(forwardingFirst ? incoming : forwarding)).HawalaCount);
        Assert.Equal(0, (await service.PreviewAsync(destination)).HawalaCount);
        var reversalDetails = await service.GetDetailsAsync(first.Id);
        Assert.Equal("Reversed", reversalDetails.Status);
        AssertBalanced(reversalDetails.LedgerEntries);
    }

    [Fact]
    public async Task Own_location_is_excluded_even_if_assigned_to_destination_and_incoming_still_includes_it()
    {
        var day = new DateTime(2038, 2, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var own = Receive(199_001_001, 2, 100_000m, day);
        var remote = Receive(199_001_002, 2, 100_000m, day);
        context.Hawalas.AddRange(own, remote);
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(Send(199_001_003, own, 100_000m, day, fixture.OwnLocation.Id), Send(199_001_004, remote, 50_000m, day));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateCommissionService(context);
        Assert.Equal(800m, (await service.PreviewAsync(Request("Incoming", day))).TotalCommissionUsd);
        var request = Request("Forwarding", day);
        var preview = await service.PreviewAsync(request);
        Assert.Single(preview.Items);
        Assert.Equal(200m, preview.TotalCommissionUsd);
        Assert.Equal(50_000m, preview.TotalSourceUsd);
        Assert.DoesNotContain(preview.PaymentLocationRates, x => x.PaymentLocationId == fixture.OwnLocation.Id);
        var batch = await service.PostAsync(request);
        var details = await service.GetDetailsAsync(batch.Id);
        Assert.Single(details.Items);
        Assert.All(details.Items, item => Assert.Equal(fixture.RemoteLocation.Id, item.PaymentLocationId));
    }

    [Fact]
    public async Task Only_own_location_produces_no_forwarding_batch()
    {
        var day = new DateTime(2038, 2, 12);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var source = Receive(199_002_001, 2, 100_000m, day);
        context.Hawalas.Add(source);
        await context.SaveChangesAsync();
        context.Hawalas.Add(Send(199_002_002, source, 100_000m, day, fixture.OwnLocation.Id));
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        var request = Request("Forwarding", day);
        Assert.Empty((await service.PreviewAsync(request)).Items);
        var count = await context.CorrespondentCommissionBatches.CountAsync();
        await Assert.ThrowsAsync<SqlException>(() => service.PostAsync(request));
        Assert.Equal(count, await context.CorrespondentCommissionBatches.CountAsync());
    }

    [Fact]
    public async Task Forwarding_uses_original_day_rates_and_location_rates_not_agent_commission()
    {
        var day = new DateTime(2038, 3, 10);
        var sendDay = day.AddDays(5);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var otherLocation = new PaymentLocation { Name = "Other commission location", NormalizedName = "other-commission-location", Address = "Other", CreatedBy = fixture.UserId };
        context.PaymentLocations.Add(otherLocation);
        var first = Receive(199_003_001, 1, 700_000m, day);
        var second = Receive(199_003_002, 1, 800_000m, day.AddDays(1));
        first.CommissionAmount = 1m; // Another commission type must not exclude forwarding.
        first.CommissionCurrencyId = 1;
        context.Hawalas.AddRange(first, second);
        context.CorrespondentDailyCommissionRates.AddRange(Rate(day, 70m), Rate(day.AddDays(1), 80m), Rate(sendDay, 100m));
        await context.SaveChangesAsync();
        var sent1 = Send(199_003_003, first, 350_000m, sendDay);
        sent1.AgentCommissionAmount = 20m; // Destination commission already set, forwarding is still eligible.
        sent1.AgentCommissionCurrencyId = 1;
        context.Hawalas.AddRange(sent1, Send(199_003_004, second, 400_000m, sendDay, otherLocation.Id));
        await context.SaveChangesAsync();
        var request = Request("Forwarding", sendDay);
        request.PaymentLocationRates.Add(new() { PaymentLocationId = otherLocation.Id, PerLakhRate = 600m });
        var service = fixture.CreateCommissionService(context);
        var preview = await service.PreviewAsync(request);
        Assert.Equal(10_000m, preview.TotalBaseAfn);
        Assert.Equal(50m, preview.TotalCommissionUsd);
        Assert.Equal(20m, preview.PaymentLocationRates.Single(x => x.PaymentLocationId == fixture.RemoteLocation.Id).TotalCommissionUsd);
        Assert.Equal(30m, preview.PaymentLocationRates.Single(x => x.PaymentLocationId == otherLocation.Id).TotalCommissionUsd);
        var batch = await service.PostAsync(request);
        var details = await service.GetDetailsAsync(batch.Id);
        Assert.Contains(details.Items, item => item.SourceToAfnRate == 70m && item.ValuationDate == day);
        Assert.Contains(details.Items, item => item.SourceToAfnRate == 80m && item.ValuationDate == day.AddDays(1));
        Assert.Equal(50m, details.TotalCommissionUsd);
        Assert.Equal(2, (await service.GetHistoryAsync(fixture.SourceCorrespondent.Id)).Single(x => x.Id == batch.Id).LocationSummaries.Count);
    }

    [Fact]
    public async Task Missing_source_rate_blocks_income_but_not_destination_expense()
    {
        var day = new DateTime(2038, 4, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var source = Receive(199_004_001, 1, 100_000m, day);
        context.Hawalas.Add(source);
        context.DailyCommissionRates.Add(new DailyCommissionRate { RateDate = day, UsdToAfnRate = 70m, CreatedBy = fixture.UserId });
        await context.SaveChangesAsync();
        context.Hawalas.Add(Send(199_004_002, source, 50_000m, day));
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        await Assert.ThrowsAsync<SqlException>(() => service.PreviewAsync(Request("Incoming", day)));
        await Assert.ThrowsAsync<SqlException>(() => service.PreviewAsync(Request("Forwarding", day)));
        var destination = await service.PostAsync(Request("Destination", day));
        Assert.Equal(100m, destination.TotalCommissionAfn);
        Assert.DoesNotContain((await service.GetDetailsAsync(destination.Id)).LedgerEntries, x => x.AccountId == fixture.SourceAccount.Id);
        context.CorrespondentDailyCommissionRates.Add(Rate(day, 70m));
        await context.SaveChangesAsync();
        Assert.Equal(3m, (await service.PostAsync(Request("Forwarding", day))).TotalCommissionUsd);
    }

    [Fact]
    public async Task Cancelled_forwarding_deduction_retains_original_rate_and_destination_stays_balanced()
    {
        var day = new DateTime(2038, 5, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var cancelled = Receive(199_005_001, 2, 100_000m, day);
        context.Hawalas.Add(cancelled);
        await context.SaveChangesAsync();
        var cancelledSend = Send(199_005_002, cancelled, 100_000m, day);
        context.Hawalas.Add(cancelledSend);
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        await service.PostAsync(Request("Forwarding", day));
        await service.PostAsync(Request("Destination", day));
        var items = await context.CorrespondentCommissionBatchItems.Where(x => x.HawalaId == cancelledSend.Id).ToListAsync();
        foreach (var item in items) item.IsActive = false;
        cancelledSend.Status = "Cancel";
        await context.SaveChangesAsync();
        var next = Receive(199_005_003, 2, 200_000m, day.AddDays(1));
        context.Hawalas.Add(next);
        await context.SaveChangesAsync();
        context.Hawalas.Add(Send(199_005_004, next, 200_000m, day.AddDays(1)));
        await context.SaveChangesAsync();
        var forwarding = Request("Forwarding", day.AddDays(1));
        forwarding.PaymentLocationRates[0].PerLakhRate = 600m;
        var preview = await service.PreviewAsync(forwarding);
        Assert.Equal(800m, preview.TotalCommissionUsd); // 1,200 at new rate minus original 400.
        Assert.Equal(800m, preview.PaymentLocationRates.Single().TotalCommissionUsd);
        var batch = await service.PostAsync(forwarding);
        Assert.Equal(preview.TotalCommissionUsd, batch.TotalCommissionUsd);
        AssertBalanced((await service.GetDetailsAsync(batch.Id)).LedgerEntries);
        var destinationRequest = Request("Destination", day.AddDays(1));
        destinationRequest.LocationCurrencyRates.Single(x => x.CurrencyId == 2).PerLakhRate = 250m;
        var destinationPreview = await service.PreviewAsync(destinationRequest);
        Assert.Equal(400m, destinationPreview.TotalCommissionUsd); // New 500 minus original saved 100.
        Assert.Equal(250m, destinationPreview.LocationCurrencyRates.Single().PerLakhRate);
        Assert.Equal(100m, destinationPreview.Items.Single(x => x.SourceAmount < 0).PerLakhRate);
        var destination = await service.PostAsync(destinationRequest);
        Assert.Equal(400m, destination.TotalCommissionUsd);
        var details = await service.GetDetailsAsync(destination.Id);
        Assert.Equal(100m, details.Items.Single(x => x.SourceAmount < 0).PerLakhRate);
        AssertBalanced(details.LedgerEntries);
        Assert.DoesNotContain(details.LedgerEntries, x => x.AccountId == fixture.SourceAccount.Id);
    }

    [Fact]
    public async Task Posted_original_day_rate_is_frozen_until_forwarding_reversed_but_destination_does_not_freeze_it()
    {
        var day = new DateTime(2021, 10, 2);
        var sendDay = day.AddDays(1);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var source = Receive(199_006_001, 1, 100_000m, day);
        context.Hawalas.Add(source);
        await context.SaveChangesAsync();
        context.Hawalas.Add(Send(199_006_002, source, 50_000m, sendDay));
        await context.SaveChangesAsync();
        var rates = new CorrespondentDailyRateService(context);
        await rates.SaveAsync(fixture.SourceCorrespondent.Id, day, 70m);
        var service = fixture.CreateCommissionService(context);
        await service.PostAsync(Request("Destination", sendDay));
        await rates.SaveAsync(fixture.SourceCorrespondent.Id, day, 80m);
        var forwarding = await service.PostAsync(Request("Forwarding", sendDay));
        Assert.Equal(3m, forwarding.TotalCommissionUsd);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rates.SaveAsync(fixture.SourceCorrespondent.Id, day, 90m));
        var unchanged = await service.GetDetailsAsync(forwarding.Id);
        Assert.Equal(80m, unchanged.Items.Single().SourceToAfnRate);
        await service.ReverseAsync(forwarding.Id, "Rate correction");
        await rates.SaveAsync(fixture.SourceCorrespondent.Id, day, 90m);
        Assert.Equal(80m, (await service.GetDetailsAsync(forwarding.Id)).Items.Single().SourceToAfnRate);
    }

    [Fact]
    public async Task Editing_after_reversal_preserves_generated_identity_and_historical_snapshots()
    {
        var day = new DateTime(2038, 6, 16);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var source = Receive(199_183_001, 2, 100_000m, day);
        source.PaymentLocationId = fixture.RemoteLocation.Id;
        source.PaidFromAccountId = fixture.DestinationAccount.Id;
        context.Hawalas.Add(source);
        await context.SaveChangesAsync();
        var send = Send(199_183_002, source, 100_000m, day);
        context.Hawalas.Add(send);
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        var batch = await service.PostAsync(Request("Forwarding", day));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService(context).UpdateHawalaAsync(source.Id, new() { FromAmount = 200_000m, ToAmount = 200_000m }));
        context.ChangeTracker.Clear();
        await service.ReverseAsync(batch.Id, "Correct transfer amount");
        await fixture.CreateService(context).UpdateHawalaAsync(source.Id, new() { FromAmount = 200_000m, ToAmount = 200_000m, PaymentLocationId = fixture.RemoteLocation.Id });
        Assert.Equal(send.Id, await context.Hawalas.Where(x => x.SourceHawalaId == source.Id).Select(x => x.Id).SingleAsync());
        Assert.Equal(100_000m, (await service.GetDetailsAsync(batch.Id)).Items.Single().SourceAmount);
        Assert.Equal(800m, (await service.PreviewAsync(Request("Forwarding", day))).TotalCommissionUsd);
    }

    [Fact]
    public async Task Concurrent_forwarding_posts_create_only_one_batch()
    {
        var day = new DateTime(2038, 6, 14);
        await using (var seed = fixture.CreateContext())
        {
            using var bypass = seed.BypassSubscriptionEnforcement();
            await ConfigureOwnLocationAsync(seed);
            var source = Receive(199_182_001, 2, 100_000m, day);
            seed.Hawalas.Add(source);
            await seed.SaveChangesAsync();
            seed.Hawalas.Add(Send(199_182_002, source, 50_000m, day));
            await seed.SaveChangesAsync();
        }
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        using var firstBypass = first.BypassSubscriptionEnforcement();
        using var secondBypass = second.BypassSubscriptionEnforcement();
        async Task<object> TryPost(HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
        {
            try { return await fixture.CreateCommissionService(context).PostAsync(Request("Forwarding", day)); }
            catch (SqlException error) { return error; }
        }
        var results = await Task.WhenAll(TryPost(first), TryPost(second));
        Assert.Single(results.OfType<CorrespondentCommissionBatchDto>());
        Assert.Single(results.OfType<SqlException>());
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.CorrespondentCommissionBatches.CountAsync(x => x.CommissionScope == "Forwarding" && x.PeriodFrom == day && x.Status == "Posted"));
    }

    [Fact]
    public async Task Small_hawala_commissions_are_rounded_by_group_and_item_shares_sum_exactly()
    {
        var day = new DateTime(2038, 6, 12);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var sources = Enumerable.Range(0, 600).Select(i => Receive(199_180_000 + i, 2, 1m, day)).ToList();
        context.Hawalas.AddRange(sources);
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(sources.Select((source, i) => Send(199_181_000 + i, source, 1m, day)));
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        foreach (var scope in new[] { "Incoming", "Forwarding", "Destination" })
        {
            var preview = await service.PreviewAsync(Request(scope, day));
            Assert.Equal(scope == "Destination" ? 1m : 2m, preview.TotalCommissionUsd);
            Assert.Equal(preview.TotalCommissionUsd, preview.Items.Sum(x => x.CommissionAfn));
            Assert.All(preview.Items, item => Assert.Equal(decimal.Truncate(item.CommissionAfn), item.CommissionAfn));
            var batch = await service.PostAsync(Request(scope, day));
            Assert.Equal(preview.TotalCommissionUsd, batch.TotalCommissionUsd);
            AssertBalanced((await service.GetDetailsAsync(batch.Id)).LedgerEntries);
        }
    }

    [Fact]
    public async Task Destination_rates_are_independent_for_each_location_and_currency_and_saved_in_history()
    {
        var day = new DateTime(2038, 7, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var otherLocation = new PaymentLocation { Name = "Other payable location", NormalizedName = "other-payable-location", Address = "Other", CreatedBy = fixture.UserId };
        context.PaymentLocations.Add(otherLocation);
        var sources = new[]
        {
            Receive(199_200_001, 1, 500_000m, day), Receive(199_200_002, 2, 100_000m, day),
            Receive(199_200_003, 1, 500_000m, day), Receive(199_200_004, 2, 100_000m, day)
        };
        context.Hawalas.AddRange(sources);
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(sources.Select((source, index) =>
            Send(199_200_011 + index, source, source.FromAmount, day,
                index < 2 ? fixture.RemoteLocation.Id : otherLocation.Id)));
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        var request = Request("Destination", day);
        request.LocationCurrencyRates.Clear();
        var initial = await service.PreviewAsync(request);
        Assert.Equal(4, initial.LocationCurrencyRates.Count);
        Assert.All(initial.LocationCurrencyRates, x => Assert.Equal(0m, x.PerLakhRate));
        Assert.Equal(0m, initial.TotalCommissionAfn);
        Assert.Equal(0m, initial.TotalCommissionUsd);
        request.LocationCurrencyRates =
        [
            new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 1, PerLakhRate = 200m },
            new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 2, PerLakhRate = 100m },
            new() { PaymentLocationId = otherLocation.Id, CurrencyId = 1, PerLakhRate = 300m },
            new() { PaymentLocationId = otherLocation.Id, CurrencyId = 2, PerLakhRate = 150m }
        ];
        var preview = await service.PreviewAsync(request);
        Assert.Equal(2_500m, preview.TotalCommissionAfn);
        Assert.Equal(250m, preview.TotalCommissionUsd);
        Assert.Equal(1_000m, preview.LocationCurrencyRates.Single(x => x.PaymentLocationId == fixture.RemoteLocation.Id && x.CurrencyId == 1).CommissionAmount);
        Assert.Equal(1_500m, preview.LocationCurrencyRates.Single(x => x.PaymentLocationId == otherLocation.Id && x.CurrencyId == 1).CommissionAmount);
        Assert.Equal(150m, preview.LocationCurrencyRates.Single(x => x.PaymentLocationId == otherLocation.Id && x.CurrencyId == 2).CommissionAmount);
        var batch = await service.PostAsync(request);
        Assert.Equal(preview.TotalCommissionAfn, batch.TotalCommissionAfn);
        Assert.Equal(preview.TotalCommissionUsd, batch.TotalCommissionUsd);
        var details = await service.GetDetailsAsync(batch.Id);
        Assert.All(details.Items, item => Assert.Equal(request.LocationCurrencyRates.Single(x =>
            x.PaymentLocationId == item.PaymentLocationId && x.CurrencyId == item.CurrencyId).PerLakhRate, item.PerLakhRate));
        AssertBalanced(details.LedgerEntries);
        Assert.DoesNotContain(details.LedgerEntries, x => x.AccountId == fixture.SourceAccount.Id);
        Assert.Contains(details.LedgerEntries, x => x.AccountId == fixture.DestinationAccount.Id && x.CurrencyCode == "AFN" && x.TalabKar == 2_500m);
        await service.ReverseAsync(batch.Id, "Verify saved per-location rates");
        var reversed = await service.GetDetailsAsync(batch.Id);
        Assert.Equal("Reversed", reversed.Status);
        Assert.All(reversed.Items, item => Assert.False(item.IsActive));
        Assert.Equal(4, (await service.PreviewAsync(request)).HawalaCount);
    }

    [Fact]
    public async Task Destination_requires_every_location_currency_pair_and_rejects_invalid_or_duplicate_rates()
    {
        var day = new DateTime(2038, 7, 12);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var first = Receive(199_201_001, 1, 100_000m, day);
        var second = Receive(199_201_002, 2, 100_000m, day);
        context.Hawalas.AddRange(first, second);
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(Send(199_201_003, first, first.FromAmount, day),
            Send(199_201_004, second, second.FromAmount, day, fixture.OwnLocation.Id));
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        var request = Request("Destination", day);
        var error = await Assert.ThrowsAsync<SqlException>(() => service.PostAsync(request));
        Assert.Equal(50023, error.Number);
        Assert.False(await context.CorrespondentCommissionBatches.AnyAsync(x =>
            x.CommissionScope == "Destination" && x.PeriodFrom == day));
        request.LocationCurrencyRates.Add(new() { PaymentLocationId = fixture.OwnLocation.Id, CurrencyId = 2, PerLakhRate = 250m });
        request.LocationCurrencyRates.Add(new() { PaymentLocationId = fixture.OwnLocation.Id, CurrencyId = 2, PerLakhRate = 300m });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(request));
        request.LocationCurrencyRates.RemoveAt(request.LocationCurrencyRates.Count - 1);
        request.LocationCurrencyRates.Last().PerLakhRate = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(request));
        request.LocationCurrencyRates.Clear();
        request.CurrencyRates = [new() { CurrencyId = 1, PerLakhRate = 200m }, new() { CurrencyId = 2, PerLakhRate = 100m }];
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(request));
    }

    [Fact]
    public async Task Destination_rounds_final_currency_total_across_locations()
    {
        var day = new DateTime(2038, 7, 14);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var sources = new[] { Receive(199_202_001, 2, 1m, day), Receive(199_202_002, 2, 1m, day) };
        context.Hawalas.AddRange(sources);
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(Send(199_202_003, sources[0], 1m, day), Send(199_202_004, sources[1], 1m, day, fixture.OwnLocation.Id));
        await context.SaveChangesAsync();
        var request = Request("Destination", day);
        request.LocationCurrencyRates =
        [
            new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 2, PerLakhRate = 60000m },
            new() { PaymentLocationId = fixture.OwnLocation.Id, CurrencyId = 2, PerLakhRate = 60000m }
        ];
        var service = fixture.CreateCommissionService(context);
        var preview = await service.PreviewAsync(request);
        Assert.Equal(1m, preview.TotalCommissionUsd); // 0.6 + 0.6 => 1, not 1 + 1.
        Assert.Equal(1m, preview.Items.Sum(x => x.CommissionAfn));
        Assert.All(preview.Items, x => Assert.Equal(decimal.Truncate(x.CommissionAfn), x.CommissionAfn));
        var batch = await service.PostAsync(request);
        Assert.Equal(preview.TotalCommissionUsd, batch.TotalCommissionUsd);
        AssertBalanced((await service.GetDetailsAsync(batch.Id)).LedgerEntries);
    }

    [Fact]
    public async Task Forwarding_preview_and_post_for_10000_hawalas_finish_within_30_seconds_without_valuation_writes()
    {
        const int count = 10_000;
        var day = new DateTime(2038, 6, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        context.CorrespondentDailyCommissionRates.Add(Rate(day, 70m));
        var sources = Enumerable.Range(0, count).Select(i => Receive(199_100_000 + i, i % 2 == 0 ? 1 : 2, i % 2 == 0 ? 70_000m : 1_000m, day)).ToList();
        context.Hawalas.AddRange(sources);
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(sources.Select((source, index) => Send(199_120_000 + index, source, source.FromAmount / 2, day)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateCommissionService(context);
        var request = Request("Forwarding", day);
        var previewWatch = Stopwatch.StartNew();
        var preview = await service.PreviewAsync(request);
        previewWatch.Stop();
        var repeatWatch = Stopwatch.StartNew();
        var repeated = await service.PreviewAsync(request);
        repeatWatch.Stop();
        var postWatch = Stopwatch.StartNew();
        var posted = await service.PostAsync(request);
        postWatch.Stop();
        Assert.Equal(count, preview.HawalaCount);
        Assert.Equal(20_000m, preview.TotalCommissionUsd);
        Assert.Equal(preview.TotalCommissionUsd, repeated.TotalCommissionUsd);
        Assert.Equal(preview.TotalCommissionUsd, posted.TotalCommissionUsd);
        Assert.False(await context.Hawalas.AnyAsync(x => x.CreatedAt == day.AddHours(10).ToUniversalTime() && x.CommissionValuedAt != null));
        Assert.True(previewWatch.Elapsed < TimeSpan.FromSeconds(30));
        Assert.True(repeatWatch.Elapsed < TimeSpan.FromSeconds(30));
        Assert.True(postWatch.Elapsed < TimeSpan.FromSeconds(30));
        output.WriteLine(JsonSerializer.Serialize(new { count, previewMs = previewWatch.Elapsed.TotalMilliseconds, repeatMs = repeatWatch.Elapsed.TotalMilliseconds, postMs = postWatch.Elapsed.TotalMilliseconds }));
        var destination = Request("Destination", day);
        var payablePreviewWatch = Stopwatch.StartNew();
        var payablePreview = await service.PreviewAsync(destination);
        payablePreviewWatch.Stop();
        var payablePostWatch = Stopwatch.StartNew();
        var payablePosted = await service.PostAsync(destination);
        payablePostWatch.Stop();
        Assert.Equal(count, payablePreview.HawalaCount);
        Assert.Equal(payablePreview.TotalCommissionAfn, payablePosted.TotalCommissionAfn);
        Assert.Equal(payablePreview.TotalCommissionUsd, payablePosted.TotalCommissionUsd);
        Assert.True(payablePreviewWatch.Elapsed < TimeSpan.FromSeconds(30));
        Assert.True(payablePostWatch.Elapsed < TimeSpan.FromSeconds(30));
        output.WriteLine(JsonSerializer.Serialize(new { count, destinationPreviewMs = payablePreviewWatch.Elapsed.TotalMilliseconds, destinationPostMs = payablePostWatch.Elapsed.TotalMilliseconds }));
    }

    private static void AssertBalanced(IEnumerable<LedgerEntryDto> entries)
    {
        foreach (var currency in entries.GroupBy(x => x.CurrencyCode))
            Assert.Equal(currency.Sum(x => x.BadehKar), currency.Sum(x => x.TalabKar));
    }

    [Fact]
    public async Task Forwarding_rounds_each_day_across_locations_not_each_hawala_or_period()
    {
        var day = new DateTime(2038, 8, 20);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var other = new PaymentLocation { Name = "Daily rounding location", NormalizedName = "daily-rounding-location", CreatedBy = fixture.UserId };
        context.PaymentLocations.Add(other);
        var sources = new[]
        {
            Receive(199_300_001, 2, 50m, day), Receive(199_300_002, 2, 75m, day),
            Receive(199_300_003, 2, 50m, day.AddDays(1)), Receive(199_300_004, 2, 75m, day.AddDays(1))
        };
        context.Hawalas.AddRange(sources);
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(sources.Select((source, index) =>
            Send(199_300_011 + index, source, source.FromAmount, day.AddDays(index / 2), index % 2 == 0 ? fixture.RemoteLocation.Id : other.Id)));
        await context.SaveChangesAsync();
        var request = Request("Forwarding", day);
        request.PeriodTo = day.AddDays(1);
        request.PaymentLocationRates.Add(new() { PaymentLocationId = other.Id, PerLakhRate = 400m });
        var service = fixture.CreateCommissionService(context);
        var preview = await service.PreviewAsync(request);
        Assert.Equal(2m, preview.TotalCommissionUsd); // Each day 0.2 + 0.3 => 1. Period-only rounding would yield 1.
        Assert.All(preview.Items.GroupBy(x => x.HawalaDate.Date), group => Assert.Equal(1m, group.Sum(x => x.CommissionAfn)));
        Assert.All(preview.Items, item => Assert.Equal(decimal.Truncate(item.CommissionAfn), item.CommissionAfn));
        var posted = await service.PostAsync(request);
        Assert.Equal(preview.TotalCommissionUsd, posted.TotalCommissionUsd);
        var details = await service.GetDetailsAsync(posted.Id);
        AssertBalanced(details.LedgerEntries);
        Assert.All(details.LedgerEntries, entry =>
        {
            Assert.Equal(decimal.Truncate(entry.TalabKar), entry.TalabKar);
            Assert.Equal(decimal.Truncate(entry.BadehKar), entry.BadehKar);
        });
        await service.ReverseAsync(posted.Id, "Test whole-unit reversal");
        Assert.Equal(2m, (await service.PreviewAsync(request)).TotalCommissionUsd);
    }

    private async Task ConfigureOwnLocationAsync(HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
    {
        var setting = await context.CompanySettings.FirstOrDefaultAsync();
        if (setting == null)
        {
            setting = new CompanySetting { CompanyName = "Commission test company" };
            context.CompanySettings.Add(setting);
        }
        setting.OwnPaymentLocationId = fixture.OwnLocation.Id;
        await context.SaveChangesAsync();
    }

    private CorrespondentCommissionPreviewRequestDto Request(string scope, DateTime day) => new()
    {
        CommissionScope = scope, HawalaType = scope == "Incoming" ? "HawalaReceive" : "HawalaSend",
        CorrespondentId = scope == "Destination" ? fixture.DestinationCorrespondent.Id : fixture.SourceCorrespondent.Id,
        PeriodFrom = day, PeriodTo = day, CommissionPerLakhAfn = 400m,
        PaymentLocationRates = scope == "Forwarding" ? [new() { PaymentLocationId = fixture.RemoteLocation.Id, PerLakhRate = 400m }] : [],
        LocationCurrencyRates = scope == "Destination" ?
            [new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 1, PerLakhRate = 200m },
             new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 2, PerLakhRate = 100m }] : []
    };

    private Hawala Receive(long number, long currency, decimal amount, DateTime day) => new()
    {
        Number = number, HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id,
        PaymentLocationId = fixture.OwnLocation.Id, FromCurrencyId = currency, ToCurrencyId = currency,
        FromAmount = amount, ToAmount = amount, Status = "Paid", CreatedBy = fixture.UserId,
        CreatedAt = day.AddHours(10).ToUniversalTime(), SenderName = "Commission test sender", ReceiverName = "Receiver"
    };

    private Hawala Send(long number, Hawala source, decimal amount, DateTime day, long? locationId = null) => new()
    {
        Number = number, HawalaType = "HawalaSend", CorrespondentId = fixture.DestinationCorrespondent.Id,
        SourceHawalaId = source.Id, IsSystemGenerated = true, PaymentLocationId = locationId ?? fixture.RemoteLocation.Id,
        FromCurrencyId = source.FromCurrencyId, ToCurrencyId = source.FromCurrencyId, FromAmount = amount, ToAmount = amount,
        Status = "Paid", CreatedBy = fixture.UserId, CreatedAt = day.AddHours(10).ToUniversalTime(),
        SenderName = "Commission test sender", ReceiverName = "Receiver"
    };

    private CorrespondentDailyCommissionRate Rate(DateTime day, decimal rate) => new()
    {
        CorrespondentId = fixture.SourceCorrespondent.Id, RateDate = day, UsdToAfnRate = rate,
        CreatedBy = fixture.UserId, CreatedAt = DateTime.UtcNow
    };
}
