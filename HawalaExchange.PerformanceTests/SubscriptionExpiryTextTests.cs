using System.Globalization;
using HawalaExchange.Application.Helpers;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Infrastructure.Services;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;

namespace HawalaExchange.PerformanceTests;

public sealed class SubscriptionExpiryTextTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Export_countdown_is_fixed_at_report_time_and_keeps_exact_date_and_numeric_columns()
    {
        var dates = new DisplayDateFormatter(); dates.SetCalendar(false);
        var report = new SaasManagementReportDto { GeneratedAt = Now, Tenants = [new SaasTenantReportRowDto
        {
            TenantName = "Preview tenant", EndAt = Now.AddDays(125), CreatedAt = Now.AddYears(-1), Users = 7, CurrencyCode = "USD"
        }] };
        var build = typeof(SaasReportingService).GetMethod("BuildWorkbook", BindingFlags.NonPublic | BindingFlags.Static)!;
        var bytes = (byte[])build.Invoke(null, [report, new SaasReportFilterDto(), dates])!;
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var sheet = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
        var xml = XDocument.Load(sheet);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var expiry = xml.Descendants(ns + "c").Single(x => (string?)x.Attribute("r") == "F2");
        Assert.Contains("125 روز دیگر تا انقضا", expiry.Value);
        Assert.Contains(Now.AddDays(125).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture), expiry.Value);
        Assert.Equal("7", xml.Descendants(ns + "c").Single(x => (string?)x.Attribute("r") == "G2").Element(ns + "v")!.Value);
    }

    [Theory]
    [InlineData(125, "125 روز دیگر تا انقضا")]
    [InlineData(1, "1 روز دیگر تا انقضا")]
    [InlineData(-3, "3 روز از انقضا گذشته")]
    [InlineData(-125, "125 روز از انقضا گذشته")]
    [InlineData(1000, "1,000 روز دیگر تا انقضا")]
    public void Counts_calendar_days_and_preserves_english_digits(int days, string expected)
        => Assert.Equal(expected, SubscriptionExpiryText.Format(Now.AddDays(days), Now));

    [Theory]
    [InlineData(2, "امروز منقضی می‌شود")]
    [InlineData(-2, "امروز منقضی شده")]
    [InlineData(0, "امروز منقضی شده")]
    public void Distinguishes_expiration_today_from_already_expired_today(int hours, string expected)
        => Assert.Equal(expected, SubscriptionExpiryText.Format(Now.AddHours(hours), Now));

    [Fact]
    public void Missing_date_is_not_presented_as_unlimited_or_zero_days()
    {
        Assert.Equal("تاریخ پایان مشخص نیست", SubscriptionExpiryText.Format(null, Now));
        Assert.Equal("تاریخ پایان مشخص نیست", SubscriptionExpiryText.Format(DateTime.MinValue, Now));
    }

    [Fact]
    public void Midnight_is_evaluated_in_kabul_not_the_server_time_zone()
    {
        var beforeMidnight = new DateTime(2026, 10, 16, 19, 15, 0, DateTimeKind.Utc);
        Assert.Equal("1 روز دیگر تا انقضا", SubscriptionExpiryText.Format(beforeMidnight.AddMinutes(30), beforeMidnight));
    }

    [Fact]
    public void Sql_unspecified_timestamps_are_treated_as_utc_and_local_values_keep_the_same_instant()
    {
        var end = Now.AddDays(125);
        Assert.Equal(SubscriptionExpiryText.Format(end, Now), SubscriptionExpiryText.Format(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), Now));
        Assert.Equal(SubscriptionExpiryText.Format(end, Now), SubscriptionExpiryText.Format(end.ToLocalTime(), Now.ToLocalTime()));
    }

    [Fact]
    public void Current_culture_does_not_change_the_countdown_digits()
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("fa-IR"); Assert.Equal("125 روز دیگر تا انقضا", SubscriptionExpiryText.Format(Now.AddDays(125), Now)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
