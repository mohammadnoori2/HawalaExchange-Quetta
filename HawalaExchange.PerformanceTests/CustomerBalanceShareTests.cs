using System.Globalization;
using System.Net;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Helpers;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HawalaExchange.PerformanceTests;

public sealed class CustomerBalanceShareTests
{
    [Fact]
    public void Balance_actions_stay_together_before_preview_and_customer_has_back_navigation()
    {
        var root=Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        var page=File.ReadAllText(Path.Combine(root,"HawalaExchange.Web/Components/Pages/Reports/CustomerActivitiesReport.razor"));
        var markup=page[..page.IndexOf("@code",StringComparison.Ordinal)];
        var previewIndex=markup.IndexOf("@if (preview != null)",StringComparison.Ordinal);
        var toolbar=markup[..previewIndex];
        Assert.Contains("@onclick=\"CopyCustomerBalanceAsync\"",toolbar);
        Assert.DoesNotContain("@onclick=\"CopyCustomerBalanceAsync\"",markup[previewIndex..]);
        Assert.Contains("برگشت به مشتری",toolbar[..toolbar.IndexOf("</Actions>",StringComparison.Ordinal)]);
        Assert.Contains("/customers/{selectedCustomerId}",toolbar);
        Assert.DoesNotContain("preview?.CustomerId == selectedCustomerId",page);
        Assert.Contains("CustomerBalanceShareText.Build(customerName, customerBalance)",page);
        Assert.Contains("hawalaTools.copyToClipboard",page);
        Assert.Contains("balanceCopyFallback",page);
        var component=File.ReadAllText(Path.Combine(root,"HawalaExchange.Web/Components/Shared/CustomerBalanceShare.razor"));
        Assert.DoesNotContain("<details class=\"w-100",component);
    }

    [Fact]
    public void Customer_report_shares_current_balances_only_under_export_permission()
    {
        var root=Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        var page=File.ReadAllText(Path.Combine(root,"HawalaExchange.Web/Components/Pages/Reports/CustomerActivitiesReport.razor"));
        Assert.Contains("@if (!IsAccountReport && !IsCorrespondentReport)",page);
        Assert.Contains("<PermissionAction Actions=\"Export\"><CustomerBalanceShare",page);
        Assert.Contains("Balances=\"customerBalance\"",page);
        Assert.Contains("CustomerName=\"@customerName\"",page);
        Assert.Contains("selectedCustomerId != customerId || IsAccountReport || IsCorrespondentReport",page);
        Assert.Contains("customerBalance = null; customerName = \"\"; ResetFilters();",page);
    }

    [Fact]
    public void Text_contains_only_identification_and_current_balances_with_English_whole_amounts()
    {
        var culture=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=new CultureInfo("fa-IR");
            var text=CustomerBalanceShareText.Build("احمد (C-1)",[
                new(){CurrencyCode="USD",Balance=-500.5m,PendingCommissionCredit=999999},
                new(){CurrencyCode="AFN",Balance=21560.4m,PendingCommissionDebit=888888},
                new(){CurrencyCode="PKR",Balance=0}]);
            Assert.Equal("مانده فعلی حساب مشتری\nمشتری: احمد (C-1)\nAFN: 21,560 (طلبکار)\nPKR: 0 (تسویه)\nUSD: 501 (بدهکار)",text.Replace("\r\n","\n"));
            Assert.DoesNotContain("999",text);Assert.DoesNotContain("888",text);
        }
        finally {CultureInfo.CurrentCulture=culture;}
    }

    [Theory]
    [InlineData(0.49,"0 (تسویه)")]
    [InlineData(-0.49,"0 (تسویه)")]
    [InlineData(0.50,"1 (طلبکار)")]
    [InlineData(-0.50,"1 (بدهکار)")]
    public void Display_rounding_does_not_label_zero_as_debt(decimal amount,string expected)
        => Assert.EndsWith(expected,CustomerBalanceShareText.Build("Test",[new(){CurrencyCode="USD",Balance=amount}]));

    [Fact]
    public void Missing_customer_or_balances_cannot_generate_a_share()
    {
        Assert.Empty(CustomerBalanceShareText.Build(null,[new(){CurrencyCode="USD",Balance=5}]));
        Assert.Empty(CustomerBalanceShareText.Build("Customer",null));
        Assert.Empty(CustomerBalanceShareText.Build("Customer",[]));
        Assert.Empty(CustomerBalanceShareText.Build("Customer",[new(){CurrencyCode=" ",Balance=5}]));
    }

    [Fact]
    public void Text_is_encoded_as_one_whatsapp_parameter_and_names_cannot_add_extra_lines()
    {
        var text=CustomerBalanceShareText.Build("Test\nName & + ? <script>",[new(){CurrencyCode="USD",Balance=25}]);
        var url=CustomerBalanceShareText.WhatsAppUrl(text);
        Assert.Equal(3,text.Split('\n').Length);
        Assert.StartsWith("https://wa.me/?text=",url);
        Assert.DoesNotContain("&",url);
        Assert.Equal(text,Uri.UnescapeDataString(url["https://wa.me/?text=".Length..]));
    }

    [Fact]
    public async Task Rendered_preview_matches_link_text_and_escapes_customer_names()
    {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<CustomerBalanceShare>(ParameterView.FromDictionary(new Dictionary<string,object?>
            {
                [nameof(CustomerBalanceShare.CustomerName)]="<script>customer</script>",
                [nameof(CustomerBalanceShare.Balances)]=new List<BalanceDto>{new(){CurrencyCode="USD",Balance=700}}
            }));
            var html=root.ToHtmlString();
            Assert.Contains("<details",html);Assert.Contains("<summary",html);Assert.Contains("https://wa.me/?text=",html);
            Assert.Contains("noopener noreferrer",html);Assert.Contains("no-referrer",html);
            Assert.DoesNotContain("<script>",html);
            Assert.Contains("&lt;script&gt;",html);
            Assert.Contains("700",WebUtility.HtmlDecode(html));
        });
    }

    [Fact]
    public async Task No_balance_renders_disabled_control_without_a_whatsapp_link()
    {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<CustomerBalanceShare>();
            Assert.Contains("disabled",root.ToHtmlString());
            Assert.DoesNotContain("wa.me",root.ToHtmlString());
        });
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
