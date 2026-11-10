using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class AgentCommissionReportTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Commission_date_includes_old_hawalas_added_in_the_selected_day_and_registration_basis_is_optional()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        await using var tx = await db.Database.BeginTransactionAsync();
        var day = new DateTime(2051, 2, 10);
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById("Asia/Kabul"));
        var number = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Hawala Row(DateTime created) => new() { Number = number++, HawalaType = "HawalaSend", CorrespondentId = fixture.DestinationCorrespondent.Id,
            FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 100, ToAmount = 100, AgentCommissionAmount = 8, AgentCommissionCurrencyId = 1,
            CreatedAt = created, CreatedBy = fixture.UserId, Status = "Paid" };
        var oldAudit = Row(start.AddDays(-20)); var oldJournal = Row(start.AddDays(-10)); var registeredToday = Row(start);
        db.Hawalas.AddRange(oldAudit, oldJournal, registeredToday); await db.SaveChangesAsync();
        string Addition() => HawalaExchange.Infrastructure.Data.ApplicationDbContext.MergeAuditJson(null,
            new Dictionary<string, object?> { ["__Description"] = "کمیشن نمایندگی به مقدار 8 برای حواله ارسالی ثبت شد" }, true)!;
        db.AuditLogs.AddRange(new() { TableName = "Hawalas", RecordId = oldAudit.Id, Action = "UPDATE", OldValue = "{}", NewValue = Addition(), CreatedAt = start.AddHours(1), UserId = fixture.UserId },
            new() { TableName = "Hawalas", RecordId = registeredToday.Id, Action = "UPDATE", NewValue = Addition(), CreatedAt = start.AddDays(-1), UserId = fixture.UserId });
        var expense = await db.Accounts.Where(x => x.AccountType == "Expense").Select(x => x.Id).FirstAsync();
        db.LedgerEntries.AddRange(new LedgerEntry { HawalaId = oldJournal.Id, AccountId = expense, CurrencyId = 1, BadehKar = 8, CreatedAt = start.AddHours(2), Description = "حواله ارسالی: هزینه کمیشن عامل پرداخت" },
            new LedgerEntry { HawalaId = oldJournal.Id, AccountId = fixture.DestinationAccount.Id, CurrencyId = 1, TalabKar = 8, CreatedAt = start.AddHours(2), Description = "کمیشن نمایندگی" });
        await db.SaveChangesAsync();
        var filter = new AgentCommissionReportFilter { CorrespondentId = fixture.DestinationCorrespondent.Id, From = day, To = day };
        var service = new AgentCommissionReportService(db);
        var report = await service.GetAsync(filter); Assert.Equal(2, report.Count);
        var details = await service.GetDetailsAsync(filter);
        Assert.Contains(details.Items, x => x.HawalaId == oldAudit.Id && x.CommissionAddedAt == start.AddHours(1));
        Assert.Contains(details.Items, x => x.HawalaId == oldJournal.Id && x.CommissionAddedAt == start.AddHours(2));
        Assert.DoesNotContain(details.Items, x => x.HawalaId == registeredToday.Id);
        filter.DateBasis = "Registration";
        Assert.Equal(registeredToday.Id, Assert.Single((await service.GetDetailsAsync(filter)).Items).HawalaId);
    }

    [Fact]
    public async Task Report_counts_each_hawala_once_and_separates_currencies_settlements_cancellations_and_exports()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        await using var tx = await db.Database.BeginTransactionAsync();
        var day = new DateTime(2049, 5, 8);
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById("Asia/Kabul"));
        var serial = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Hawala Transfer(decimal fee, long feeCurrency = 1, string reference = "Report") => new() {
            Number = serial++, ReferenceNumber = reference, HawalaType = "HawalaSend", CorrespondentId = fixture.DestinationCorrespondent.Id,
            FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 100, ToAmount = 100, AgentCommissionAmount = fee,
            AgentCommissionCurrencyId = feeCurrency, PaymentLocationId = fixture.RemoteLocation.Id, Status = "Paid", CreatedAt = start, CreatedBy = fixture.UserId,
            SenderName = "<script>Sender</script>", ReceiverName = "Receiver" };
        var paid = Transfer(8); var historical = Transfer(2, 2, "HIST-ONLY"); var billed = Transfer(3); var unrecorded = Transfer(5);
        unrecorded.PaymentLocationId = null;
        historical.PaymentLocationId = fixture.OwnLocation.Id;
        var cancelled = Transfer(9); cancelled.Status = "Cancel";
        var zero = Transfer(0); var outside = Transfer(99); outside.CreatedAt = start.AddDays(1);
        var beforeStart = Transfer(99); beforeStart.CreatedAt = start.AddTicks(-1);
        var received = Transfer(99); received.HawalaType = "HawalaReceive";
        var other = Transfer(99); other.CorrespondentId = fixture.SourceCorrespondent.Id;
        var deleted = Transfer(99); deleted.IsDeleted = true;
        db.Hawalas.AddRange(paid, historical, billed, unrecorded, cancelled, zero, outside, beforeStart, received, other, deleted); await db.SaveChangesAsync();
        AgentPaymentBill Bill(Hawala h, string status, bool active) => new() { CorrespondentId = fixture.DestinationCorrespondent.Id, Status = status, CreatedBy = fixture.UserId,
            SnapshotJson = "{}", Items = [new() { HawalaId = h.Id, IsActive = active }] };
        var historicalBill = Bill(historical, "Historical", true);
        db.AgentPaymentBills.AddRange(Bill(paid, "Paid", true), Bill(historical, "Cancelled", false), historicalBill, Bill(billed, "Pending", true));
        await db.SaveChangesAsync();
        var ledgerCount = await db.LedgerEntries.CountAsync(); var transactionCount = await db.Transactions.CountAsync();
        HtmlToPdfDocument? pdfDocument = null;
        var pdf = new Mock<IConverter>(); pdf.Setup(x => x.Convert(It.IsAny<IDocument>())).Callback<IDocument>(x => pdfDocument = (HtmlToPdfDocument)x).Returns("%PDF-test"u8.ToArray());
        var service = new AgentCommissionReportService(db, pdf.Object);
        var filter = new AgentCommissionReportFilter { CorrespondentId = fixture.DestinationCorrespondent.Id, From = day, To = day };
        var options = await service.GetLookupsAsync(fixture.DestinationCorrespondent.Id);
        Assert.Equal(2, options.Locations.Count); Assert.Contains(options.Currencies, x => x.Name == "AFN"); Assert.Contains(options.Currencies, x => x.Name == "USD");
        var report = await service.GetAsync(filter);
        Assert.Equal(4, report.Count); Assert.Equal(3, report.Locations.Count);
        var afn = report.Currencies.Single(x => x.Currency == "AFN"); var usd = report.Currencies.Single(x => x.Currency == "USD");
        Assert.Equal(16, afn.Commission); Assert.Equal(8, afn.PaidCommission); Assert.Equal(3, afn.BilledCommission); Assert.Equal(5, afn.UnrecordedCommission);
        Assert.Equal(400, usd.Principal); Assert.Equal(2, usd.Commission); Assert.Equal(2, usd.HistoricalCommission);
        var first = await service.GetDetailsAsync(filter, pageSize: 1);
        var second = await service.GetDetailsAsync(filter, page: 2, pageSize: 1);
        Assert.Equal(4, first.TotalCount); Assert.NotEqual(first.Items.Single().HawalaId, second.Items.Single().HawalaId);
        filter.Settlement = "Historical";
        Assert.Equal(historicalBill.Id, Assert.Single((await service.GetDetailsAsync(filter)).Items).BillId);
        Assert.Equal(1, (await service.GetAsync(filter)).Count);
        filter.Settlement = ""; filter.IncludeCancelled = true;
        var withCancelled = await service.GetAsync(filter);
        Assert.Equal(5, withCancelled.Count); Assert.Equal(1, withCancelled.CancelledCount);
        Assert.Equal(16, withCancelled.Currencies.Single(x => x.Currency == "AFN").Commission);
        Assert.Equal(9, withCancelled.Currencies.Single(x => x.Currency == "AFN").CancelledCommission);
        filter.IncludeCancelled = false; filter.IncludeZeroCommission = true;
        Assert.Equal(5, (await service.GetAsync(filter)).Count);
        filter.IncludeZeroCommission = false;
        Assert.Single((await service.GetDetailsAsync(filter, fixture.OwnLocation.Id)).Items);
        Assert.Equal(unrecorded.Id, Assert.Single((await service.GetDetailsAsync(filter, 0)).Items).HawalaId);
        filter.Search = "HIST-ONLY"; Assert.Equal(1, (await service.GetAsync(filter)).Count); filter.Search = "";
        var excel = await service.ExportAsync(filter, ExportFormat.Excel);
        using (var workbook = new XLWorkbook(new MemoryStream(excel.Content))) {
            Assert.Equal(8, workbook.Worksheet("Hawalas").LastRowUsed()!.RowNumber());
            Assert.Equal(16, workbook.Worksheet("Summary").Cell(2, 5).GetValue<decimal>());
        }
        var document = await service.ExportAsync(filter, ExportFormat.Pdf);
        Assert.Equal("application/pdf", document.ContentType); Assert.NotNull(pdfDocument);
        Assert.Contains("&lt;script&gt;Sender&lt;/script&gt;", pdfDocument.Objects.Single().HtmlContent);
        Assert.DoesNotContain("<script>Sender</script>", pdfDocument.Objects.Single().HtmlContent);
        Assert.Equal(ledgerCount, await db.LedgerEntries.CountAsync()); Assert.Equal(transactionCount, await db.Transactions.CountAsync());
        using (db.UseTenantScope(999999)) await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(filter));
    }
}
