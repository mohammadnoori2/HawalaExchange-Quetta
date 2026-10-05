using System.Globalization;

namespace HawalaExchange.Application.Services;

public static class AmountValueHelper
{
    public static string Format(decimal value, int maximumDecimalPlaces = 0)
    {
        var decimalPlaces = Math.Clamp(maximumDecimalPlaces, 0, 8);
        var format = decimalPlaces == 0
            ? "#,##0"
            : $"#,##0.{new string('#', decimalPlaces)}";
        return (decimalPlaces == 0 ? RoundConvertedAmount(value) : value).ToString(format, CultureInfo.InvariantCulture);
    }

    public static string Format(decimal? value, int maximumDecimalPlaces = 0) =>
        value.HasValue ? Format(value.Value, maximumDecimalPlaces) : string.Empty;

    public static decimal RoundConvertedAmount(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    public static string ToWholeMoney(this decimal value) => Format(value, 0);
}
