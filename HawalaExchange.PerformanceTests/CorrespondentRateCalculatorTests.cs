using HawalaExchange.Application.Services;

namespace HawalaExchange.PerformanceTests;

public sealed class CorrespondentRateCalculatorTests
{
    [Theory]
    [InlineData("AFN", 2, "USD", 1)]
    [InlineData("USD", 1, "AFN", 2)]
    [InlineData("afn", 2, "usd", 1)]
    public void Standard_quotation_preserves_daily_rate(string source, int sourcePriority, string target, int targetPriority)
    {
        Assert.Equal(66.5m, CorrespondentRateCalculator.ToSettlementRate(source, sourcePriority, target, targetPriority, 66.5m));
        Assert.Equal(66.5m, CorrespondentRateCalculator.ToDailyRate(source, sourcePriority, target, targetPriority, 66.5m));
    }

    [Fact]
    public void Reverse_quotation_uses_reciprocal_not_the_wrong_conversion_direction()
    {
        Assert.Equal(.02m, CorrespondentRateCalculator.ToSettlementRate("AFN", 1, "USD", 2, 50m));
        Assert.Equal(50m, CorrespondentRateCalculator.ToDailyRate("USD", 2, "AFN", 1, .02m));
    }

    [Fact]
    public void Other_currency_pairs_are_not_mistaken_for_USD_AFN()
    {
        Assert.False(CorrespondentRateCalculator.Supports("EUR", "USD"));
        Assert.False(CorrespondentRateCalculator.Supports("USD", "USD"));
        Assert.Throws<ArgumentException>(() => CorrespondentRateCalculator.ToSettlementRate("EUR", 1, "USD", 2, 66.5m));
    }
}
