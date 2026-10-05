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

public class CommissionDailyBreakdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DaysExpandIntoLocationsAndPreserveSignedAmountsAndCalendar(bool persian)
    {
        var display = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,
            new TestCurrentTenant { TenantId = 0 });
        await display.EnsureLoadedAsync();
        display.SetCalendar(persian);
        using var services = new ServiceCollection().AddLogging().AddSingleton(display).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var firstDay = new DateTime(2026, 10, 1);
        var secondDay = firstDay.AddDays(1);
        var items = new List<CorrespondentCommissionItemDto>
        {
            Item(101, firstDay.AddHours(8), 1, "Kabul", "AFN", 100000m, 1500m, 6m),
            Item(102, firstDay.AddHours(12), 2, "Herat", "USD", 1000m, 1000m, 4m),
            Item(103, firstDay.AddHours(13), 1, "Kabul", "USD", -100m, -100m, -0.4m),
            Item(104, secondDay, 1, "Kabul", "USD", 500m, 500m, 2m)
        };
        Probe? component = null;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-AF");
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(Probe.Items)] = items,
                    [nameof(Probe.Capture)] = (Action<Probe>)(value => component = value)
                }));
                string Html() => WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains(display.Date(firstDay), Html());
                Assert.Contains(display.Date(secondDay), Html());
                Assert.Contains("12 USD", Html());
                Assert.DoesNotContain("Kabul", Html());
                component!.ToggleDay(firstDay);
                Assert.Contains("Kabul", Html());
                Assert.Contains("Herat", Html());
                Assert.Contains("10 USD", Html());
                Assert.Contains("کسر کمیشن", Html());
                Assert.Contains(">101<", Html());
                Assert.DoesNotContain(">104<", Html());
                component.ToggleDay(secondDay);
                Assert.Contains(">104<", Html());
                Assert.DoesNotContain(">101<", Html());
                Assert.DoesNotContain("Herat", Html());
                component.ToggleDay(secondDay);
                Assert.DoesNotContain("Kabul", Html());
                await component.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(Probe.Items)] = new List<CorrespondentCommissionItemDto>() }));
                Assert.Contains("0 USD", Html());
                Assert.DoesNotContain(display.Date(firstDay), Html());
            });
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static CorrespondentCommissionItemDto Item(long number, DateTime date, long location, string name,
        string currency, decimal amount, decimal basis, decimal commission) => new()
    {
        HawalaNumber = number, HawalaDate = date, ValuationDate = date.Date,
        PaymentLocationId = location, PaymentLocationName = name, CurrencyCode = currency,
        SourceAmount = amount, AfnEquivalent = basis, CommissionAfn = commission,
        PerLakhRate = 400m, SourceToAfnRate = 66m, SourceName = "Quetta", IsActive = amount >= 0
    };

    public class Probe : CommissionDailyBreakdown
    {
        [Parameter] public Action<Probe> Capture { get; set; } = default!;
        protected override void OnInitialized() => Capture(this);
    }
}
