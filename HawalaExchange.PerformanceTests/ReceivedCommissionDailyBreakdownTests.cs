using System.Globalization;
using System.Net;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.PerformanceTests;

public sealed class ReceivedCommissionDailyBreakdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Days_show_signed_totals_and_expand_directly_into_received_hawalas(bool persian)
    {
        var display = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 0 });
        await display.EnsureLoadedAsync(); display.SetCalendar(persian);
        using var services = new ServiceCollection().AddLogging().AddSingleton(display).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var day = new DateTime(2026, 10, 6);
        var items = new List<CorrespondentCommissionItemDto>
        {
            Item(2172, day, "AFN", 21560, 300, 1.2m),
            Item(2173, day, "USD", -100, -100, -0.4m),
            Item(2174, day.AddDays(1), "USD", 50, 50, 0.2m)
        };
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-AF");
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                Probe? component = null;
                var root = await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(Probe.Items)] = items, [nameof(Probe.TotalCommissionUsd)] = 1m,
                    [nameof(Probe.Capture)] = (Action<Probe>)(value => component = value)
                }));
                string Html() => WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains(display.Date(day), Html());
                Assert.Contains("21,560 AFN", Html()); Assert.Contains("-100 USD", Html());
                Assert.DoesNotContain("Sender2172", Html());
                component!.ToggleDay(day);
                Assert.Contains("Sender2172", Html()); Assert.Contains("Receiver2172", Html());
                Assert.Contains("REF2172", Html()); Assert.Contains("Qonduz", Html());
                Assert.Contains("66.75", Html()); Assert.Contains("کسر کمیشن", Html());
                Assert.DoesNotContain("Sender2174", Html());
                component.ToggleDay(day.AddDays(1));
                Assert.Contains("Sender2174", Html()); Assert.DoesNotContain("Sender2172", Html());
                component.ToggleDay(day.AddDays(1));
                Assert.DoesNotContain("Sender2174", Html());
                await component.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(Probe.Items)] = new List<CorrespondentCommissionItemDto>(), [nameof(Probe.TotalCommissionUsd)] = 0m }));
                Assert.Contains("0 USD", Html()); Assert.DoesNotContain(display.Date(day), Html());
            });
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public async Task Final_total_is_the_saved_or_preview_total_not_the_sum_of_rounded_days()
    {
        var display = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 0 });
        await display.EnsureLoadedAsync();
        using var services = new ServiceCollection().AddLogging().AddSingleton(display).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var day = new DateTime(2026, 10, 6);
        var items = new[] { Item(1, day, "USD", 100, 100, 0.4m), Item(2, day.AddDays(1), "USD", 100, 100, 0.4m) };
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(Probe.Items)] = items, [nameof(Probe.TotalCommissionUsd)] = 1m, [nameof(Probe.Capture)] = (Action<Probe>)(_ => { }) }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("گردکردن نهایی", html);
            var footer = html[html.IndexOf("<tfoot>", StringComparison.Ordinal)..];
            Assert.Contains("1 USD", footer);
            Assert.Equal(0.4m, items[0].CommissionAfn); Assert.Equal(0.4m, items[1].CommissionAfn);
        });
    }

    private static CorrespondentCommissionItemDto Item(long number, DateTime day, string currency, decimal amount, decimal basis, decimal commission) => new()
    {
        HawalaId = number, HawalaNumber = number, HawalaDate = day.AddHours(10), ValuationDate = day,
        ReferenceNumber = $"REF{number}", SenderName = $"Sender{number}", ReceiverName = $"Receiver{number}",
        CurrencyCode = currency, SourceAmount = amount, AfnEquivalent = basis, CommissionAfn = commission,
        PaymentLocationName = "Qonduz", SourceToAfnRate = 66.75m, PerLakhRate = 400
    };
    public sealed class Probe : ReceivedCommissionDailyBreakdown
    {
        [Parameter] public Action<Probe> Capture { get; set; } = default!;
        protected override void OnInitialized() => Capture(this);
    }
}
