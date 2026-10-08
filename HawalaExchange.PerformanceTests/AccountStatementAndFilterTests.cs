using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Moq;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.PerformanceTests;

public sealed class RecordFilterSetTests
{
    [Fact]
    public void Multiple_conditions_apply_before_pagination_and_preserve_null_safety()
    {
        var items = Enumerable.Range(1, 30).Select(i => new ExpenseDto { Id = i, Amount = i * 100, CurrencyCode = i % 2 == 0 ? "USD" : "AFN", Title = i == 28 ? "Special" : "Other" }).ToList();
        var filters = new RecordFilterSet();
        filters.Rules.Add(new() { Field = "CurrencyCode", Text = "USD", Exact = true });
        filters.Rules.Add(new() { Field = "Amount", Min = 2500, Max = 2900 });
        Assert.Equal(new long[] { 26, 28 }, filters.Apply(items).Take(10).Select(x => x.Id));
        filters.Rules.Add(new() { Field = "Title", Text = "special" });
        Assert.Equal(28, Assert.Single(filters.Apply(items)).Id);
        filters.Clear(); Assert.Equal(30, filters.Apply(items).Count());
    }
    [Fact]
    public void Dates_are_inclusive_ranges_and_invalid_ranges_never_silently_pass()
    {
        var day = new DateTime(2045, 6, 1);
        var rows = new[] { new CustomerDto { CreatedAt = day.AddHours(23).ToUniversalTime(), FullName = "A" }, new CustomerDto { CreatedAt = day.AddDays(1).ToUniversalTime(), FullName = "B" } };
        var state = new RecordFilterSet(); state.Rules.Add(new() { Field = "CreatedAt", From = day, To = day });
        Assert.Equal("A", Assert.Single(state.Apply(rows)).FullName);
        state.Rules[0].From = day.AddDays(2); Assert.NotEmpty(state.Error); Assert.Empty(state.Apply(rows));
        state.Rules[0].FromValue = DateTime.MinValue; Assert.Null(state.Rules[0].From);
    }
    [Fact]
    public void Only_allowlisted_scalar_fields_are_exposed()
    {
        Assert.Contains(RecordFilterSet.Fields<MoneyExchangeOperationDto>(), x => x.Name == "ExchangeDate");
        Assert.Contains(RecordFilterSet.Fields<AedDealDto>(), x => x.Name == "SourceCorrespondentName");
        Assert.DoesNotContain(RecordFilterSet.Fields<CustomerDto>(), x => x.Name.Contains("Path") || x.Name == "Id");
    }
}

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class AccountStatementAndFilterTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Statement_reconciles_opening_movements_closing_and_keeps_filtered_details_separate()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var account = new Account { AccountCode = "STMT-CUSTOMER", AccountName = "Statement customer", AccountType = "Customer" };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        var day = new DateTime(2046, 1, 10);
        context.LedgerEntries.AddRange(Entry(account.Id, day.AddDays(-1), 1000, 0), Entry(account.Id, day, 500, 0), Entry(account.Id, day.AddHours(23), 0, 200), Entry(account.Id, day.AddDays(1), 900, 0));
        await context.SaveChangesAsync(); var ledgerCount = await context.LedgerEntries.CountAsync(); context.ChangeTracker.Clear();
        var service = new AccountStatementService(context);
        var filter = new AccountStatementFilter { AccountId = account.Id, From = day, To = day };
        var result = await service.GetAsync(filter); var summary = Assert.Single(result.Summaries);
        Assert.Equal(1000, summary.Opening); Assert.Equal(500, summary.Credit); Assert.Equal(200, summary.Debit); Assert.Equal(1300, summary.Closing);
        Assert.Equal(new decimal[] { 1500, 1300 }, result.Entries.Select(x => x.Balance));
        filter.Direction = "Debit"; filter.MinAmount = 100;
        var selected = await service.GetAsync(filter); Assert.Equal(1300, Assert.Single(selected.Summaries).Closing); Assert.Equal(200, Assert.Single(selected.Entries).Debit);
        Assert.Equal(ledgerCount, await context.LedgerEntries.CountAsync()); Assert.Empty(context.ChangeTracker.Entries());
        using (context.UseTenantScope(999999)) await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(filter));
    }

    [Fact]
    public async Task Pending_commission_is_excluded_then_moves_into_statement_on_period_close_without_reposting()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var correspondent = new Correspondent { Code = "STMT-CORR", Name = "Statement correspondent" };
        context.Correspondents.Add(correspondent); await context.SaveChangesAsync();
        var account = new Account { AccountCode = "STMT-CORR", AccountName = correspondent.Name, AccountType = "Correspondent", CorrespondentId = correspondent.Id };
        var transaction = new Transaction { TransactionNo = "STMT-COMMISSION", TransactionType = "PeriodicCorrespondentCommission", BranchId = 1, CreatedBy = fixture.UserId, Status = "Paid" };
        context.AddRange(account, transaction); await context.SaveChangesAsync();
        var posted = new DateTime(2046, 2, 1); var closed = posted.AddDays(2);
        var ledger = Entry(account.Id, posted, 0, 50); ledger.TransactionId = transaction.Id;
        context.LedgerEntries.Add(ledger); await context.SaveChangesAsync();
        var service = new AccountStatementService(context);
        var filter = new AccountStatementFilter { AccountId = account.Id, From = posted, To = posted };
        var pending = await service.GetAsync(filter);
        Assert.Empty(pending.Entries); Assert.Equal(0, Assert.Single(pending.Summaries).Closing); Assert.Equal(50, pending.Summaries.Single().PendingDebit);
        var period = new CorrespondentAccountPeriod { CorrespondentId = correspondent.Id, PeriodNumber = 1, PeriodFrom = posted.ToUniversalTime(), PeriodTo = closed.ToUniversalTime(), ClosedAt = closed.ToUniversalTime(), ClosedBy = fixture.UserId };
        context.CorrespondentAccountPeriods.Add(period); await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.CorrespondentCommissionRecognitions (TenantId, AccountId, TransactionId, PeriodId) VALUES ({context.CurrentTenantId}, {account.Id}, {transaction.Id}, {period.Id})");
        Assert.Equal(50, Assert.Single((await service.GetAsync(filter)).Summaries).PendingDebit);
        filter.From = filter.To = closed;
        var recognized = await service.GetAsync(filter);
        Assert.Equal(closed.ToUniversalTime(), Assert.Single(recognized.Entries).Date);
        Assert.Equal(-50, Assert.Single(recognized.Summaries).Closing); Assert.Equal(0, recognized.Summaries.Single().PendingDebit);
        Assert.Equal(1, await context.LedgerEntries.CountAsync(x => x.AccountId == account.Id));
        filter.From = filter.To = closed.AddDays(1);
        Assert.Equal(-50, Assert.Single((await service.GetAsync(filter)).Summaries).Opening);
    }

    [Fact]
    public async Task Account_operation_amount_direction_and_currency_filters_run_before_pagination()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var account = new Account { AccountCode = "STMT-FILTER", AccountName = "Filter account", AccountType = "Customer" };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        var day = new DateTime(2046, 3, 1);
        context.LedgerEntries.AddRange(Entry(account.Id, day, 100, 0), Entry(account.Id, day.AddHours(1), 800, 0), Entry(account.Id, day.AddHours(2), 0, 900));
        await context.SaveChangesAsync();
        var service = new JournalService(context);
        var result = await service.GetAccountOperationsPageAsync(new() { AccountId = account.Id, MinAmount = 500, MaxAmount = 850, Direction = "Credit", CurrencyCode = "USD", PageSize = 1 });
        Assert.Equal(1, result.TotalCount); Assert.Equal(800, Assert.Single(Assert.Single(result.Items).CurrencySummaries).TotalTalabKar);
    }

    [Fact]
    public async Task Hawala_extra_filters_combine_sender_reference_numbers_and_manual_provenance_before_counting()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var day = new DateTime(2046, 4, 1);
        context.Hawalas.AddRange(Transfer(295000001, "Chosen", day), Transfer(295000002, "Other", day)); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        var result = await service.GetHawalasAsync(new() { PageSize = 1, Extra = new() { Sender = "Chosen", Reference = "FILTER", MinNumber = 295000001, MaxNumber = 295000002, From = day, To = day, CurrencyId = 2, Registration = "Manual", Commission = "Without" } });
        Assert.Equal(1, result.TotalCount); Assert.Equal(295000001, Assert.Single(result.Items).Number);
    }
    [Fact]
    public async Task Filtered_customer_excel_keeps_actual_running_balance_and_handles_missing_description()
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var customer = new Customer { CustomerCode = "STMT-EXPORT", FullName = "Export customer" };
        context.Customers.Add(customer); await context.SaveChangesAsync();
        var account = new Account { AccountCode = "STMT-EXPORT", AccountName = customer.FullName, AccountType = "Customer", CustomerId = customer.Id };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        var day = new DateTime(2046, 5, 1);
        var opening = Entry(account.Id, day.AddDays(-1), 1000, 0); opening.Description = null;
        context.LedgerEntries.AddRange(opening, Entry(account.Id, day, 500, 0), Entry(account.Id, day.AddHours(23), 0, 200)); await context.SaveChangesAsync();
        var service = new ExportService(context, Mock.Of<IConverter>());
        var file = await service.ExportCustomerActivitiesAsync(new() { CustomerId = customer.Id, FromDate = day.ToUniversalTime(), ToDate = day.AddDays(1).ToUniversalTime().AddTicks(-1), Direction = "Debit", Search = "Statement test", CurrencyCode = "USD", MinAmount = 100 }, ExportFormat.Excel);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal(1300m, workbook.Worksheet(1).Cell(5, 7).GetValue<decimal>());
        Assert.Equal(200m, workbook.Worksheet(1).Cell(5, 4).GetValue<decimal>());
    }
    private LedgerEntry Entry(long account, DateTime local, decimal credit, decimal debit) => new() { AccountId = account, CurrencyId = 2, CreatedAt = local.ToUniversalTime(), TalabKar = credit, BadehKar = debit, Description = "Statement test" };
    private Hawala Transfer(long number, string sender, DateTime day) => new() { Number = number, SenderName = sender, ReceiverName = "Receiver", ReferenceNumber = "FILTER", HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id, FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 1000, ToAmount = 1000, Status = "Paid", CreatedBy = fixture.UserId, CreatedAt = day.AddHours(10).ToUniversalTime() };
}
