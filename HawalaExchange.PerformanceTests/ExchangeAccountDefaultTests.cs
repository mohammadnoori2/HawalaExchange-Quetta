using System.Net;
using System.Text.RegularExpressions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared.Modals;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Moq;
namespace HawalaExchange.PerformanceTests;
public sealed class ExchangeAccountDefaultTests
{
    [Theory]
    [InlineData("Correspondent",100,"CorrespondentCredit")]
    [InlineData("Correspondent",-100,"CorrespondentDebt")]
    [InlineData("Customer",100,"Customer")]
    [InlineData("Cash",100,"Treasury")]
    [InlineData("Bank",100,"Treasury")]
    public async Task Both_fields_default_to_the_current_account_without_falling_back_to_cash(string type,decimal balance,string mode)
    {
        var display=new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,new TestCurrentTenant {TenantId=0});
        var accounts=new Mock<IAccountService>();
        accounts.Setup(x=>x.GetAllAsync()).ReturnsAsync(new[] {new AccountDto {Id=555,AccountName="Current account",AccountCode="555",AccountType=type},new AccountDto {Id=777,AccountName="Wrong cash",AccountCode="777",AccountType="Cash"}});
        var currencies=new Mock<ICurrencyService>();
        currencies.Setup(x=>x.GetActiveCurrenciesAsync()).ReturnsAsync(new[] {new CurrencyDto {Id=1,Code="AFN",Name="AFN",IsActive=true},new CurrencyDto {Id=2,Code="USD",Name="USD",IsActive=true}});
        var settings=new Mock<ICompanySettingService>(); settings.Setup(x=>x.GetAsync()).ReturnsAsync(new CompanySettingDto {DefaultProfitCurrencyId=1});
        var balances=new Mock<IBalanceService>(); balances.Setup(x=>x.GetAccountBalanceAsync(555,null)).ReturnsAsync(new[] {new BalanceDto {CurrencyId=1,Balance=balance}});
        using var provider=new ServiceCollection().AddLogging().AddSingleton(display).AddSingleton(accounts.Object).AddSingleton(currencies.Object)
            .AddSingleton(settings.Object).AddSingleton(balances.Object).AddSingleton(Mock.Of<IMoneyExchangeOperationService>()).AddSingleton(Mock.Of<IHawalaService>()).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var component=await renderer.RenderComponentAsync<MoneyExchangeModal>(ParameterView.FromDictionary(new Dictionary<string,object?> {["IsVisible"]=true,["InitialAccountId"]=555L}));
            var html=component.ToHtmlString();
            Assert.Equal(2,Regex.Matches(html,"data-account-value=\"555\"").Count);
            if(type=="Correspondent")
            {
                var text=WebUtility.HtmlDecode(html);
                Assert.DoesNotContain("Wrong cash",text);
                Assert.DoesNotContain("مالک پول",text);
                Assert.DoesNotContain("گزینه خرید و فروش",text);
                Assert.Contains(balance<0 ? "بدهکاری ارز فروش" : "طلبکاری ارز فروش",text);
                Assert.Contains("برعکس‌کردن جهت ثبت",text);
            }
            else
            {
                Assert.Contains($"value=\"{mode}\"",html);
                Assert.Contains("مالک پول",WebUtility.HtmlDecode(html));
            }
        });
    }
}
