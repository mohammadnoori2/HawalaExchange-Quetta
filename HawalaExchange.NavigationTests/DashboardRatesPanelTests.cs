using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public class DashboardRatesPanelTests
{
    [Theory]
    [InlineData(false,true,true)]
    [InlineData(true,true,false)]
    [InlineData(false,false,false)]
    public async Task Add_today_is_shown_only_for_missing_today_and_authorized_users(bool current,bool create,bool showAdd)
    {
        var rates=new Mock<IExchangeRateService>();var permissions=Permissions(create);
        var now=new DateTime(2026,11,17,10,30,0);
        rates.Setup(x=>x.GetDashboardRatesAsync()).ReturnsAsync(new DashboardExchangeRatesDto{LocalNow=now,
            Rates=[new(){FromCurrencyId=1,ToCurrencyId=2,FromCurrencyCode="AFN",ToCurrencyCode="USD",BuyRate=65,SellRate=66,EffectiveDate=current?now:now.AddDays(-1)}]});
        using var services=new ServiceCollection().AddLogging().AddSingleton(rates.Object).AddSingleton(permissions.Object)
            .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,Mock.Of<ICurrentTenant>())).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        var html=System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async()=>
            (await renderer.RenderComponentAsync<DashboardRatesPanel>()).ToHtmlString()));
        Assert.Equal(showAdd,html.Contains("افزودن نرخ امروز"));
        Assert.Contains(current?"نرخ امروز</small>":"آخرین نرخ؛ نرخ امروز ثبت نشده",html);
        Assert.Contains(current?"1405/08/26":"1405/08/25",html);
    }
    [Fact]
    public async Task Opening_prefills_today_and_pair_then_save_refreshes_rates_and_parent()
    {
        var rates=new Mock<IExchangeRateService>();var permissions=Permissions(true);
        var now=new DateTime(2026,11,17,10,30,0);var data=new DashboardExchangeRatesDto{LocalNow=now};
        rates.Setup(x=>x.GetDashboardRatesAsync()).ReturnsAsync(()=>data);
        var panel=new DashboardRatesPanel{Currencies=[new(){Id=1,Code="AFN"},new(){Id=2,Code="USD"}]};
        Set(panel,"RatesService",rates.Object);Set(panel,"Permissions",permissions.Object);
        var parentRefresh=0;panel.OnSaved=EventCallback.Factory.Create(new object(),()=>parentRefresh++);
        await Invoke(panel,"OpenToday",new ExchangeRateDto{FromCurrencyId=1,ToCurrencyId=2});
        var request=(CreateExchangeRateDto)typeof(DashboardRatesPanel).GetField("request",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel)!;
        Assert.Equal(now,request.EffectiveDate);Assert.Equal(1,request.FromCurrencyId);Assert.Equal(2,request.ToCurrencyId);
        await Invoke(panel,"Save");rates.Verify(x=>x.CreateAsync(It.IsAny<CreateExchangeRateDto>()),Times.Never);
        request.BuyRate=65;request.SellRate=66;
        rates.Setup(x=>x.CreateAsync(It.IsAny<CreateExchangeRateDto>())).ReturnsAsync(new ExchangeRateDto()).Callback(()=>data=new(){LocalNow=now,Rates=[new(){EffectiveDate=now}]});
        await Invoke(panel,"Save");
        rates.Verify(x=>x.CreateAsync(request),Times.Once);Assert.Equal(1,parentRefresh);
        Assert.False((bool)typeof(DashboardRatesPanel).GetField("showModal",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel)!);
        Assert.Single(((DashboardExchangeRatesDto)typeof(DashboardRatesPanel).GetField("data",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel)!).Rates);
    }
    private static Mock<IUserPermissionService> Permissions(bool create)
    {
        var mock=new Mock<IUserPermissionService>();mock.Setup(x=>x.RefreshAsync(It.IsAny<bool>(),It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(x=>x.Can(It.IsAny<string>())).Returns((string code)=>code=="Rates.View" || code=="Rates.Create"&&create);return mock;
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(target,value);
    private static Task Invoke(object target,string name,params object[] args)=>(Task)target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args)!;
}
