using System.Net;
using System.Text.RegularExpressions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public class CompactReceiptTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Receipts_keep_identifiers_on_one_row_hide_sender_agency_and_never_claim_an_unpaid_hawala_was_paid(bool executed, bool showAgency)
    {
        var display = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 0 });
        using var provider = new ServiceCollection().AddLogging().AddSingleton(display).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var receipt = await renderer.RenderComponentAsync<HawalaReceipt>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Hawala"] = new HawalaDto { Number = 2172, ReferenceNumber = "A44559", HawalaType = "HawalaReceive", ReceiverName = "Receiver", CreatedAt = new DateTime(2026, 10, 31, 5, 2, 0, DateTimeKind.Utc),
                    ReceiverPhone = "0700123456", CorrespondentName = "Quetta Agency", SenderName = "<script>Sender</script>", ToAmount = 21560, ToCurrencyCode = "AFN" },
                ["Company"] = new CompanySettingDto { CompanyName = "Exchange", Address = "Kabul" }, ["ExecutionMode"] = executed, ["ShowSendingCorrespondent"] = showAgency
            }));
            var html = receipt.ToHtmlString(); var text = WebUtility.HtmlDecode(html);
            var row = Regex.Match(html, "<tr class=\"receipt-identifiers\">[\\s\\S]*?</tr>").Value;
            Assert.Contains("2172", row); Assert.Contains("A44559", row);
            Assert.Contains("نمبر مسلسل/نمبر حواله", WebUtility.HtmlDecode(row));
            Assert.DoesNotContain("مشخصات حواله", text); Assert.Equal(showAgency, text.Contains("Quetta Agency"));
            Assert.Contains("0700123456", text); Assert.Contains("21,560", text); Assert.Contains("امضاء یا اثر انگشت گیرنده", text);
            Assert.Contains("AFN", text);
            Assert.Equal(executed, text.Contains("دریافت کردم"));
            Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("واتساپ", text);
            var previewDirectory = Environment.GetEnvironmentVariable("HAWALA_RECEIPT_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(previewDirectory) && !showAgency)
            {
                Directory.CreateDirectory(previewDirectory);
                File.WriteAllText(Path.Combine(previewDirectory, executed ? "executed.article.html" : "pending.article.html"), html);
            }
        });
    }
}
