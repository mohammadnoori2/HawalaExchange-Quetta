using System.Globalization;
using System.Text;

namespace HawalaExchange.Web.Helpers;

public static class MoneyFormatHelper
{
    public static string Format(decimal value, int decimalPlaces = 2) =>
        value.ToString($"N{Math.Clamp(decimalPlaces, 0, 8)}", CultureInfo.InvariantCulture);

    public static string Format(decimal? value, int decimalPlaces = 2) =>
        value.HasValue ? Format(value.Value, decimalPlaces) : string.Empty;

    public static bool TryParse(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = NormalizeDigits(value)
            .Replace(",", string.Empty)
            .Replace("٬", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("٫", ".");

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static string NormalizeDigits(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            result.Append(character switch
            {
                '۰' or '٠' => '0',
                '۱' or '١' => '1',
                '۲' or '٢' => '2',
                '۳' or '٣' => '3',
                '۴' or '٤' => '4',
                '۵' or '٥' => '5',
                '۶' or '٦' => '6',
                '۷' or '٧' => '7',
                '۸' or '٨' => '8',
                '۹' or '٩' => '9',
                _ => character
            });
        }

        return result.ToString();
    }
}
