using System.Text;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HawalaExchange.PerformanceTests;

public sealed class ActivityVisualReviewTests
{
    [Fact]
    public async Task Export_actual_components_with_synthetic_data_for_browser_visual_review()
    {
        var root=Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        var output=Path.Combine(root,"HawalaExchange.Web","obj","activity-ui-review-20261019");
        Directory.CreateDirectory(Path.Combine(output,"fonts"));
        var web=Path.Combine(root,"HawalaExchange.Web");
        File.Copy(Path.Combine(web,"wwwroot/fonts/Vazirmatn-Regular.ttf"),Path.Combine(output,"fonts/Vazirmatn-Regular.ttf"),true);
        var css=File.ReadAllText(Path.Combine(web,"wwwroot/lib/bootstrap/dist/css/bootstrap.rtl.min.css"))+
            File.ReadAllText(Path.Combine(web,"wwwroot/styles/pages/Platform/PlatformWorkspace.css")).Replace("../../../fonts/","fonts/")+
            File.ReadAllText(Path.Combine(web,"Components/Shared/TenantActivityChart.razor.css"))+
            File.ReadAllText(Path.Combine(web,"Components/Shared/TenantActivityDetailCharts.razor.css"));
        using var provider=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var names=new[]{"صرافی آزمایشی کابل","صرافی آزمایشی کویته","صرافی آزمایشی هرات","صرافی آزمایشی مزار","صرافی آزمایشی قندهار","صرافی آزمایشی بلخ","صرافی آزمایشی غزنی","صرافی آزمایشی بامیان","صرافی آزمایشی کندز","صرافی آزمایشی تخار"};
            var rows=Enumerable.Range(1,1000).Select(i=>new PlatformTenantActivity(i,i<=10?names[i-1]:$"صرافی آزمایشی {i}",false,12000/i,7000/i,2000/i,20,null)).ToArray();
            var chart=await renderer.RenderComponentAsync<TenantActivityChart>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { [nameof(TenantActivityChart.Rows)]=rows,[nameof(TenantActivityChart.TenantLink)]=(Func<long,string>)(_=>"tenant.html") }));
            PlatformActivityType[] types=[new("HawalaReceive","حواله دریافتی",4800),new("HawalaSend","حواله ارسالی",2600),new("Deposit","رسید",1700),new("Withdraw","برد",1400),new("Exchange","تبادله اسعار",800),new("Expense","مصارف",200),new("Transfer","انتقال",150),new("CapitalInvestment","ثبت سرمایه",20),new("OwnerWithdrawal","برداشت مالک",10),new("Adjustment","تعدیل حساب",5),new("OpeningBalance","مانده افتتاحیه",4),new("Other","سایر معاملات",30)];
            var report=new PlatformActivityReport([],[],[]) {Types=types};
            var detail=await renderer.RenderComponentAsync<TenantActivityDetailCharts>(ParameterView.FromDictionary(new Dictionary<string,object?>{[nameof(TenantActivityDetailCharts.Report)]=report}));
            string Document(string title,string body)=>$"<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>{title}</title><style>{css}</style><body><div class='app-shell'><main class='platform-workspace'><div class='platform-content'><p class='text-muted'>بررسی ظاهری با داده‌های کاملاً آزمایشی</p><h1 class='page-title'>{title}</h1><section class='modern-card p-4'>{body}</section></div></main></div></body></html>";
            await File.WriteAllTextAsync(Path.Combine(output,"index.html"),Document("میزان فعالیت صرافی‌ها",chart.ToHtmlString()),Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(output,"tenant.html"),Document("گزارش صرافی آزمایشی کابل","<section class='modern-card p-3 mb-3' aria-label='خلاصه صرافی'>صرافی آزمایشی کابل · فعال · پلن آزمایشی · 20 کاربر · 3 شعبه</section>"+detail.ToHtmlString()),Encoding.UTF8);
            Assert.Contains("activity-ranking-table",chart.ToHtmlString());
            Assert.Contains("type-bar",detail.ToHtmlString());
        });
    }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
