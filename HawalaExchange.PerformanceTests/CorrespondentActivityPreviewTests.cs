using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Moq;
using Microsoft.EntityFrameworkCore;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class CorrespondentActivityPreviewTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Correspondent_report_keeps_pending_commission_out_then_recognizes_it_on_period_close()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var owner = new Correspondent { Code = "CORR-RPT-" + Guid.NewGuid().ToString("N"), Name = "نمایندگی آزمایشی" };
        context.Correspondents.Add(owner); await context.SaveChangesAsync();
        var account = new Account { AccountCode = owner.Code, AccountName = owner.Name, CorrespondentId = owner.Id, AccountType = "Correspondent" };
        var commission = new Transaction { TransactionNo = "RPT-" + Guid.NewGuid().ToString("N"), TransactionType = "PeriodicCorrespondentCommission", BranchId = 1, CreatedBy = fixture.UserId, Status = "Paid" };
        context.AddRange(account, commission); await context.SaveChangesAsync();
        var day = new DateTime(2048, 1, 10);
        var pendingEntry = Entry(account.Id, day.AddDays(-1), 2, 0, 50); pendingEntry.TransactionId = commission.Id;
        context.LedgerEntries.AddRange(Entry(account.Id, day.AddDays(-1), 2, 1000, 0), Entry(account.Id, day, 2, 500, 0),
            Entry(account.Id, day.AddHours(23), 2, 0, 200), Entry(account.Id, day.AddDays(1), 2, 9999, 0), pendingEntry,
            Entry(account.Id, day.AddDays(-1), 1, 0, 3000));
        await context.SaveChangesAsync();
        var service = new ExportService(context, Mock.Of<IConverter>());
        var filter = new ExportFilterDto { CorrespondentId = owner.Id, FromDate = day.ToUniversalTime(), ToDate = day.AddDays(1).ToUniversalTime().AddTicks(-1), CurrencyCode = "USD", Direction = "Debit" };
        var report = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Equal("نمایندگی", report.OwnerType); Assert.Equal(owner.Id, report.CorrespondentId);
        Assert.Equal(1300, Assert.Single(report.Rows).RunningBalance);
        var usd = Assert.Single(report.Balances); Assert.Equal(1000, usd.Opening); Assert.Equal(500, usd.Credit); Assert.Equal(200, usd.Debit); Assert.Equal(1300, usd.Closing); Assert.Equal(50, usd.PendingDebit);
        var file = await service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Excel);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal("صورت حساب نمایندگی", workbook.Worksheet(1).Name);
        Assert.Equal(1000m, workbook.Worksheet(1).Cell(4, 7).GetValue<decimal>());
        Assert.Equal(1300m, workbook.Worksheet(1).Cell(7, 7).GetValue<decimal>());
        Assert.Equal(50m, workbook.Worksheet(1).Cell(8, 4).GetValue<decimal>());
        filter.CurrencyCode = "";
        var multi = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Equal(-3000, Assert.Single(multi.Balances, x => x.Currency == "AFN").Closing);
        var closed = day.AddDays(2);
        var period = new CorrespondentAccountPeriod { CorrespondentId = owner.Id, PeriodNumber = 1, PeriodFrom = day.AddDays(-2).ToUniversalTime(), PeriodTo = closed.ToUniversalTime(), ClosedAt = closed.ToUniversalTime(), ClosedBy = fixture.UserId };
        context.CorrespondentAccountPeriods.Add(period); await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.CorrespondentCommissionRecognitions (TenantId, AccountId, TransactionId, PeriodId) VALUES ({context.CurrentTenantId}, {account.Id}, {commission.Id}, {period.Id})");
        filter.CurrencyCode = "USD"; filter.FromDate = closed.ToUniversalTime(); filter.ToDate = closed.AddDays(1).ToUniversalTime().AddTicks(-1);
        var recognized = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Equal(closed.ToUniversalTime(), Assert.Single(recognized.Rows).Date);
        var summary = Assert.Single(recognized.Balances); Assert.Equal(11299, summary.Opening); Assert.Equal(11249, summary.Closing); Assert.Equal(0, summary.PendingDebit);
        using (context.UseTenantScope(999999))
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.GetCustomerActivitiesPreviewAsync(filter));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Excel));
        }
    }

    [Fact]
    public async Task Correspondent_without_activity_exports_empty_report_and_rejects_mixed_owners()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var owner = new Correspondent { Code = "CORR-EMPTY-" + Guid.NewGuid().ToString("N"), Name = "نمایندگی <آزمایشی>" };
        context.Correspondents.Add(owner); await context.SaveChangesAsync();
        string html = "";
        var converter = new Mock<IConverter>();
        converter.Setup(x => x.Convert(It.IsAny<IDocument>())).Callback<IDocument>(doc => html = ((HtmlToPdfDocument)doc).Objects[0].HtmlContent).Returns("%PDF"u8.ToArray());
        var service = new ExportService(context, converter.Object);
        var filter = new ExportFilterDto { CorrespondentId = owner.Id };
        var report = await service.GetCustomerActivitiesPreviewAsync(filter);
        Assert.Empty(report.Rows); Assert.Empty(report.Balances);
        await service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Pdf);
        Assert.Contains("صورت حساب نمایندگی", html); Assert.Contains("&lt;آزمایشی&gt;", html);
        filter.CustomerId = 1;
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetCustomerActivitiesPreviewAsync(filter));
    }

    private static LedgerEntry Entry(long account, DateTime local, long currency, decimal credit, decimal debit) =>
        new() { AccountId = account, CurrencyId = currency, CreatedAt = local.ToUniversalTime(), TalabKar = credit, BadehKar = debit, Description = "Correspondent activity test" };
}
