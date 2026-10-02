using System.Globalization;
using HawalaExchange.Web.Helpers;
using Xunit;

namespace HawalaExchange.PerformanceTests;

public class DualCalendarDatePickerTests
{
    [Theory]
    [InlineData(2026, 3, 21, "1405/01/01")]
    [InlineData(2026, 10, 2, "1405/07/10")]
    [InlineData(2024, 3, 20, "1403/01/01")]
    public void SelectedDateHasBothEquivalentDates(int year, int month, int day, string persian)
    {
        var date = new DateTime(year, month, day);
        Assert.Equal(persian, DualCalendarDateHelper.PersianText(date));
        Assert.Equal(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DualCalendarDateHelper.GregorianText(date));
    }

    [Theory]
    [InlineData(2024, 2, true, 29)]
    [InlineData(2025, 2, true, 28)]
    [InlineData(1403, 12, false, 30)]
    [InlineData(1404, 12, false, 29)]
    [InlineData(1405, 1, false, 31)]
    public void CalendarGridPairsEveryDayAndHandlesLeapYears(int year, int month, bool gregorian, int count)
    {
        var grid = DualCalendarDateHelper.MonthDays(year, month, gregorian);
        var days = grid.Where(x => x.Date.HasValue).ToArray();
        var first = DualCalendarDateHelper.FirstDay(year, month, gregorian);
        Assert.Equal(count, days.Length);
        Assert.Equal(((int)first.DayOfWeek + 1) % 7, grid.Count(x => !x.Date.HasValue));
        var calendar = new PersianCalendar();
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(first.AddDays(i), days[i].Date);
            Assert.Equal(i + 1, days[i].Day);
            Assert.Equal(gregorian ? calendar.GetDayOfMonth(first.AddDays(i)) : first.AddDays(i).Day, days[i].AlternateDay);
        }
    }

    [Fact]
    public void EmptyDateHasNoInventedEquivalent()
    {
        Assert.Empty(DualCalendarDateHelper.PersianText(DateTime.MinValue));
        Assert.Empty(DualCalendarDateHelper.GregorianText(DateTime.MinValue));
    }

    [Fact]
    public void BothCalendarsKeepTimeAndEnglishDigitsUnderPersianCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
            var date = new DateTime(2026, 10, 2, 15, 45, 0);
            Assert.Equal("1405/07/10 03:45 PM", DualCalendarDateHelper.PersianText(date, true));
            Assert.Equal("2026-10-02 03:45 PM", DualCalendarDateHelper.GregorianText(date, true));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void BothCalendarsUseSameLocalDateForUtcValues()
    {
        var utc = new DateTime(2026, 10, 2, 23, 45, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();
        Assert.Equal(local, DualCalendarDateHelper.LocalDate(utc));
        Assert.Equal(DualCalendarDateHelper.PersianText(local, true), DualCalendarDateHelper.PersianText(utc, true));
        Assert.Equal(DualCalendarDateHelper.GregorianText(local, true), DualCalendarDateHelper.GregorianText(utc, true));
    }
}
