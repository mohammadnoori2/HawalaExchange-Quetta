using System.Net;
using System.Text.RegularExpressions;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HawalaExchange.PerformanceTests;

public sealed class TenantActivityDetailChartTests
{
    [Fact]
    public async Task Breakdown_and_daily_charts_show_received_sent_cash_exchange_and_bounded_date_pages()
    {
        var start = new DateTime(2026,10,1);
        PlatformActivityType[] types = [new("HawalaReceive","حواله دریافتی",4),new("HawalaSend","حواله ارسالی",2),new("Deposit","رسید",1),new("Withdraw","برد",1),new("Exchange","تبادله اسعار",2)];
        var days = Enumerable.Range(0, 366).Select(i => new PlatformDailyActivity(start.AddDays(i),10,6,0,1) {Types=types}).ToArray();
        var report = new PlatformActivityReport([],[],days) {Types=types};
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Probe? component=null;
            var root=await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { [nameof(Probe.Report)]=report,[nameof(Probe.Capture)]=(Action<Probe>)(x=>component=x) }));
            string Html()=>WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("حواله دریافتی",Html()); Assert.Contains("حواله ارسالی",Html());
            Assert.Contains("رسید",Html());Assert.Contains("برد",Html());Assert.Contains("تبادله اسعار",Html());
            Assert.Equal(14,Regex.Matches(Html(),"data-daily-column=").Count);
            Assert.Equal(42,Regex.Matches(Html(),"data-daily-bar=").Count);
            component!.Type("Deposit");
            Assert.Equal(14,Regex.Matches(Html(),"data-daily-bar=").Count);
            Assert.Contains("رسید",Html());
            component.Type("invalid"); Assert.Equal(14,Regex.Matches(Html(),"data-daily-bar=").Count);
            component.Page(999);
            Assert.Equal(2,Regex.Matches(Html(),"data-daily-column=").Count);
            Assert.DoesNotContain("NaN",Html());Assert.DoesNotContain("Infinity",Html());
            component.Type("Overview");Assert.Equal(42,Regex.Matches(Html(),"data-daily-bar=").Count);
        });
    }
    public sealed class Probe:TenantActivityDetailCharts
    {
        [Parameter]public Action<Probe> Capture {get;set;}=default!;
        protected override void OnInitialized()=>Capture(this);
        public void Type(string code){SelectType(code);StateHasChanged();}
        public void Page(int page){ChangePage(page);StateHasChanged();}
    }
}
