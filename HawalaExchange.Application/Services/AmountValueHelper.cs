using System.Globalization;

namespace HawalaExchange.Application.Services;

public static class AmountValueHelper
{
    public static string Format(decimal value, int maximumDecimalPlaces = 8)
    {
        var decimalPlaces = Math.Clamp(maximumDecimalPlaces, 0, 8);
        var format = decimalPlaces == 0
            ? "#,##0"
            : $"#,##0.{new string('#', decimalPlaces)}";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string Format(decimal? value, int maximumDecimalPlaces = 8) =>
        value.HasValue ? Format(value.Value, maximumDecimalPlaces) : string.Empty;

    public static decimal RoundConvertedAmount(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
