using System.Net;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Web.Components.Shared.Modals;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moq;

namespace HawalaExchange.PerformanceTests;

public class HawalaCancellationModalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_shows_original_rate_mixed_currencies_and_manual_continuation_without_executing_cancellation(bool pending)
    {
        var service = new Mock<IHawalaService>();
        service.Setup(x => x.GetCancellationPreviewAsync(1)).ReturnsAsync(new HawalaCancellationPreviewDto
        {
            HawalaId = 1, Number = 101, Sender = "<script>Quetta</script>", Receiver = "Receiver", PaymentLocation = "Barchi",
            HasCommission = true, HasConversion = true, IsClosedPeriod = true, IsCancelled = pending,
            AccountingMode = pending ? "ManualPending" : "", UsdAmount = 1000,
            Rates = [new() { Correspondent = "Quetta", SourceAmount = 65000, SourceCurrency = "AFN", TargetAmount = 1000, TargetCurrency = "USD", Rate = 65 }],
            Entries = [new() { Account = "Quetta", Currency = "USD", TalabKar = 1000 }, new() { Account = "Barchi", Currency = "AFN", BadehKar = 65000 }]
        });
        using var provider = new ServiceCollection().AddLogging().AddSingleton(service.Object).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<HawalaCancellationModal>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["HawalaId"] = 1L }));
            var html = root.ToHtmlString(); var text = WebUtility.HtmlDecode(html);
            Assert.Contains("65,000", text); Assert.Contains("1,000", text); Assert.Contains("AFN", text); Assert.Contains("USD", text);
            Assert.Contains("دوره اصلی بسته است", text); Assert.Contains("کمیشن‌های ثبت‌شده حفظ می‌شوند", text);
            Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
            Assert.Contains(pending ? "تأیید و ثبت حواله جبرانی" : "تأیید لغو و ادامه ثبت دستی", text);
        });
        service.Verify(x => x.CancelHawalaAsync(It.IsAny<long>(), It.IsAny<CancelHawalaDto>()), Times.Never);
        service.Verify(x => x.CompleteCancellationAsync(It.IsAny<long>(), It.IsAny<HawalaCompensationRequestDto>()), Times.Never);
    }
}
