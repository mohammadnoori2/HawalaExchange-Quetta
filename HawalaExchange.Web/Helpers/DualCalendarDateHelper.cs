using System.Globalization;

namespace HawalaExchange.Web.Helpers;

public static class DualCalendarDateHelper
{
    public static DateTime LocalDate(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

    public static string GregorianText(DateTime value, bool includeTime = false) => value == DateTime.MinValue
        ? string.Empty : LocalDate(value).ToString(includeTime ? "yyyy-MM-dd hh:mm tt" : "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string PersianText(DateTime value, bool includeTime = false)
    {
        if (value == DateTime.MinValue) return string.Empty;
        var date = LocalDate(value);
        var calendar = new PersianCalendar();
        var text = FormattableString.Invariant($"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}");
        return includeTime ? $"{text} {date.ToString("hh:mm tt", CultureInfo.InvariantCulture)}" : text;
    }

    public static DateTime FirstDay(int year, int month, bool gregorian) => gregorian
        ? new DateTime(year, month, 1) : new PersianCalendar().ToDateTime(year, month, 1, 0, 0, 0, 0);

    public static IReadOnlyList<DualCalendarDay> MonthDays(int year, int month, bool gregorian)
    {
        var calendar = new PersianCalendar();
        var first = FirstDay(year, month, gregorian);
        var count = gregorian ? DateTime.DaysInMonth(year, month) : calendar.GetDaysInMonth(year, month);
        var result = new List<DualCalendarDay>();
        for (var i = 0; i < ((int)first.DayOfWeek + 1) % 7; i++) result.Add(new(null, 0, 0));
        for (var day = 1; day <= count; day++)
        {
            var date = first.AddDays(day - 1);
            result.Add(new(date, day, gregorian ? calendar.GetDayOfMonth(date) : date.Day));
        }
        return result;
    }
}

public sealed record DualCalendarDay(DateTime? Date, int Day, int AlternateDay);
