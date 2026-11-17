using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HawalaExchange.NavigationTests;

public class DashboardCounterpartyTableTests
{
    [Fact]
    public async Task Accounts_appear_once_with_original_amounts_in_separate_currency_columns()
    {
        var groups=new List<DashboardCounterpartyCurrencyDto>{
            new(){CurrencyCode="USD",TopDebtors=[new(){AccountId=1,Name="Alpha",Amount=500}],AccountBalances=new(){{1,500},{2,15}},TotalReceivables=515},
            new(){CurrencyCode="AFN",TopDebtors=[new(){AccountId=1,Name="Alpha",Amount=9000},new(){AccountId=2,Name="Beta",Amount=8000}],
                AccountBalances=new(){{1,9000},{2,8000}},TotalReceivables=17000},new(){CurrencyCode="PKR"}};
        var html=await Render(groups,false);
        Assert.Equal(1,html.Split("<table ").Length-1);
        Assert.Equal(1,html.Split(">Alpha</a>").Length-1);
        Assert.Contains("data-currency=\"USD\"",html);Assert.Contains("data-currency=\"AFN\"",html);
        Assert.DoesNotContain("data-currency=\"PKR\"",html);
        Assert.Contains(">15</td>",html);Assert.Contains(">9,000</td>",html);Assert.Contains(">17,000</td>",html);
    }
    [Fact]
    public async Task Creditor_columns_show_credit_balances_and_do_not_mix_opposite_debts()
    {
        var groups=new List<DashboardCounterpartyCurrencyDto>{
            new(){CurrencyCode="USD",TopCreditors=[new(){AccountId=1,Name="Alpha",Amount=25}],AccountBalances=new(){{1,-25}},TotalPayables=25},
            new(){CurrencyCode="AFN",AccountBalances=new(){{1,9000}},TotalReceivables=9000}};
        var html=await Render(groups,true);
        Assert.Contains(">25</td>",html);Assert.DoesNotContain("data-currency=\"AFN\"",html);Assert.DoesNotContain(">9,000</td>",html);
    }
    private static async Task<string> Render(List<DashboardCounterpartyCurrencyDto> groups,bool creditors)
    {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async()=>(await renderer.RenderComponentAsync<DashboardCounterpartyTable>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"Groups",groups},{"Creditors",creditors}}))).ToHtmlString()));
    }
}
