using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using HawalaExchange.Web.Helpers;

namespace HawalaExchange.PerformanceTests;

public sealed class CommissionDefaultsAndPendingTests : IAsyncLifetime
{
    private readonly SqlServerPerformanceFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task All_pending_includes_late_backdated_hawalas_and_remembers_only_posted_rates_for_each_scope()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var settings = await context.CompanySettings.FirstAsync();
        settings.OwnPaymentLocationId = fixture.OwnLocation.Id;
        var secondLocation = new PaymentLocation { Name = "Commission defaults second location", NormalizedName = "commission-defaults-second-location", CreatedBy = fixture.UserId };
        context.PaymentLocations.Add(secondLocation);
        await context.SaveChangesAsync();
        var service = fixture.CreateCommissionService(context);
        long number = 299_000_000;
        foreach (var scope in new[] { "Incoming", "Destination", "Forwarding" })
        {
            var source = new Correspondent { Code = $"NEW-S-{scope}", Name = scope, CommissionMethod = "PeriodicPerLakh", SettlementCurrencyId = 2 };
            var target = new Correspondent { Code = $"NEW-D-{scope}", Name = scope, CommissionMethod = "PeriodicPerLakh", SettlementCurrencyId = 2 };
            context.Correspondents.AddRange(source, target);
            await context.SaveChangesAsync();
            context.Accounts.AddRange(
                new Account { AccountCode = source.Code, AccountName = source.Name, AccountType = "Correspondent", CorrespondentId = source.Id },
                new Account { AccountCode = target.Code, AccountName = target.Name, AccountType = "Correspondent", CorrespondentId = target.Id });
            await context.SaveChangesAsync();
            var day = new DateTime(2045, 1, 20);
            async Task<long> Add(DateTime date, long currencyId = 2, long? locationId = null)
            {
                var received = new Hawala { Number = ++number, HawalaType = "HawalaReceive", CorrespondentId = source.Id,
                    PaymentLocationId = locationId ?? fixture.RemoteLocation.Id, FromCurrencyId = currencyId, ToCurrencyId = currencyId, FromAmount = 100000, ToAmount = 100000,
                    Status = "Paid", CreatedBy = fixture.UserId, CreatedAt = date.AddHours(10).ToUniversalTime(), SenderName = "Test", ReceiverName = "Test" };
                context.Hawalas.Add(received);
                await context.SaveChangesAsync();
                if (scope == "Incoming") return received.Id;
                var sent = new Hawala { Number = ++number, HawalaType = "HawalaSend", CorrespondentId = target.Id,
                    SourceHawalaId = received.Id, IsSystemGenerated = true, PaymentLocationId = locationId ?? fixture.RemoteLocation.Id,
                    FromCurrencyId = currencyId, ToCurrencyId = currencyId, FromAmount = 100000, ToAmount = 100000, Status = "Paid",
                    CreatedBy = fixture.UserId, CreatedAt = date.AddHours(10).ToUniversalTime(), SenderName = "Test", ReceiverName = "Test" };
                context.Hawalas.Add(sent); await context.SaveChangesAsync(); return sent.Id;
            }
            var firstId = await Add(day);
            if (scope != "Incoming") await Add(day, 2, secondLocation.Id);
            if (scope == "Destination") await Add(day, 1);
            var owner = scope == "Destination" ? target.Id : source.Id;
            var request = new CorrespondentCommissionPreviewRequestDto { CorrespondentId = owner, CommissionScope = scope,
                HawalaType = scope == "Incoming" ? "HawalaReceive" : "HawalaSend", PeriodFrom = day, PeriodTo = day,
                CommissionPerLakhAfn = 425,
                PaymentLocationRates = scope == "Forwarding" ? [new() { PaymentLocationId = fixture.RemoteLocation.Id, PerLakhRate = 475 },
                    new() { PaymentLocationId = secondLocation.Id, PerLakhRate = 575 }] : [],
                LocationCurrencyRates = scope == "Destination" ? [new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 2, PerLakhRate = 525 },
                    new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 1, PerLakhRate = 575 },
                    new() { PaymentLocationId = secondLocation.Id, CurrencyId = 2, PerLakhRate = 625 }] : [] };
            var initial = await service.PreviewAsync(request);
            Assert.Equal(scope == "Incoming" ? 1 : scope == "Forwarding" ? 2 : 3, initial.HawalaCount);
            Assert.Contains(initial.Items, x => x.HawalaId == firstId);
            await service.PostAsync(request);
            var defaults = await service.GetDefaultRatesAsync(owner, scope);
            Assert.Equal(425, defaults.CommissionPerLakhAfn);
            if (scope == "Forwarding")
            {
                Assert.Equal(475, defaults.PaymentLocationRates.Single(x => x.PaymentLocationId == fixture.RemoteLocation.Id).PerLakhRate);
                Assert.Equal(575, defaults.PaymentLocationRates.Single(x => x.PaymentLocationId == secondLocation.Id).PerLakhRate);
            }
            if (scope == "Destination")
            {
                Assert.Equal(525, defaults.LocationCurrencyRates.Single(x => x.PaymentLocationId == fixture.RemoteLocation.Id && x.CurrencyId == 2).PerLakhRate);
                Assert.Equal(575, defaults.LocationCurrencyRates.Single(x => x.PaymentLocationId == fixture.RemoteLocation.Id && x.CurrencyId == 1).PerLakhRate);
                Assert.Equal(625, defaults.LocationCurrencyRates.Single(x => x.PaymentLocationId == secondLocation.Id).PerLakhRate);
            }
            using (context.UseTenantScope(999999))
                Assert.Empty((await service.GetDefaultRatesAsync(owner, scope)).LocationCurrencyRates);

            var lateId = await Add(day.AddMonths(-3));
            Assert.Empty((await service.PreviewAsync(request)).Items);
            request.AllUncalculated = true;
            Assert.Equal(lateId, Assert.Single((await service.PreviewAsync(request)).Items).HawalaId);
            if (scope == "Destination")
            {
                var firstClickRequest = CommissionPreviewState.Copy(request);
                firstClickRequest.LocationCurrencyRates.Clear();
                var calls = 0;
                var firstClickPreview = await CommissionPreviewCalculator.CalculateAsync(firstClickRequest, (input, token) =>
                { calls++; return service.PreviewAsync(input, token); });
                Assert.Equal(2, calls);
                Assert.Equal(425, firstClickPreview.TotalCommissionUsd);
                Assert.Equal(425, Assert.Single(firstClickPreview.LocationCurrencyRates).PerLakhRate);
            }
            request.CommissionPerLakhAfn = 600;
            request.PaymentLocationRates.ForEach(x => x.PerLakhRate = 650);
            request.LocationCurrencyRates.ForEach(x => x.PerLakhRate = 700);
            await service.PreviewAsync(request); // Preview alone must not replace saved defaults.
            Assert.Equal(425, (await service.GetDefaultRatesAsync(owner, scope)).CommissionPerLakhAfn);
            var second = await service.PostAsync(request);
            Assert.Empty((await service.PreviewAsync(request)).Items);
            Assert.Equal(600, (await service.GetDefaultRatesAsync(owner, scope)).CommissionPerLakhAfn);
            if (scope == "Destination")
                Assert.Equal(575, (await service.GetDefaultRatesAsync(owner, scope)).LocationCurrencyRates.Single(x => x.CurrencyId == 1).PerLakhRate);
            if (scope == "Forwarding")
                Assert.Equal(575, (await service.GetDefaultRatesAsync(owner, scope)).PaymentLocationRates.Single(x => x.PaymentLocationId == secondLocation.Id).PerLakhRate);
            await service.ReverseAsync(second.Id, "Test reversed defaults");
            Assert.Equal(425, (await service.GetDefaultRatesAsync(owner, scope)).CommissionPerLakhAfn);
            Assert.Equal(lateId, Assert.Single((await service.PreviewAsync(request)).Items).HawalaId);
        }
    }
}
