using System.Globalization;

namespace HawalaExchange.Application.Helpers;

/// <summary>Display-only calendar-day countdown in Kabul time; never decides subscription access.</summary>
public static class SubscriptionExpiryText
{
    private static readonly TimeZoneInfo Kabul = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kabul");

    public static DateTime LocalDateTime(DateTime value)
        => TimeZoneInfo.ConvertTimeFromUtc(Utc(value), Kabul);

    public static string Format(DateTime? endAt, DateTime utcNow)
    {
        if (!endAt.HasValue || endAt == DateTime.MinValue) return "تاریخ پایان مشخص نیست";
        var end = Utc(endAt.Value);
        var now = Utc(utcNow);
        var days = (LocalDateTime(end).Date - LocalDateTime(now).Date).Days;
        if (days == 0) return end <= now ? "امروز منقضی شده" : "امروز منقضی می‌شود";
        var count = Math.Abs(days).ToString("N0", CultureInfo.InvariantCulture);
        return days > 0 ? $"{count} روز دیگر تا انقضا" : $"{count} روز از انقضا گذشته";
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
