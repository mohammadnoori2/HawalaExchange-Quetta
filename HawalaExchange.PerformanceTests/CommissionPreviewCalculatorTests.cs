using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Helpers;

namespace HawalaExchange.PerformanceTests;

public class CommissionPreviewCalculatorTests
{
    [Fact]
    public async Task First_click_calculates_new_pairs_with_defaults_and_preserves_saved_rates()
    {
        var request = new CorrespondentCommissionPreviewRequestDto { CommissionScope = "Destination", CommissionPerLakhAfn = 200,
            LocationCurrencyRates = [new() { PaymentLocationId = 1, CurrencyId = 2, PerLakhRate = 450 }] };
        var calls = 0;
        var preview = await CommissionPreviewCalculator.CalculateAsync(request, (r, ct) =>
        {
            calls++;
            var rate = r.LocationCurrencyRates.SingleOrDefault(x => x.CurrencyId == 1)?.PerLakhRate ?? 0;
            return Task.FromResult(new CorrespondentCommissionPreviewDto { HawalaCount = 2, TotalCommissionUsd = rate + 450,
                LocationCurrencyRates = [new() { PaymentLocationId = 1, CurrencyId = 1, PerLakhRate = rate },
                    new() { PaymentLocationId = 1, CurrencyId = 2, PerLakhRate = 450 }] });
        });
        Assert.Equal(2, calls);
        Assert.Equal(650, preview.TotalCommissionUsd);
        Assert.Equal(450, request.LocationCurrencyRates.Single(x => x.CurrencyId == 2).PerLakhRate);
        var state = new CommissionPreviewState();
        state.Accept(request);
        Assert.True(state.IsCurrent(request));
        request.LocationCurrencyRates[0].PerLakhRate = 500;
        Assert.False(state.IsCurrent(request));
    }

    [Theory]
    [InlineData("Incoming")]
    [InlineData("Forwarding")]
    [InlineData("Destination")]
    public async Task Existing_rates_or_empty_results_do_not_require_an_extra_query(string scope)
    {
        var calls = 0;
        await CommissionPreviewCalculator.CalculateAsync(new() { CommissionScope = scope }, (r, ct) =>
        { calls++; return Task.FromResult(new CorrespondentCommissionPreviewDto()); });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failed_recalculation_does_not_return_the_discovery_preview_as_ready_to_post()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommissionPreviewCalculator.CalculateAsync(
            new() { CommissionScope = "Destination", CommissionPerLakhAfn = 200 }, (r, ct) =>
            {
                if (++calls == 2) throw new InvalidOperationException("SQL failed");
                return Task.FromResult(new CorrespondentCommissionPreviewDto { HawalaCount = 1,
                    LocationCurrencyRates = [new() { PaymentLocationId = 1, CurrencyId = 2 }] });
            }));
    }

    [Fact]
    public async Task Saved_rates_need_only_one_query()
    {
        var request = new CorrespondentCommissionPreviewRequestDto { CommissionScope = "Destination",
            LocationCurrencyRates = [new() { PaymentLocationId = 1, CurrencyId = 2, PerLakhRate = 450 }] };
        var calls = 0;
        var result = await CommissionPreviewCalculator.CalculateAsync(request, (r, ct) =>
        { calls++; return Task.FromResult(new CorrespondentCommissionPreviewDto { HawalaCount = 1,
            LocationCurrencyRates = r.LocationCurrencyRates }); });
        Assert.Equal(1, calls);
        Assert.Equal(450, Assert.Single(result.LocationCurrencyRates).PerLakhRate);
    }

    [Fact]
    public async Task A_new_location_arriving_during_calculation_requires_retry()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommissionPreviewCalculator.CalculateAsync(
            new() { CommissionScope = "Destination", CommissionPerLakhAfn = 200 }, (r, ct) =>
                Task.FromResult(new CorrespondentCommissionPreviewDto { HawalaCount = 1,
                    LocationCurrencyRates = [new() { PaymentLocationId = ++calls, CurrencyId = 2 }] })));
        Assert.Equal(2, calls);
    }
}
