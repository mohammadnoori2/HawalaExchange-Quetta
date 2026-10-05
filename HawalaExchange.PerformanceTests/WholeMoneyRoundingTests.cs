using HawalaExchange.Application.Services;

namespace HawalaExchange.PerformanceTests;

public class WholeMoneyRoundingTests
{
    [Theory]
    [InlineData("10.49", "10")]
    [InlineData("10.50", "11")]
    [InlineData("0.49", "0")]
    [InlineData("0.50", "1")]
    [InlineData("-10.49", "-10")]
    [InlineData("-10.50", "-11")]
    public void Monetary_display_rounds_half_away_from_zero(string input, string expected)
    {
        var amount = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, AmountValueHelper.Format(amount));
        Assert.Equal(expected, amount.ToWholeMoney());
        Assert.Equal("66.5", AmountValueHelper.Format(66.5m, 8)); // Exchange rates retain precision.
    }
}
