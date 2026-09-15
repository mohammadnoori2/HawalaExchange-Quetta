using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class DashboardPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Dashboard_procedure_preserves_counts_profit_balances_and_tenant_scope()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var date = new DateTime(2080, 4, 12);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = new Customer { CustomerCode = $"DASH-{suffix}", FullName = $"Dashboard {suffix}" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        var customerAccount = new Account
        {
            AccountCode = $"DASH-{suffix}", AccountName = customer.FullName,
            AccountType = "Customer", CustomerId = customer.Id
        };
        context.Accounts.Add(customerAccount);
        await context.SaveChangesAsync();
        var incomeAccount = await context.Accounts.SingleAsync(x => x.AccountCode == "3001");
        context.Hawalas.AddRange(
            NewHawala(8_800_001, date.AddHours(9), "Paid"),
            NewHawala(8_800_002, date.AddHours(10), "Cancel"));
        context.LedgerEntries.AddRange(
            Entry(customerAccount.Id, 2, 0, 500, date.AddHours(9)),
            Entry(incomeAccount.Id, 2, 80, 0, date.AddHours(9)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await fixture.CreateFinancialReportService(context)
            .GetDashboardSummaryAsync(date, date, 2);

        Assert.Equal(1, result.HawalaCount);
        Assert.Equal(1, result.TotalActivityCount);
        Assert.Equal(80, result.NetProfit);
        Assert.Equal(500, Assert.Single(result.TopDebtors, x => x.AccountId == customerAccount.Id).Amount);
        Assert.Equal(1, Assert.Single(result.DailyActivities).Value);
        Assert.Equal(date.AddHours(9), result.LastActivityAt);

        var secondTenant = new Tenant { Name = $"Dashboard tenant {suffix}", IsActive = true };
        context.Tenants.Add(secondTenant);
        await context.SaveChangesAsync();
        using (context.UseTenantScope(secondTenant.Id))
        {
            var usd = new Currency { Code = "USD", Name = "US Dollar", IsActive = true };
            context.Currencies.Add(usd);
            await context.SaveChangesAsync();
            var isolated = await fixture.CreateFinancialReportService(context)
                .GetDashboardSummaryAsync(date, date, usd.Id);
            Assert.Equal(0, isolated.TotalActivityCount);
            Assert.Empty(isolated.TopDebtors);
        }
    }

    [Fact]
    public async Task Measure_aggregated_dashboard_against_loading_raw_ledger()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Dashboard performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int entryCount = 50_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var date = new DateTime(2081, 1, 1);
        var account = await context.Accounts.SingleAsync(x => x.AccountCode == "3001");
        context.LedgerEntries.AddRange(Enumerable.Range(0, entryCount).Select(index =>
            Entry(account.Id, 2, 10, 0, date.AddMinutes(index % 1440))));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var legacyWatch = Stopwatch.StartNew();
        var legacyRows = await context.LedgerEntries.AsNoTracking()
            .Include(x => x.Account).Include(x => x.Currency)
            .Where(x => x.CreatedAt >= date && x.CreatedAt < date.AddDays(1))
            .ToListAsync();
        legacyWatch.Stop();

        var procedureWatch = Stopwatch.StartNew();
        var dashboard = await fixture.CreateFinancialReportService(context)
            .GetDashboardSummaryAsync(date, date, 2);
        procedureWatch.Stop();

        Assert.Equal(entryCount * 10m, dashboard.NetProfit);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ledgerEntries = legacyRows.Count,
            legacyMilliseconds = Math.Round(legacyWatch.Elapsed.TotalMilliseconds, 2),
            storedProcedureMilliseconds = Math.Round(procedureWatch.Elapsed.TotalMilliseconds, 2),
            improvementPercent = Math.Round((1 - procedureWatch.Elapsed.TotalMilliseconds /
                legacyWatch.Elapsed.TotalMilliseconds) * 100, 2)
        }));
    }

    [Fact]
    public async Task Journal_procedure_returns_balanced_readable_entries_for_the_selected_local_day()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var localDate = new DateTime(2082, 2, 3);
        var createdAt = localDate.ToUniversalTime().AddHours(8);
        var cash = await context.Accounts.SingleAsync(x => x.AccountCode == "1001");
        var income = await context.Accounts.SingleAsync(x => x.AccountCode == "3001");
        context.LedgerEntries.AddRange(
            Entry(cash.Id, 2, 0, 125, createdAt),
            Entry(income.Id, 2, 125, 0, createdAt));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var journal = await fixture.CreateJournalService(context).GetDailyJournalAsync(localDate);

        Assert.Equal(2, journal.Entries.Count);
        var usd = Assert.Single(journal.CurrencySummaries);
        Assert.Equal(125, usd.TotalTalabKar);
        Assert.Equal(125, usd.TotalBadehKar);
        Assert.All(journal.Operations, operation => Assert.False(string.IsNullOrWhiteSpace(operation.SummarySentence)));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private Hawala NewHawala(long number, DateTime createdAt, string status) => new()
    {
        Number = number, HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id,
        SenderName = "Sender", ReceiverName = "Receiver", FromCurrencyId = 2, ToCurrencyId = 2,
        FromAmount = 100, ToAmount = 100, Status = status, CreatedAt = createdAt,
        CreatedBy = fixture.UserId
    };

    private static LedgerEntry Entry(long accountId, long currencyId, decimal talabKar, decimal badehKar, DateTime createdAt) => new()
    {
        AccountId = accountId, CurrencyId = currencyId, TalabKar = talabKar,
        BadehKar = badehKar, CreatedAt = createdAt, Description = "Dashboard performance test"
    };
}
