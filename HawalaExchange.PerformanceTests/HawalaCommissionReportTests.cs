using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class HawalaCommissionReportTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Estimates_use_source_day_rates_separate_all_three_types_and_never_write_valuations_or_journals()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await OwnLocation(context);
        var day = new DateTime(2042, 1, 10);
        var afn = Receive(290000001, 1, 660000, day);
        var usd = Receive(290000002, 2, 10000, day);
        context.Hawalas.AddRange(afn, usd); await context.SaveChangesAsync();
        var sentAfn = Send(290000003, afn.Id, 1, 330000, day.AddDays(1));
        var sentUsd = Send(290000004, usd.Id, 2, 6000, day.AddDays(1));
        context.Hawalas.AddRange(sentAfn, sentUsd);
        context.CorrespondentDailyCommissionRates.AddRange(Rate(day, 66), Rate(day.AddDays(1), 99)); await context.SaveChangesAsync();
        var transactions = await context.Transactions.CountAsync(); var ledger = await context.LedgerEntries.CountAsync();
        context.ChangeTracker.Clear();
        var service = new HawalaCommissionReportService(context);
        var filter = Filter(day); filter.To = day.AddDays(1); filter.Estimate = true;
        var report = await service.GetAsync(filter);
        Assert.Equal(6, report.Rows.Count);
        var forward = report.Rows.Single(x => x.HawalaId == sentAfn.Id && x.CommissionType == "Forwarding");
        Assert.Equal(66m, forward.ExchangeRate); Assert.Equal(day, forward.RateDate);
        var dollar = report.Summaries.Single(x => x.Currency == "USD");
        Assert.True(dollar.Estimate); Assert.Equal(124m, dollar.Income); Assert.Equal(6m, dollar.Expense); Assert.Equal(118m, dollar.Net);
        Assert.Equal(130m, dollar.Total);
        Assert.Equal(660m, report.Summaries.Single(x => x.Currency == "AFN").Total);
        Assert.Equal(660m, report.Summaries.Single(x => x.Currency == "AFN").Expense);
        Assert.Equal(4, report.Amounts.Sum(x => x.Count));
        Assert.Equal(660000m, report.Amounts.Single(x => x.HawalaType == "HawalaReceive" && x.Currency == "AFN").Amount);
        Assert.Equal(330000m, report.Amounts.Single(x => x.HawalaType == "HawalaSend" && x.Currency == "AFN").Amount);
        Assert.Equal(transactions, await context.Transactions.CountAsync()); Assert.Equal(ledger, await context.LedgerEntries.CountAsync());
        Assert.All(await context.Hawalas.Where(x => x.Id == afn.Id || x.Id == sentAfn.Id).ToListAsync(), h => Assert.Null(h.CommissionBaseUsdAmount));
        filter.CorrespondentIds = [fixture.SourceCorrespondent.Id];
        var sourceReport = await service.GetAsync(filter);
        Assert.DoesNotContain(sourceReport.Rows, x => x.CommissionType == "Destination");
        Assert.Equal(2, sourceReport.Rows.Count(x => x.CommissionType == "Forwarding"));
        filter.CorrespondentIds = [fixture.DestinationCorrespondent.Id];
        Assert.All((await service.GetAsync(filter)).Rows, x => Assert.Equal("Destination", x.CommissionType));
    }

    [Fact]
    public async Task Missing_rates_remain_unknown_and_cancelled_source_or_own_office_is_not_estimated_as_forwarding()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        await OwnLocation(context);
        var day = new DateTime(2042, 1, 15);
        var source = Receive(290000010, 1, 100000, day);
        context.Hawalas.Add(source); await context.SaveChangesAsync();
        var sent = Send(290000011, source.Id, 1, 50000, day);
        sent.PaymentLocationId = fixture.OwnLocation.Id;
        context.Hawalas.Add(sent); await context.SaveChangesAsync();
        var filter = Filter(day); filter.Estimate = true;
        var service = new HawalaCommissionReportService(context);
        var report = await service.GetAsync(filter);
        var income = report.Rows.Single(x => x.CommissionType == "Incoming");
        Assert.Null(income.Commission); Assert.Contains("نرخ روز", income.Note);
        Assert.DoesNotContain(report.Rows, x => x.CommissionType == "Forwarding");
        source.Status = "Cancel"; await context.SaveChangesAsync();
        report = await service.GetAsync(filter);
        Assert.DoesNotContain(report.Rows, x => x.IsEstimate);
    }

    [Fact]
    public async Task Stored_commission_uses_saved_values_and_exact_recognition_not_current_rates_or_date_guesses()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var day = new DateTime(2042, 1, 12);
        var source = Receive(290000020, 1, 660000, day);
        var transaction = new Transaction { TransactionNo = "REPORT-SNAPSHOT", TransactionType = "PeriodicCorrespondentCommission", BranchId = 1, CreatedBy = fixture.UserId, Status = "Paid" };
        context.AddRange(source, transaction); await context.SaveChangesAsync();
        var batch = new CorrespondentCommissionBatch
        {
            CorrespondentId = fixture.SourceCorrespondent.Id, CommissionScope = "Incoming", AccountingVersion = 2,
            PostingTransactionId = transaction.Id, CreatedBy = fixture.UserId, PeriodFrom = day, PeriodTo = day,
            TotalBaseAfn = 10000, TotalCommissionAfn = 50, TotalCommissionUsd = 50, CommissionPerLakhAfn = 500,
            Items = [new CorrespondentCommissionBatchItem { HawalaId = source.Id, CommissionScope = "Incoming", SourceCurrencyId = 1,
                SourceAmount = 660000, AfnEquivalent = 10000, SourceToAfnRate = 66, CommissionAfn = 50, PerLakhRate = 500, ValuationDate = day }]
        };
        context.CorrespondentCommissionBatches.Add(batch);
        context.CorrespondentDailyCommissionRates.Add(Rate(day, 99)); await context.SaveChangesAsync();
        var service = new HawalaCommissionReportService(context);
        var filter = Filter(day); filter.Estimate = true; filter.IncomingRate = 1000;
        var row = Assert.Single((await service.GetAsync(filter)).Rows);
        Assert.False(row.IsEstimate); Assert.Equal(50m, row.Commission); Assert.Equal(66m, row.ExchangeRate); Assert.Equal("Calculated", row.CommissionStatus);
        var file = await service.ExportAsync(filter);
        using (var workbook = new XLWorkbook(new MemoryStream(file.Content)))
        {
            Assert.Equal("مجموع کمیشن‌ها (دریافتی + پرداختی)", workbook.Worksheet("Summary").Cell(1, 6).GetString());
            Assert.Equal(50m, workbook.Worksheet("Summary").Cell(2, 6).GetValue<decimal>());
        }
        var period = new CorrespondentAccountPeriod { CorrespondentId = fixture.SourceCorrespondent.Id, PeriodNumber = 901, PeriodFrom = day, PeriodTo = day.AddDays(1), ClosedAt = day.AddDays(1), ClosedBy = fixture.UserId };
        context.CorrespondentAccountPeriods.Add(period); await context.SaveChangesAsync();
        Assert.Equal("Calculated", Assert.Single((await service.GetAsync(filter)).Rows).CommissionStatus);
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.CorrespondentCommissionRecognitions (TenantId, AccountId, TransactionId, PeriodId) VALUES ({context.CurrentTenantId}, {fixture.SourceAccount.Id}, {transaction.Id}, {period.Id})");
        Assert.Equal("Recognized", Assert.Single((await service.GetAsync(filter)).Rows).CommissionStatus);
        batch.Status = "Reversed"; batch.Items.Single().IsActive = false; await context.SaveChangesAsync();
        filter.CommissionStatus = "Reversed";
        var reversed = await service.GetAsync(filter);
        Assert.Equal(50m, Assert.Single(reversed.Rows).Commission); Assert.Empty(reversed.Summaries);
        // Remove this test-only close so it cannot constrain unrelated commission
        // scenarios using the shared disposable fixture's earlier transfer dates.
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.CorrespondentCommissionRecognitions WHERE TenantId = {context.CurrentTenantId} AND PeriodId = {period.Id}");
        context.CorrespondentAccountPeriods.Remove(period); await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Filters_tenant_isolation_bulk_provenance_and_excel_preserve_literal_details()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var day = new DateTime(2042, 1, 13);
        var source = Receive(290000030, 2, 10000, day); source.SenderName = "=not-a-formula"; source.ReferenceNumber = "SEARCH-REF";
        var manual = Receive(290000031, 2, 20000, day);
        context.Hawalas.AddRange(source, manual); await context.SaveChangesAsync();
        var batch = new HawalaImportBatch { FileName = "Report.xlsx", FileHash = "test", CorrespondentId = fixture.SourceCorrespondent.Id, CreatedBy = fixture.UserId,
            Rows = [new HawalaImportRow { ExcelRowNumber = 1, HawalaId = source.Id }, new HawalaImportRow { ExcelRowNumber = 2 }] };
        context.HawalaImportBatches.Add(batch); await context.SaveChangesAsync();
        var filter = Filter(day); filter.Registration = "Bulk"; filter.Search = "SEARCH-REF";
        var service = new HawalaCommissionReportService(context);
        var report = await service.GetAsync(filter);
        Assert.Equal(source.Id, Assert.Single(report.Rows).HawalaId);
        var file = await service.ExportAsync(filter);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal(4, workbook.Worksheets.Count);
        Assert.Equal("=not-a-formula", workbook.Worksheet(1).Cell(2, 8).GetString());
        Assert.False(workbook.Worksheet(1).Cell(2, 8).HasFormula);
        Assert.Equal(2, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
        filter.Search = ""; filter.Registration = "Manual";
        Assert.Equal(manual.Id, Assert.Single((await service.GetAsync(filter)).Rows).HawalaId);
        using (context.UseTenantScope(999999)) Assert.Empty((await service.GetAsync(Filter(day))).Rows);
        Assert.Equal(2, await context.Hawalas.CountAsync(x => x.Id == source.Id || x.Id == manual.Id));
    }

    private Hawala Receive(long number, long currency, decimal amount, DateTime day) => new()
    { Number = number, HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id, FromCurrencyId = currency, ToCurrencyId = currency,
        FromAmount = amount, ToAmount = amount, PaymentLocationId = fixture.OwnLocation.Id, CreatedAt = day.AddHours(10).ToUniversalTime(), CreatedBy = fixture.UserId, Status = "Paid", SenderName = "Sender", ReceiverName = "Receiver" };
    private Hawala Send(long number, long sourceId, long currency, decimal amount, DateTime day)
    { var h = Receive(number, currency, amount, day); h.HawalaType = "HawalaSend"; h.SourceHawalaId = sourceId; h.CorrespondentId = fixture.DestinationCorrespondent.Id; h.PaymentLocationId = fixture.RemoteLocation.Id; return h; }
    private CorrespondentDailyCommissionRate Rate(DateTime day, decimal rate) => new() { CorrespondentId = fixture.SourceCorrespondent.Id, RateDate = day, UsdToAfnRate = rate, CreatedBy = fixture.UserId };
    private static HawalaCommissionReportFilter Filter(DateTime day) => new() { From = day, To = day };
    private async Task OwnLocation(HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
    { var setting = await context.CompanySettings.FirstOrDefaultAsync(); if (setting == null) { setting = new CompanySetting { CompanyName = "Report tests" }; context.CompanySettings.Add(setting); } setting.OwnPaymentLocationId = fixture.OwnLocation.Id; await context.SaveChangesAsync(); }
}
