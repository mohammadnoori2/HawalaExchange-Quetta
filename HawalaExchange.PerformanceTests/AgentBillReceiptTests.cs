using System.Net;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Security;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
namespace HawalaExchange.PerformanceTests;
public sealed class AgentBillReceiptTests
{
    [Fact]
    public async Task Thermal_receipt_keeps_all_hawalas_and_separate_currency_totals_and_escapes_user_text()
    {
        using var provider = new ServiceCollection().AddLogging().AddSingleton(new AppDateDisplay(
            new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 0 })).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<AgentBillReceipt>(ParameterView.FromDictionary(new Dictionary<string, object?> {
                ["Bill"] = new AgentBillDto { Id = 42, Status = "Paid", RecipientName = "<script>Agent</script>", CorrespondentName = "Misc Kabul", CreatedAt = DateTime.UtcNow,
                    Items = Enumerable.Range(1, 120).Select(i => new HawalaPaymentBatchItemDto { Number = i, ReceiverName = "Receiver-" + i, Amount = 100, CurrencyCode = "USD", AgentCommission = 8, CommissionCurrencyCode = "AFN" }).ToList(),
                    Totals = [new() { CurrencyCode = "USD", Principal = 12000 }, new() { CurrencyCode = "AFN", AgentCommission = 960 }] },
                ["Company"] = new CompanySettingDto { CompanyName = "Test exchange" } }));
            var html = component.ToHtmlString(); var text = WebUtility.HtmlDecode(html);
            Assert.Contains("hawala-receipt agent-bill-receipt", html);
            Assert.Contains("Receiver-120", text); Assert.Contains("12,000", text); Assert.Contains("960", text);
            Assert.Contains("AFN", text); Assert.Contains("USD", text); Assert.DoesNotContain("<script>", html);
        });
    }

    [Theory]
    [InlineData("Pending", "در انتظار پرداخت")]
    [InlineData("Paid", "پرداخت‌شده")]
    [InlineData("Historical", "رسید تسویه قبلی")]
    public async Task Bill_receipt_shows_separate_currency_totals_and_never_claims_an_unpaid_bill_is_paid(string state,string label)
    {
        var display=new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,new TestCurrentTenant { TenantId=0 });
        using var provider=new ServiceCollection().AddLogging().AddSingleton(display).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            var component=await renderer.RenderComponentAsync<AgentBillDetails>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Bill"]=new AgentBillDto { Id=42,Status=state,IsHistoricalReceipt=state=="Historical",PreviousSettlementReference="Old cash book 123",RecipientName="<script>Agent</script>",CorrespondentName="Misc Kabul",CreatedAt=DateTime.UtcNow,
                    Totals=[new() {CurrencyId=1,CurrencyCode="AFN",Principal=100000,AgentCommission=400},new() {CurrencyId=2,CurrencyCode="USD",Principal=200,AgentCommission=8}],
                    Items=[new() {Number=34,ReceiverName="Receiver",Amount=100000,CurrencyCode="AFN",AgentCommission=400,CommissionCurrencyCode="AFN"}] } }));
            var html=component.ToHtmlString(); var text=WebUtility.HtmlDecode(html);
            Assert.Contains(label,text); Assert.Contains("100,400",text); Assert.Contains("208",text);
            Assert.Contains("مجموع کمیشن عامل",text); Assert.Contains("AFN",text); Assert.Contains("USD",text);
            Assert.DoesNotContain("<script>",html); Assert.Contains("امضا یا اثر انگشت",text);
            if (state == "Historical") { Assert.Contains("بدون ثبت حسابداری جدید", text); Assert.Contains("Old cash book 123", text); Assert.DoesNotContain("مجموع قابل پرداخت", text); }
        });
    }
    [Theory]
    [InlineData("/correspondents/1/agent-bills")]
    [InlineData("/correspondents/1/agent-bills/42")]
    public void Bill_routes_require_execution_view_permission(string path) => Assert.Equal("HawalaExecution.View",PermissionCatalog.PagePermission(path));
}
