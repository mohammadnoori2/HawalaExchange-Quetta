using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HawalaExchange.NavigationTests;

public class HawalaIndependentAmountTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_shows_stored_destination_amount_only_for_active_conversion(bool active)
    {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var dto=new HawalaDto { Id=6641,FromAmount=28000,FromCurrencyCode="USD",HasIndependentExchange=active,
                IndependentExchangeAmount=1864800,IndependentExchangeCurrency="AFN" };
            var view=await renderer.RenderComponentAsync<HawalaIndependentAmount>(ParameterView.FromDictionary(new Dictionary<string,object?> { ["Hawala"]=dto }));
            var html=view.ToHtmlString();
            if(active) { Assert.Contains("1,864,800",html);Assert.Contains("AFN",html);Assert.DoesNotContain("28,000",html); }
            else Assert.DoesNotContain("1,864,800",html);
            Assert.Equal(28000,dto.FromAmount);
        });
    }
}
