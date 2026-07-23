using System.Globalization;
using System.Text;

namespace HawalaExchange.Web.Helpers;

public static class PersianDateHelper
{
    private static readonly PersianCalendar PersianCalendar = new();

    public static string ToPersianDate(DateTime date)
    {
        if (date == DateTime.MinValue)
            return string.Empty;

        var localDate = ToLocalDateTime(date);

        var year = PersianCalendar.GetYear(localDate);
        var month = PersianCalendar.GetMonth(localDate);
        var day = PersianCalendar.GetDayOfMonth(localDate);

        return $"{year:0000}/{month:00}/{day:00}";
    }

    public static string ToPersianDate(DateOnly date)
    {
        if (date == DateOnly.MinValue)
            return string.Empty;

        return ToPersianDate(date.ToDateTime(TimeOnly.MinValue));
    }

    public static string ToPersianDateTime(DateTime date)
    {
        if (date == DateTime.MinValue)
            return string.Empty;

        var localDate = ToLocalDateTime(date);

        var year = PersianCalendar.GetYear(localDate);
        var month = PersianCalendar.GetMonth(localDate);
        var day = PersianCalendar.GetDayOfMonth(localDate);

        return $"{year:0000}/{month:00}/{day:00} {localDate:HH:mm}";
    }

    public static string ToPersianDateTimeWithSeconds(DateTime date)
    {
        if (date == DateTime.MinValue)
            return string.Empty;

        var localDate = ToLocalDateTime(date);

        var year = PersianCalendar.GetYear(localDate);
        var month = PersianCalendar.GetMonth(localDate);
        var day = PersianCalendar.GetDayOfMonth(localDate);

        return $"{year:0000}/{month:00}/{day:00} {localDate:HH:mm:ss}";
    }

    public static bool TryParsePersianDate(string? value, out DateTime date)
    {
        date = DateTime.MinValue;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = NormalizeDigits(value.Trim());
        value = value.Replace("-", "/");

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out var year))
            return false;

        if (!int.TryParse(parts[1], out var month))
            return false;

        if (!int.TryParse(parts[2], out var day))
            return false;

        try
        {
            date = PersianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParsePersianDateTime(string? value, out DateTime date)
    {
        date = DateTime.MinValue;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = NormalizeDigits(value.Trim());
        value = value.Replace("-", "/");

        var sections = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (sections.Length is 0 or > 2)
            return false;

        var datePart = sections[0];
        var timePart = sections.Length > 1 ? sections[1] : "00:00";

        var dateParts = datePart.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (dateParts.Length != 3)
            return false;

        if (!int.TryParse(dateParts[0], out var year))
            return false;

        if (!int.TryParse(dateParts[1], out var month))
            return false;

        if (!int.TryParse(dateParts[2], out var day))
            return false;

        var hour = 0;
        var minute = 0;

        var timeParts = timePart.Split(':');

        if (timeParts.Length is 0 or > 2 ||
            !int.TryParse(timeParts[0], out hour))
            return false;

        if (timeParts.Length == 2 && !int.TryParse(timeParts[1], out minute))
            return false;

        try
        {
            date = PersianCalendar.ToDateTime(year, month, day, hour, minute, 0, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static DateTime PersianDateToUtc(string persianDate)
    {
        if (!TryParsePersianDate(persianDate, out var localDate))
            throw new InvalidOperationException("تاریخ شمسی معتبر نیست.");

        return localDate.ToUniversalTime();
    }

    public static DateTime PersianDateTimeToUtc(string persianDateTime)
    {
        if (!TryParsePersianDateTime(persianDateTime, out var localDate))
            throw new InvalidOperationException("تاریخ و زمان شمسی معتبر نیست.");

        return localDate.ToUniversalTime();
    }

    private static DateTime ToLocalDateTime(DateTime date)
    {
        if (date.Kind == DateTimeKind.Utc)
            return date.ToLocalTime();

        return date;
    }

    private static string NormalizeDigits(string input)
    {
        var builder = new StringBuilder(input.Length);

        foreach (var ch in input)
        {
            builder.Append(ch switch
            {
                '۰' => '0',
                '۱' => '1',
                '۲' => '2',
                '۳' => '3',
                '۴' => '4',
                '۵' => '5',
                '۶' => '6',
                '۷' => '7',
                '۸' => '8',
                '۹' => '9',

                '٠' => '0',
                '١' => '1',
                '٢' => '2',
                '٣' => '3',
                '٤' => '4',
                '٥' => '5',
                '٦' => '6',
                '٧' => '7',
                '٨' => '8',
                '٩' => '9',

                _ => ch
            });
        }

        return builder.ToString();
    }
    public static string ToTime12(DateTime date)
    {
        if (date == DateTime.MinValue)
            return string.Empty;

        var localDate = ToLocalDateTime(date);

        return localDate.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture);
    }

    public static string ToPersianDateTime12(DateTime date)
    {
        if (date == DateTime.MinValue)
            return string.Empty;

        var localDate = ToLocalDateTime(date);

        var year = PersianCalendar.GetYear(localDate);
        var month = PersianCalendar.GetMonth(localDate);
        var day = PersianCalendar.GetDayOfMonth(localDate);

        var time = localDate.ToString("hh:mm tt", CultureInfo.InvariantCulture);

        return $"{year:0000}/{month:00}/{day:00} {time}";
    }
}
