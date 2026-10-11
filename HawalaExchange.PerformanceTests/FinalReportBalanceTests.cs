using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Moq;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class FinalReportBalanceTests(SqlServerPerformanceFixture fixture)
{
    [Theory]
    [InlineData("Customer", 100, "طلبکار", "#e6f7e6", "#1a7a3a")]
    [InlineData("Customer", -100, "بدهکار", "#fde8e8", "#dc3545")]
    [InlineData("Customer", 0, "تسویه", "#f2f2f2", "#495057")]
    [InlineData("Correspondent", 100, "طلبکار", "#e6f7e6", "#1a7a3a")]
    [InlineData("Correspondent", -100, "بدهکار", "#fde8e8", "#dc3545")]
    [InlineData("Correspondent", 0, "تسویه", "#f2f2f2", "#495057")]
    [InlineData("Cash", 100, "طلبکار", "#e6f7e6", "#1a7a3a")]
    [InlineData("Cash", -100, "بدهکار", "#fde8e8", "#dc3545")]
    [InlineData("Cash", 0, "تسویه", "#f2f2f2", "#495057")]
    public async Task Final_excel_and_pdf_status_has_correct_color_for_all_report_owners(string type, int amount, string status, string background, string foreground)
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var key = Guid.NewGuid().ToString("N");
        var customer = new Customer { CustomerCode = "FINAL-C-" + key, FullName = "مشتری آزمایشی" };
        var correspondent = new Correspondent { Code = "FINAL-R-" + key, Name = "نمایندگی آزمایشی" };
        context.AddRange(customer, correspondent); await context.SaveChangesAsync();
        var account = new Account { AccountCode = "FINAL-A-" + key, AccountName = "حساب آزمایشی", AccountType = type,
            CustomerId = type == "Customer" ? customer.Id : null, CorrespondentId = type == "Correspondent" ? correspondent.Id : null };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        var day = new DateTime(2051, 1, 1);
        context.LedgerEntries.Add(new LedgerEntry { AccountId = account.Id, CurrencyId = 2, CreatedAt = day.ToUniversalTime(),
            TalabKar = amount >= 0 ? Math.Max(amount, 100) : 0, BadehKar = amount < 0 ? -amount : 0, Description = "Final balance test" });
        if (amount == 0)
            context.LedgerEntries.Add(new LedgerEntry { AccountId = account.Id, CurrencyId = 2, CreatedAt = day.AddMinutes(1).ToUniversalTime(),
                BadehKar = 100, Description = "Settled balance test" });
        await context.SaveChangesAsync();
        string html = "";
        var converter = new Mock<IConverter>();
        converter.Setup(x => x.Convert(It.IsAny<IDocument>())).Callback<IDocument>(doc => html = ((HtmlToPdfDocument)doc).Objects[0].HtmlContent).Returns("%PDF"u8.ToArray());
        var service = new ExportService(context, converter.Object);
        var filter = new ExportFilterDto { CustomerId = type == "Customer" ? customer.Id : null, CorrespondentId = type == "Correspondent" ? correspondent.Id : null,
            AccountId = type == "Cash" ? account.Id : null, FromDate = day.ToUniversalTime(), ToDate = day.AddDays(1).ToUniversalTime().AddTicks(-1) };
        var report = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Equal(amount, Assert.Single(report.Balances).Closing);
        var excel = await service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Excel);
        using var workbook = new XLWorkbook(new MemoryStream(excel.Content));
        var sheet = workbook.Worksheet(1); var finalRow = sheet.LastRowUsed()!.RowNumber();
        Assert.Equal("وضعیت نهایی حساب - USD", sheet.Cell(finalRow, 1).GetString());
        Assert.Equal(Math.Abs(amount), sheet.Cell(finalRow, 7).GetValue<int>());
        Assert.Equal(status, sheet.Cell(finalRow, 8).GetString());
        Assert.Equal(XLColor.FromHtml(background), sheet.Cell(finalRow, 8).Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml(foreground), sheet.Cell(finalRow, 8).Style.Font.FontColor);
        Assert.True(sheet.Cell(finalRow, 8).Style.Font.Bold);
        await service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Pdf);
        var finalHtml = html[html.LastIndexOf("<h3>وضعیت نهایی حساب</h3>", StringComparison.Ordinal)..];
        Assert.Contains(status, finalHtml); Assert.Contains(background, finalHtml); Assert.Contains(foreground, finalHtml); Assert.Contains("USD", finalHtml);
        using (context.UseTenantScope(999999)) await Assert.ThrowsAsync<ArgumentException>(() => service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Excel));
    }
}
