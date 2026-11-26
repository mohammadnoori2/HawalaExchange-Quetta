using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Pages;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public class DashboardReferenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dashboard_keeps_real_balances_actions_and_reports_in_both_empty_and_populated_states(bool empty)
    {
        var collection=new ServiceCollection().AddLogging();
        var injected=typeof(Home).Assembly.GetTypes().Where(t=>typeof(IComponent).IsAssignableFrom(t))
            .SelectMany(t=>t.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            .Where(p=>p.GetCustomAttribute<InjectAttribute>() is not null&&p.PropertyType.IsInterface).Select(p=>p.PropertyType).Distinct();
        foreach(var type in injected) { var mock=(Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;collection.AddSingleton(type,mock.Object); }
        var currencies=new Mock<ICurrencyService>();
        var currencyList=new List<CurrencyDto>{new(){Id=1,Code="AFN",IsActive=true},new(){Id=2,Code="USD",IsActive=true}};
        currencies.Setup(x=>x.GetAllAsync()).ReturnsAsync(currencyList);
        var company=new Mock<ICompanySettingService>();company.Setup(x=>x.GetAsync()).ReturnsAsync(new CompanySettingDto{DefaultProfitCurrencyId=2});
        var financial=new Mock<IFinancialReportService>();
        var debtors=new List<DashboardDebtorDto>{new(){AccountId=11,Name="نماینده: کویته",AccountTypeName="نمایندگی"},new(){AccountId=12,Name="نماینده: عبدالله",AccountTypeName="نمایندگی"},new(){AccountId=13,Name="نماینده: سیستم بانک",AccountTypeName="نمایندگی"}};
        var summary=new DashboardSummaryDto{HawalaCount=empty?0:1988,ExchangeCount=empty?0:33,NetProfit=empty?0:-1023,TotalActivityCount=2067,AccountOperationCount=38,ExpenseCount=8,LastActivityAt=new DateTime(2026,11,24,8,28,0),
            CounterpartiesByCurrency=empty?[]:[new(){CurrencyCode="AFN",TotalReceivables=7995209,TopDebtors=debtors,AccountBalances=new(){{11,5897020},{12,1299950},{13,798239}}},new(){CurrencyCode="USD",TotalReceivables=3121487,TopDebtors=debtors.Take(2).ToList(),AccountBalances=new(){{11,2644724},{12,476763}}}]};
        financial.Setup(x=>x.GetDashboardSummaryAsync(It.IsAny<DateTime>(),It.IsAny<DateTime>(),2)).ReturnsAsync(summary);
        var journal=new Mock<IJournalService>();journal.Setup(x=>x.GetCurrentCashBalancesAsync()).ReturnsAsync(empty?Array.Empty<CurrentCashBalanceDto>():new[]{new CurrentCashBalanceDto{AccountId=1,AccountName="صندوق",CurrencyCode="AFN",Balance=4695940},new(){AccountId=1,AccountName="صندوق",CurrencyCode="USD",Balance=77771}});
        var alerts=new Mock<ICashBalanceAlertService>();alerts.Setup(x=>x.GetActiveAlertsAsync(It.IsAny<bool>(),It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CashBalanceAlertDto>());
        var permissions=new Mock<IUserPermissionService>();permissions.Setup(x=>x.Can(It.IsAny<string>())).Returns(true);
        var rates=new Mock<IExchangeRateService>();rates.Setup(x=>x.GetDashboardRatesAsync()).ReturnsAsync(new DashboardExchangeRatesDto{LocalNow=new DateTime(2026,11,24)});
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().Options;var tenant=Mock.Of<ICurrentTenant>();
        collection.AddSingleton(currencies.Object).AddSingleton(company.Object).AddSingleton(financial.Object).AddSingleton(journal.Object).AddSingleton(alerts.Object).AddSingleton(permissions.Object).AddSingleton(rates.Object)
            .AddSingleton(options).AddSingleton(new ApplicationDbContext(options,tenant)).AddSingleton(new AppDateDisplay(options,tenant))
            .AddSingleton(new UiReferenceDataCache(tenant,currencies.Object,Mock.Of<IPaymentLocationService>(),company.Object)).AddSingleton<NavigationManager>(new TestNavigation());
        using var services=collection.BuildServiceProvider();await using var renderer=new PreviewRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var view=renderer.BeginRenderingComponent(typeof(Home),ParameterView.Empty);await view.QuiescenceTask;var html=System.Net.WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("dashboard-reference",html);Assert.Contains("dailyChart",html);Assert.Contains("periodicChart",html);Assert.Contains("liquidityChart",html);Assert.Contains("miniChart",html);
            if(!empty){Assert.Contains("4,695,940",html);Assert.Contains("77,771",html);Assert.Contains("-1,023",html);Assert.Contains("5,897,020",html);}
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!Directory.Exists(Path.Combine(root.FullName,"HawalaExchange.Web")))root=root.Parent;
            var output=Path.Combine(root!.FullName,"HawalaExchange.Web","obj","dashboard-reference-qa");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,empty?"empty.html":"dashboard.html"),view.ToHtmlString());
        });
        financial.Verify(x=>x.GetDashboardSummaryAsync(It.IsAny<DateTime>(),It.IsAny<DateTime>(),2),Times.Once);
        journal.Verify(x=>x.GetCurrentCashBalancesAsync(),Times.Once);
    }
    private sealed class PreviewRenderer(IServiceProvider services,ILoggerFactory logger):StaticHtmlRenderer(services,logger)
    {
        protected override IComponent ResolveComponentForRenderMode(Type type,int? parentId,IComponentActivator activator,IComponentRenderMode mode)=>activator.CreateInstance(type);
    }
    private sealed class TestNavigation:NavigationManager
    {
        public TestNavigation()=>Initialize("http://localhost/","http://localhost/");
        protected override void NavigateToCore(string uri,bool forceLoad) { }
    }
}
