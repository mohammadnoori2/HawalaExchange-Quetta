using System.Globalization;

namespace HawalaExchange.Application.Helpers;

/// <summary>Display only: never used for storage, API parameters or accounting dates.</summary>
public class DisplayDateFormatter
{
    public bool UsePersianCalendar { get; private set; } = true;
    public void SetCalendar(bool usePersianCalendar) => UsePersianCalendar = usePersianCalendar;

    public string Date(System.DateTime? value, string emptyText = "—")
    {
        if (!value.HasValue || value == System.DateTime.MinValue) return emptyText;
        var date = LocalDate(value.Value);
        if (!UsePersianCalendar) return date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var calendar = new PersianCalendar();
        return FormattableString.Invariant($"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}");
    }

    public string Date(DateOnly value) => Date(value.ToDateTime(TimeOnly.MinValue));

    public string DateTime(System.DateTime? value, bool use24Hour = false, bool showSeconds = false, string emptyText = "—")
    {
        if (!value.HasValue || value == System.DateTime.MinValue) return emptyText;
        var format = use24Hour ? showSeconds ? "HH:mm:ss" : "HH:mm" : showSeconds ? "hh:mm:ss tt" : "hh:mm tt";
        return $"{Date(value)} {LocalDate(value.Value).ToString(format, CultureInfo.InvariantCulture)}";
    }

    public string Month(System.DateTime value) => value == System.DateTime.MinValue ? string.Empty : Date(value)[..7];
    private static System.DateTime LocalDate(System.DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
}
