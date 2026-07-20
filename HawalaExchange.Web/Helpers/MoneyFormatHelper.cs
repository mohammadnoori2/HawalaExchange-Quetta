using System.Globalization;
using System.Text;

namespace HawalaExchange.Web.Helpers;

public static class MoneyFormatHelper
{
    public static string Format(decimal value, int decimalPlaces = 2) =>
        value.ToString($"N{Math.Clamp(decimalPlaces, 0, 8)}", CultureInfo.InvariantCulture);

    public static string Format(decimal? value, int decimalPlaces = 2) =>
        value.HasValue ? Format(value.Value, decimalPlaces) : string.Empty;

    public static string FormatWhileTyping(string? value, int decimalPlaces = 2)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = NormalizeDigits(value)
            .Replace(",", string.Empty)
            .Replace("\u066C", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("\u066B", ".");

        var isNegative = normalized.StartsWith('-');
        if (isNegative)
            normalized = normalized[1..];

        var decimalIndex = normalized.IndexOf('.');
        var hasDecimalPoint = decimalIndex >= 0;
        var integerPart = hasDecimalPoint ? normalized[..decimalIndex] : normalized;
        var fractionPart = hasDecimalPoint ? normalized[(decimalIndex + 1)..] : string.Empty;

        integerPart = new string(integerPart.Where(char.IsDigit).ToArray());
        fractionPart = new string(fractionPart.Where(char.IsDigit).ToArray());
        fractionPart = fractionPart[..Math.Min(fractionPart.Length, Math.Clamp(decimalPlaces, 0, 8))];

        if (integerPart.Length == 0)
            integerPart = "0";

        var firstGroupLength = integerPart.Length % 3;
        if (firstGroupLength == 0)
            firstGroupLength = 3;

        var groups = new List<string> { integerPart[..firstGroupLength] };
        for (var index = firstGroupLength; index < integerPart.Length; index += 3)
            groups.Add(integerPart.Substring(index, 3));

        var formatted = string.Join(",", groups);
        if (isNegative)
            formatted = $"-{formatted}";

        if (hasDecimalPoint && decimalPlaces > 0)
            formatted += $".{fractionPart}";

        return formatted;
    }

    public static bool TryParse(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = NormalizeDigits(value)
            .Replace(",", string.Empty)
            .Replace("\u066C", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("\u066B", ".");

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
                '\u06F0' or '\u0660' => '0',
                '\u06F1' or '\u0661' => '1',
                '\u06F2' or '\u0662' => '2',
                '\u06F3' or '\u0663' => '3',
                '\u06F4' or '\u0664' => '4',
                '\u06F5' or '\u0665' => '5',
                '\u06F6' or '\u0666' => '6',
                '\u06F7' or '\u0667' => '7',
                '\u06F8' or '\u0668' => '8',
                '\u06F9' or '\u0669' => '9',
                _ => character
            });
        }

        return result.ToString();
    }
}
