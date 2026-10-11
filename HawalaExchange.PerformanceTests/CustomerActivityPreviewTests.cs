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
public sealed class CustomerActivityPreviewTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Preview_reconciles_each_currency_and_filters_details_without_changing_balances()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var customer = new Customer { CustomerCode = "PREVIEW-" + Guid.NewGuid().ToString("N"), FullName = "مشتری آزمایشی" };
        context.Customers.Add(customer); await context.SaveChangesAsync();
        var account = new Account { AccountCode = customer.CustomerCode, AccountName = customer.FullName, CustomerId = customer.Id, AccountType = "Customer" };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        var day = new DateTime(2047, 6, 10).ToUniversalTime();
        context.LedgerEntries.AddRange(
            Entry(account.Id, day.AddTicks(-1), 2, 1000, 0), Entry(account.Id, day, 2, 500, 0),
            Entry(account.Id, day.AddDays(1).AddTicks(-1), 2, 0, 200), Entry(account.Id, day.AddDays(1), 2, 9999, 0),
            Entry(account.Id, day.AddDays(-1), 1, 0, 3000), Entry(account.Id, day.AddHours(1), 1, 1000, 0));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var service = new ExportService(context, Mock.Of<IConverter>());
        var filter = new ExportFilterDto { CustomerId = customer.Id, FromDate = day, ToDate = day.AddDays(1).AddTicks(-1) };
        var all = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Equal(3, all.Rows.Count);
        var usd = Assert.Single(all.Balances, x => x.Currency == "USD");
        Assert.Equal(1000, usd.Opening); Assert.Equal(500, usd.Credit); Assert.Equal(200, usd.Debit); Assert.Equal(1300, usd.Closing);
        var afn = Assert.Single(all.Balances, x => x.Currency == "AFN");
        Assert.Equal(-3000, afn.Opening); Assert.Equal(-2000, afn.Closing);
        filter.CurrencyCode = "USD"; filter.Direction = "Debit"; filter.Search = "Preview";
        var selected = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Equal(1300, Assert.Single(selected.Rows).RunningBalance);
        Assert.Equal(500, Assert.Single(selected.Balances).Credit);
        Assert.Equal(1300, selected.Balances[0].Closing);
        context.LedgerEntries.Add(Entry(account.Id, day.AddHours(2), 2, 999, 0)); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var snapshotFile = await service.ExportCustomerActivitiesPreviewAsync(selected, ExportFormat.Excel);
        using var snapshotBook = new XLWorkbook(new MemoryStream(snapshotFile.Content));
        Assert.Equal(1300m, snapshotBook.Worksheet(1).Cell(7, 7).GetValue<decimal>());
        using (context.UseTenantScope(999999)) await Assert.ThrowsAsync<ArgumentException>(() => service.GetCustomerActivitiesPreviewAsync(filter));
        using (context.UseTenantScope(999999)) await Assert.ThrowsAsync<ArgumentException>(() => service.ExportCustomerActivitiesPreviewAsync(selected, ExportFormat.Excel));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Empty_period_keeps_opening_and_closing_in_preview_excel_and_pdf()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var customer = new Customer { CustomerCode = "EMPTY-" + Guid.NewGuid().ToString("N"), FullName = "مشتری <آزمایشی>" };
        context.Customers.Add(customer); await context.SaveChangesAsync();
        var account = new Account { AccountCode = customer.CustomerCode, AccountName = customer.FullName, CustomerId = customer.Id, AccountType = "Customer" };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        var day = new DateTime(2047, 7, 1).ToUniversalTime();
        context.LedgerEntries.Add(Entry(account.Id, day.AddDays(-1), 2, 21560, 0)); await context.SaveChangesAsync();
        string html = "";
        var converter = new Mock<IConverter>();
        converter.Setup(x => x.Convert(It.IsAny<IDocument>())).Callback<IDocument>(doc => html = ((HtmlToPdfDocument)doc).Objects[0].HtmlContent).Returns("%PDF"u8.ToArray());
        var service = new ExportService(context, converter.Object);
        var filter = new ExportFilterDto { CustomerId = customer.Id, FromDate = day, ToDate = day.AddDays(1).AddTicks(-1) };
        var report = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Empty(report.Rows); Assert.Equal(21560, Assert.Single(report.Balances).Opening); Assert.Equal(21560, report.Balances[0].Closing);
        var excel = await service.ExportCustomerActivitiesAsync(filter, ExportFormat.Excel);
        using var workbook = new XLWorkbook(new MemoryStream(excel.Content));
        Assert.Equal(21560m, workbook.Worksheet(1).Cell(4, 7).GetValue<decimal>());
        Assert.Equal(21560m, workbook.Worksheet(1).Cell(7, 7).GetValue<decimal>());
        await service.ExportCustomerActivitiesAsync(filter, ExportFormat.Pdf);
        Assert.Contains("مانده قبل از شروع بازه", html); Assert.Contains("21,560", html); Assert.Contains("جمع بازه و مانده پایان USD", html);
        Assert.Contains("&lt;آزمایشی&gt;", html);
        filter.FromDate = day.AddDays(2);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetCustomerActivitiesPreviewAsync(filter));
    }

    [Fact]
    public async Task Native_pdf_generates_report_with_opening_balance()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var customer = new Customer { CustomerCode = "PDF-" + Guid.NewGuid().ToString("N"), FullName = "مشتری آزمایشی" };
        context.Customers.Add(customer); await context.SaveChangesAsync();
        var account = new Account { AccountCode = customer.CustomerCode, AccountName = customer.FullName, CustomerId = customer.Id, AccountType = "Customer" };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        var day = new DateTime(2026, 10, 10).ToUniversalTime();
        context.LedgerEntries.AddRange(Entry(account.Id, day.AddDays(-1), 2, 21560, 0), Entry(account.Id, day, 2, 0, 500)); await context.SaveChangesAsync();
        var service = new ExportService(context, new SynchronizedConverter(new PdfTools()));
        var file = await service.ExportCustomerActivitiesAsync(new() { CustomerId = customer.Id, FromDate = day, ToDate = day.AddDays(1).AddTicks(-1) }, ExportFormat.Pdf);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(file.Content, 0, 4));
        var output = Environment.GetEnvironmentVariable("HAWALA_CUSTOMER_PDF_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) await File.WriteAllBytesAsync(output, file.Content);
    }

    private static LedgerEntry Entry(long account, DateTime utc, long currency, decimal credit, decimal debit) =>
        new() { AccountId = account, CurrencyId = currency, CreatedAt = utc, TalabKar = credit, BadehKar = debit, Description = "Preview test" };
}
