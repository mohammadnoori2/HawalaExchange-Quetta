using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Helpers;

namespace HawalaExchange.PerformanceTests;

public class CommissionPreviewStateTests
{
    [Fact]
    public void PostingRequiresASuccessfulCalculation()
    {
        var state = new CommissionPreviewState();
        var request = Request();
        Assert.False(state.IsCurrent(request));
        Assert.Throws<InvalidOperationException>(() => state.ForPosting(request));
        state.Accept(request);
        Assert.True(state.IsCurrent(request));
        Assert.NotSame(request, state.ForPosting(request));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangingEitherCommissionRateRequiresRecalculation(bool destination)
    {
        var state = new CommissionPreviewState();
        var request = Request();
        state.Accept(request);
        if (destination) request.LocationCurrencyRates[0].PerLakhRate = 500;
        else request.PaymentLocationRates[0].PerLakhRate = 500;
        Assert.False(state.IsCurrent(request));
        Assert.Throws<InvalidOperationException>(() => state.ForPosting(request));
        state.Accept(request);
        Assert.True(state.IsCurrent(request));
    }

    [Fact]
    public void InvalidationRejectsEvenUncommittedInputAndIsolatedSnapshotsDoNotChange()
    {
        var state = new CommissionPreviewState();
        var request = Request();
        state.Accept(request);
        var snapshot = state.ForPosting(request);
        state.Invalidate();
        Assert.False(state.IsCurrent(request));
        request.PaymentLocationRates[0].PerLakhRate = 900;
        Assert.Equal(400, snapshot.PaymentLocationRates[0].PerLakhRate);
    }

    [Fact]
    public void DateChangesAlsoInvalidatePosting()
    {
        var state = new CommissionPreviewState();
        var request = Request();
        state.Accept(request);
        request.PeriodTo = request.PeriodTo.AddDays(1);
        Assert.False(state.IsCurrent(request));
    }

    private static CorrespondentCommissionPreviewRequestDto Request() => new()
    {
        CorrespondentId = 1,
        PeriodFrom = new DateTime(2026, 10, 1), PeriodTo = new DateTime(2026, 10, 31),
        PaymentLocationRates = [new() { PaymentLocationId = 1, PerLakhRate = 400 }],
        LocationCurrencyRates = [new() { PaymentLocationId = 1, CurrencyId = 1, PerLakhRate = 200 }]
    };
}
