using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class AccountBalancePerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Procedure_returns_multi_currency_current_and_historical_balances()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var customer = NewCustomer("BAL-CUSTOMER");
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        var account = NewAccount("BAL-ACCOUNT", "Customer", customerId: customer.Id);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        context.LedgerEntries.AddRange(
            Entry(account.Id, 1, 1_000, 0, cutoff.AddMinutes(-2)),
            Entry(account.Id, 1, 0, 250, cutoff.AddMinutes(-1)),
            Entry(account.Id, 2, 500, 0, cutoff.AddMinutes(-1)),
            Entry(account.Id, 2, 50, 0, cutoff.AddMinutes(1)),
            Entry(account.Id, 1, 100, 0, cutoff.AddMinutes(-1)),
            Entry(account.Id, 1, 0, 100, cutoff.AddMinutes(-1)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateBalanceService(context);

        var historical = (await service.GetAccountBalanceAsync(account.Id, cutoff)).ToList();
        var current = (await service.GetAccountBalanceAsync(account.Id)).ToList();
        var customerBalance = await service.GetCustomerBalanceAsync(customer.Id);

        Assert.Equal(750, historical.Single(x => x.CurrencyId == 1).Balance);
        Assert.Equal(500, historical.Single(x => x.CurrencyId == 2).Balance);
        Assert.Equal(550, current.Single(x => x.CurrencyId == 2).Balance);
        Assert.Equal(current.Select(x => (x.CurrencyId, x.Balance)),
            customerBalance!.Balances.Select(x => (x.CurrencyId, x.Balance)));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Bulk_owner_reads_include_empty_owners_without_n_plus_one_queries()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var correspondents = Enumerable.Range(0, 20).Select(index => new Correspondent
        {
            Code = $"BAL-{suffix}-{index}",
            Name = $"Balance correspondent {suffix} {index}",
            CommissionMethod = "PerTransaction"
        }).ToArray();
        var emptyCorrespondent = new Correspondent
        {
            Code = $"BAL-{suffix}-EMPTY",
            Name = $"Balance correspondent {suffix} empty",
            CommissionMethod = "PerTransaction"
        };
        context.Correspondents.AddRange(correspondents.Append(emptyCorrespondent));
        await context.SaveChangesAsync();
        var accounts = correspondents.Select((correspondent, index) =>
            NewAccount($"BAL-{suffix}-{index}", "Correspondent", correspondentId: correspondent.Id)).ToArray();
        context.Accounts.AddRange(accounts);
        await context.SaveChangesAsync();
        context.LedgerEntries.AddRange(accounts.Select((account, index) =>
            Entry(account.Id, 2, 1_000 + index, 0, DateTime.UtcNow)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        fixture.Commands.Reset();
        var results = (await fixture.CreateBalanceService(context).GetAllCorrespondentBalancesAsync()).ToList();

        Assert.Equal(1, fixture.Commands.Count); // owner query; the raw stored-procedure call is the second round trip
        foreach (var correspondent in correspondents)
        {
            var result = Assert.Single(results, x => x.CorrespondentId == correspondent.Id);
            Assert.Single(result.Balances);
        }
        Assert.Empty(Assert.Single(results, x => x.CorrespondentId == emptyCorrespondent.Id).Balances);
    }

    [Fact]
    public async Task Procedure_is_tenant_scoped_and_limit_reads_are_set_based()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var account = NewAccount($"BAL-LIMIT-{Guid.NewGuid():N}", "Cash");
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        context.LedgerEntries.Add(Entry(account.Id, 2, 0, 400, DateTime.UtcNow));
        context.AccountBadehkarLimits.Add(new AccountBadehkarLimit
        {
            AccountId = account.Id,
            CurrencyId = 2,
            BadehkarLimit = 500,
            IsActive = true,
            CreatedBy = fixture.UserId
        });
        var secondTenant = new Tenant { Name = $"Balance tenant {Guid.NewGuid():N}", IsActive = true };
        context.Tenants.Add(secondTenant);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateBalanceService(context);

        Assert.True(await service.ValidateBadehkarLimitAsync(account.Id, 2, 100));
        Assert.False(await service.ValidateBadehkarLimitAsync(account.Id, 2, 101));
        fixture.Commands.Reset();
        var limits = (await service.GetAccountsWithLimitsAsync()).ToList();
        Assert.Contains(limits, x => x.AccountId == account.Id && x.Balance == -400 && x.AvailableBalance == 100);
        Assert.Equal(1, fixture.Commands.Count); // limit query; the raw stored-procedure call is the second round trip

        using (context.UseTenantScope(secondTenant.Id))
            Assert.Empty(await fixture.CreateBalanceService(context).GetAccountBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Measure_set_based_correspondent_balances_against_legacy_n_plus_one_reads()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Balance performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int accountCount = 500;
        const int entriesPerAccount = 20;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var correspondents = Enumerable.Range(0, accountCount).Select(index => new Correspondent
        {
            Code = $"PERF-BAL-{suffix}-{index}",
            Name = $"Performance balance {suffix} {index}",
            CommissionMethod = "PerTransaction"
        }).ToArray();
        context.Correspondents.AddRange(correspondents);
        await context.SaveChangesAsync();
        var accounts = correspondents.Select((correspondent, index) =>
            NewAccount($"PERF-BAL-{suffix}-{index}", "Correspondent", correspondentId: correspondent.Id)).ToArray();
        context.Accounts.AddRange(accounts);
        await context.SaveChangesAsync();
        context.LedgerEntries.AddRange(accounts.SelectMany(account =>
            Enumerable.Range(0, entriesPerAccount).Select(index =>
                Entry(account.Id, index % 2 == 0 ? 1 : 2, 100 + index, 0, DateTime.UtcNow.AddMinutes(-index)))));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        fixture.Commands.Reset();
        var legacyStopwatch = Stopwatch.StartNew();
        foreach (var account in accounts)
        {
            _ = await context.LedgerEntries.AsNoTracking()
                .Where(x => x.AccountId == account.Id)
                .GroupBy(x => x.CurrencyId)
                .Select(x => x.Sum(entry => entry.TalabKar - entry.BadehKar))
                .ToListAsync();
        }
        legacyStopwatch.Stop();
        var legacyCommands = fixture.Commands.Count;

        fixture.Commands.Reset();
        var procedureStopwatch = Stopwatch.StartNew();
        var results = (await fixture.CreateBalanceService(context).GetAllCorrespondentBalancesAsync()).ToList();
        procedureStopwatch.Stop();
        var procedureDatabaseCalls = fixture.Commands.Count + 1; // EF owner query + raw stored procedure

        Assert.All(correspondents, correspondent =>
            Assert.Contains(results, x => x.CorrespondentId == correspondent.Id && x.Balances.Count == 2));
        Assert.Equal(accountCount, legacyCommands);
        Assert.Equal(2, procedureDatabaseCalls);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            accounts = accountCount,
            ledgerEntries = accountCount * entriesPerAccount,
            legacyMilliseconds = Math.Round(legacyStopwatch.Elapsed.TotalMilliseconds, 2),
            legacyCommands,
            storedProcedureMilliseconds = Math.Round(procedureStopwatch.Elapsed.TotalMilliseconds, 2),
            storedProcedureDatabaseCalls = procedureDatabaseCalls,
            improvementPercent = Math.Round((1 - procedureStopwatch.Elapsed.TotalMilliseconds /
                legacyStopwatch.Elapsed.TotalMilliseconds) * 100, 2)
        }));
    }

    private static Customer NewCustomer(string prefix) => new()
    {
        CustomerCode = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(50, prefix.Length + 33)],
        FullName = $"{prefix} customer"
    };

    private static Account NewAccount(string code, string type, long? customerId = null, long? correspondentId = null) => new()
    {
        AccountCode = code.Length <= 50 ? code : code[..50],
        AccountName = $"{code} account",
        AccountType = type,
        CustomerId = customerId,
        CorrespondentId = correspondentId
    };

    private static LedgerEntry Entry(long accountId, long currencyId, decimal talabKar, decimal badehKar, DateTime createdAt) => new()
    {
        AccountId = accountId,
        CurrencyId = currencyId,
        TalabKar = talabKar,
        BadehKar = badehKar,
        Description = "Balance procedure test",
        CreatedAt = createdAt
    };
}
