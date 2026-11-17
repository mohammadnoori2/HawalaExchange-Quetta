using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HawalaExchange.NavigationTests;

public class CurrencyBadgeTests
{
    [Theory]
    [InlineData(" usd ","USD",0)]
    [InlineData("AFN","AFN",1)]
    [InlineData("PKR","PKR",2)]
    public async Task Known_currencies_have_a_stable_readable_label_and_distinct_tone(string code,string label,int tone)
    {
        var html=await Render(code);
        Assert.Contains($"data-currency=\"{label}\"",html);
        Assert.Contains($"currency-tone-{tone}",html);
        Assert.Contains($">{label}</bdi>",html);
    }
    [Fact]
    public async Task Missing_currency_is_empty_and_custom_currency_is_html_encoded()
    {
        Assert.Equal(string.Empty,(await Render(null)).Trim());
        var html=await Render("<img>");
        Assert.DoesNotContain("<IMG>",html);
        Assert.Contains("&lt;IMG&gt;",html);
        Assert.Equal(html,await Render("<img>"));
    }
    private static async Task<string> Render(string? code)
    {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async()=>
            (await renderer.RenderComponentAsync<CurrencyBadge>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Code",code}}))).ToHtmlString());
    }
}
