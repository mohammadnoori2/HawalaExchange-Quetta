using System.Globalization;
using System.Numerics;

namespace HawalaExchange.Application.Services;

/// <summary>
/// Converts monetary values to readable Dari words. This helper is UI-independent
/// so it can be reused by receipts, reports, exports, and other application areas.
/// </summary>
public static class DariNumberToWords
{
    private static readonly string[] Ones =
    [
        "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"
    ];

    private static readonly string[] Teens =
    [
        "ده", "یازده", "دوازده", "سیزده", "چهارده",
        "پانزده", "شانزده", "هفده", "هجده", "نوزده"
    ];

    private static readonly string[] Tens =
    [
        "", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"
    ];

    private static readonly string[] Hundreds =
    [
        "", "صد", "دو صد", "سه صد", "چهار صد",
        "پنج صد", "شش صد", "هفت صد", "هشت صد", "نه صد"
    ];

    private static readonly string[] Scales =
    [
        "", "هزار", "میلیون", "میلیارد", "تریلیون",
        "کوادریلیون", "کوینتیلیون", "سکستیلیون", "سپتیلیون", "اکتیلیون"
    ];

    private static readonly string[] FractionScales =
    [
        "", "دهم", "صدم", "هزارم", "ده هزارم",
        "صد هزارم", "میلیونم", "ده میلیونم", "صد میلیونم"
    ];

    public static string ToWords(decimal value)
    {
        if (value == 0)
            return "صفر";

        var isNegative = value < 0;
        var absoluteValue = decimal.Abs(value);
        var invariantValue = absoluteValue.ToString(
            "0.############################",
            CultureInfo.InvariantCulture);
        var parts = invariantValue.Split('.');
        var integerPart = BigInteger.Parse(parts[0], CultureInfo.InvariantCulture);
        var result = ToIntegerWords(integerPart);

        if (parts.Length > 1)
        {
            var fractionDigits = parts[1].TrimEnd('0');
            if (fractionDigits.Length > 0)
            {
                var fractionValue = BigInteger.Parse(
                    fractionDigits,
                    CultureInfo.InvariantCulture);
                var fractionScale = GetFractionScale(fractionDigits.Length);
                result = $"{result} و {ToIntegerWords(fractionValue)} {fractionScale}";
            }
        }

        return isNegative ? $"منفی {result}" : result;
    }

    private static string ToIntegerWords(BigInteger value)
    {
        if (value == 0)
            return "صفر";

        var groups = new List<string>();
        var scaleIndex = 0;

        while (value > 0)
        {
            var groupValue = (int)(value % 1000);
            if (groupValue > 0)
            {
                if (scaleIndex >= Scales.Length)
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        "مقدار برای تبدیل به حروف بیش از حد بزرگ است.");

                var groupWords = ToThreeDigitWords(groupValue);
                if (!string.IsNullOrEmpty(Scales[scaleIndex]))
                    groupWords = $"{groupWords} {Scales[scaleIndex]}";

                groups.Insert(0, groupWords);
            }

            value /= 1000;
            scaleIndex++;
        }

        return string.Join(" و ", groups);
    }

    private static string ToThreeDigitWords(int value)
    {
        var parts = new List<string>();
        var hundreds = value / 100;
        var remainder = value % 100;

        if (hundreds > 0)
            parts.Add(Hundreds[hundreds]);

        if (remainder is >= 10 and <= 19)
        {
            parts.Add(Teens[remainder - 10]);
        }
        else
        {
            var tens = remainder / 10;
            var ones = remainder % 10;

            if (tens > 0)
                parts.Add(Tens[tens]);
            if (ones > 0)
                parts.Add(Ones[ones]);
        }

        return string.Join(" و ", parts);
    }

    private static string GetFractionScale(int decimalPlaces)
    {
        if (decimalPlaces < FractionScales.Length)
            return FractionScales[decimalPlaces];

        return "جزء اعشاری";
    }
}
