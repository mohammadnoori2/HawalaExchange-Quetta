using System.Net;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.PerformanceTests;

public sealed class RecordDetailsLinkTests
{
    [Theory]
    [InlineData("Account", "/accounts/17")]
    [InlineData("Customer", "/customers/17")]
    [InlineData("Correspondent", "/correspondents/17")]
    [InlineData("PaymentLocation", "/payment-locations/17")]
    [InlineData("Branch", "/branches/17")]
    [InlineData("Currency", "/currencies/17")]
    public async Task Registered_names_open_their_view_route(string kind,string route)
    {
        var html=await Render(kind,17,"نام آزمایشی");
        Assert.Contains($"href=\"{route}\"",html);
        Assert.Contains("نام آزمایشی",WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("edit",html,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Missing_ids_remain_text_and_do_not_guess_a_destination(long? id)
    {
        Assert.Null(RecordDetailsRoutes.Get("Account",id));
        Assert.DoesNotContain("href=",await Render("Account",id,"دفتر خود ما"));
    }

    [Fact]
    public async Task Duplicate_names_still_link_to_distinct_ids_and_html_is_encoded()
    {
        var first=await Render("Customer",7,"<script>نام مشترک</script>");
        var next=await Render("Customer",8,"<script>نام مشترک</script>");
        Assert.Contains("href=\"/customers/7\"",first);Assert.Contains("href=\"/customers/8\"",next);
        Assert.DoesNotContain("<script>",first);Assert.Contains("&lt;script&gt;",first);
        Assert.Null(RecordDetailsRoutes.Get("https://example.com/",7));
    }

    private static async Task<string> Render(string kind,long? id,string text)
    {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var component=await renderer.RenderComponentAsync<RecordDetailsLink>(ParameterView.FromDictionary(new Dictionary<string,object?>
                {{nameof(RecordDetailsLink.Kind),kind},{nameof(RecordDetailsLink.Id),id},{nameof(RecordDetailsLink.Text),text}}));
            return component.ToHtmlString();
        });
    }

    [Fact]
    public void New_reference_detail_routes_are_authorized_and_only_read_data()
    {
        var source=ReadReferencePage();
        Assert.Contains("@attribute [Authorize]",source);
        foreach(var route in new[]{"payment-locations","branches","currencies"})
            Assert.Contains($"@page \"/{route}/{{Id:long}}\"",source);
        Assert.DoesNotContain("<input",source);Assert.DoesNotContain("<EditForm",source);
        Assert.DoesNotContain(".UpdateAsync(",source);Assert.DoesNotContain(".DeleteAsync(",source);
        Assert.Contains("GetByIdAsync(id)",source);
    }

    private static string ReadReferencePage([System.Runtime.CompilerServices.CallerFilePath]string source="")
    {
        var root=Directory.GetParent(Path.GetDirectoryName(source)!)!.FullName;
        return File.ReadAllText(Path.Combine(root,"HawalaExchange.Web","Components","Pages","ReferenceDetails.razor"));
    }
}
