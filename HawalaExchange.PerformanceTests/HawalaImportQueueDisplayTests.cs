using HawalaExchange.Application.Services;

namespace HawalaExchange.PerformanceTests;

public sealed class HawalaImportQueueDisplayTests
{
    [Theory]
    [InlineData("Preview", "پیش‌نمایش")]
    [InlineData("Queued", "در صف")]
    [InlineData("Running", "در حال ثبت")]
    [InlineData("Failed", "ناموفق")]
    [InlineData("Completed", "تکمیل شده")]
    public void History_filters_use_readable_queue_statuses(string status, string expected) =>
        Assert.Equal(expected, AppDisplayText.Status(status));
}
