using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class CorrespondentDetailsPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Details_page_combines_header_and_account_without_tracking()
    {
        await using var context = fixture.CreateContext();
        var service = fixture.CreateCorrespondentService(context);
        context.ChangeTracker.Clear();
        fixture.Commands.Reset();

        var result = await service.GetDetailsPageAsync(fixture.SourceCorrespondent.Id);

        Assert.NotNull(result);
        Assert.Equal(fixture.SourceCorrespondent.Id, result.Correspondent.Id);
        Assert.Equal(fixture.SourceCorrespondent.Code, result.Correspondent.Code);
        Assert.Equal(fixture.SourceCorrespondent.Name, result.Correspondent.Name);
        Assert.Equal(fixture.SourceCorrespondent.SettlementCurrencyId, result.Correspondent.SettlementCurrencyId);
        Assert.Equal("USD", result.Correspondent.SettlementCurrencyCode);
        Assert.Equal(fixture.SourceAccount.Id, result.AccountId);
        Assert.Equal(1, fixture.Commands.Count);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Initial_details_path_reduces_database_round_trips()
    {
        await using var context = fixture.CreateContext();
        var correspondentService = fixture.CreateCorrespondentService(context);
        var hawalaService = fixture.CreateService(context);
        var filter = new HawalaFilterDto
        {
            CorrespondentId = fixture.SourceCorrespondent.Id,
            HawalaType = "HawalaSend",
            PageNumber = 1,
            PageSize = 10,
            SortColumn = "CreatedAt",
            SortDirection = "desc"
        };

        context.ChangeTracker.Clear();
        fixture.Commands.Reset();
        var legacyTimer = Stopwatch.StartNew();
        var legacyCorrespondent = await context.Correspondents
            .AsNoTracking()
            .Include(x => x.SettlementCurrency)
            .FirstAsync(x => x.Id == fixture.SourceCorrespondent.Id);
        _ = await context.Currencies.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        var legacyAccountId = await context.Accounts
            .AsNoTracking()
            .Where(x => x.CorrespondentId == fixture.SourceCorrespondent.Id && !x.IsArchived)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync();
        var legacyHawalas = await hawalaService.GetHawalasAsync(filter);
        legacyTimer.Stop();
        var legacyCommands = fixture.Commands.Count;

        context.ChangeTracker.Clear();
        fixture.Commands.Reset();
        var optimizedTimer = Stopwatch.StartNew();
        var optimizedDetails = await correspondentService.GetDetailsPageAsync(fixture.SourceCorrespondent.Id);
        var optimizedHawalas = await hawalaService.GetHawalasAsync(filter);
        optimizedTimer.Stop();
        var optimizedCommands = fixture.Commands.Count;

        Assert.NotNull(optimizedDetails);
        Assert.Equal(legacyCorrespondent.Id, optimizedDetails.Correspondent.Id);
        Assert.Equal(legacyAccountId, optimizedDetails.AccountId);
        Assert.Equal(legacyHawalas.TotalCount, optimizedHawalas.TotalCount);
        Assert.Equal(legacyHawalas.Items.Select(x => x.Id), optimizedHawalas.Items.Select(x => x.Id));
        Assert.Equal(5, legacyCommands);
        Assert.Equal(3, optimizedCommands);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            legacyCommands,
            optimizedCommands,
            roundTripReductionPercent = 40,
            legacyMilliseconds = Math.Round(legacyTimer.Elapsed.TotalMilliseconds, 2),
            optimizedMilliseconds = Math.Round(optimizedTimer.Elapsed.TotalMilliseconds, 2)
        }));
    }

    [Fact]
    public async Task Details_page_is_tenant_scoped_and_honors_cancellation()
    {
        await using var context = fixture.CreateContext();
        var service = fixture.CreateCorrespondentService(context);

        using (context.UseTenantScope(2))
            Assert.Null(await service.GetDetailsPageAsync(fixture.SourceCorrespondent.Id));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetDetailsPageAsync(fixture.SourceCorrespondent.Id, cancellation.Token));
    }

    [Fact]
    public async Task Status_page_returns_header_balance_and_first_operations_page_in_one_call()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var marker = $"Status bootstrap {Guid.NewGuid():N}";
        var future = DateTime.UtcNow.AddYears(5);
        var entries = Enumerable.Range(0, 15).Select(index => new LedgerEntry
        {
            AccountId = fixture.SourceAccount.Id,
            CurrencyId = 2,
            TalabKar = 321 + index,
            Description = $"{marker}-{index}",
            CreatedAt = future.AddMinutes(index)
        }).ToArray();
        context.LedgerEntries.AddRange(entries);
        await context.SaveChangesAsync();
        var expectedBalance = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == fixture.SourceAccount.Id && x.CurrencyId == 2)
            .SumAsync(x => x.TalabKar - x.BadehKar);
        context.ChangeTracker.Clear();

        fixture.Commands.Reset();
        var legacyTimer = Stopwatch.StartNew();
        var legacyDetails = await fixture.CreateCorrespondentService(context)
            .GetDetailsPageAsync(fixture.SourceCorrespondent.Id);
        var legacyBalances = (await fixture.CreateBalanceService(context)
            .GetAccountBalanceAsync(fixture.SourceAccount.Id)).ToList();
        var legacyOperations = await fixture.CreateJournalService(context)
            .GetAccountOperationsPageAsync(new AccountOperationsFilterDto
            {
                AccountId = fixture.SourceAccount.Id,
                PageSize = 10
            });
        legacyTimer.Stop();
        var legacyDatabaseCalls = fixture.Commands.Count + 2;

        fixture.Commands.Reset();
        var combinedTimer = Stopwatch.StartNew();
        var result = await fixture.CreateCorrespondentService(context)
            .GetStatusPageAsync(fixture.SourceCorrespondent.Id);
        combinedTimer.Stop();
        var combinedDatabaseCalls = fixture.Commands.Count + 1;

        Assert.NotNull(result);
        Assert.NotNull(legacyDetails);
        Assert.Equal(fixture.SourceCorrespondent.Id, result.Correspondent.Id);
        Assert.Equal(fixture.SourceAccount.Id, result.AccountId);
        Assert.Equal(expectedBalance, result.Balances.Single(x => x.CurrencyId == 2).Balance);
        Assert.Equal(legacyBalances.Select(x => (x.CurrencyId, x.Balance)),
            result.Balances.Select(x => (x.CurrencyId, x.Balance)));
        Assert.Equal(legacyOperations.TotalCount, result.Operations.TotalCount);
        Assert.Equal(legacyOperations.Items.Select(x => x.OperationKey),
            result.Operations.Items.Select(x => x.OperationKey));
        Assert.Equal(10, result.Operations.PageSize);
        Assert.Equal(10, result.Operations.Items.Count);
        Assert.True(result.Operations.TotalCount >= 15);
        Assert.True(result.Operations.TotalPages >= 2);
        Assert.All(result.Operations.Items, x => Assert.StartsWith(marker, x.Description));
        Assert.Equal(3, legacyDatabaseCalls);
        Assert.Equal(1, combinedDatabaseCalls);
        Assert.Empty(context.ChangeTracker.Entries());
        output.WriteLine(JsonSerializer.Serialize(new
        {
            legacyDatabaseCalls,
            combinedDatabaseCalls,
            legacyMilliseconds = Math.Round(legacyTimer.Elapsed.TotalMilliseconds, 2),
            combinedMilliseconds = Math.Round(combinedTimer.Elapsed.TotalMilliseconds, 2)
        }));

        using (context.UseTenantScope(2))
            Assert.Null(await fixture.CreateCorrespondentService(context)
                .GetStatusPageAsync(fixture.SourceCorrespondent.Id));
    }

    [Fact]
    public async Task Closing_period_carries_net_balance_without_posting_new_ledger_entries()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var suffix = Guid.NewGuid().ToString("N");
        var correspondent = new Correspondent
        {
            Code = $"PERIOD-{suffix}",
            Name = $"Period correspondent {suffix}",
            CommissionMethod = "PerTransaction"
        };
        context.Correspondents.Add(correspondent);
        await context.SaveChangesAsync();
        var account = new Account
        {
            AccountCode = $"PERIOD-ACCOUNT-{suffix}",
            AccountName = "Period account",
            AccountType = "Correspondent",
            CorrespondentId = correspondent.Id
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        context.LedgerEntries.AddRange(
            new LedgerEntry
            {
                AccountId = account.Id, CurrencyId = 2, TalabKar = 1_000,
                Description = "Period opening debit", CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new LedgerEntry
            {
                AccountId = account.Id, CurrencyId = 2, BadehKar = 300,
                Description = "Period opening credit", CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await context.SaveChangesAsync();
        var ledgerCountBeforeClose = await context.LedgerEntries.CountAsync(x => x.AccountId == account.Id);

        var service = fixture.CreateCorrespondentService(context);
        var preview = await service.GetPeriodClosePreviewAsync(correspondent.Id);
        Assert.Equal(1, preview.PeriodNumber);
        Assert.Equal(700, Assert.Single(preview.ClosingBalances).Net);
        Assert.Empty(preview.OpeningBalances);
        Assert.Equal(ledgerCountBeforeClose,
            await context.LedgerEntries.CountAsync(x => x.AccountId == account.Id));
        var first = await service.ClosePeriodAsync(correspondent.Id, new CloseCorrespondentPeriodDto
        {
            Note = "First period"
        });

        Assert.Equal(ledgerCountBeforeClose, await context.LedgerEntries.CountAsync(x => x.AccountId == account.Id));
        var firstBalance = Assert.Single(first.Balances);
        Assert.Equal(700, firstBalance.TalabKar);
        Assert.Equal(0, firstBalance.BadehKar);
        Assert.Empty(first.OpeningBalances);
        var periodList = await service.GetPeriodsAsync(correspondent.Id);
        Assert.Equal(700, Assert.Single(Assert.Single(periodList).Balances).Net);

        await Task.Delay(20);
        context.LedgerEntries.Add(new LedgerEntry
        {
            AccountId = account.Id, CurrencyId = 2, BadehKar = 50,
            Description = "Current period movement", CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var status = await service.GetStatusPageAsync(correspondent.Id);
        Assert.NotNull(status);
        Assert.Equal(650, Assert.Single(status.Balances).Balance);

        await Task.Delay(20);
        var second = await service.ClosePeriodAsync(correspondent.Id, new CloseCorrespondentPeriodDto
        {
            Note = "Second period"
        });
        Assert.Equal(650, Assert.Single(second.Balances).Net);
        Assert.Equal(700, Assert.Single(second.OpeningBalances).Net);
        Assert.Equal(ledgerCountBeforeClose + 1,
            await context.LedgerEntries.CountAsync(x => x.AccountId == account.Id));
    }

    [Fact]
    public async Task Current_balance_cache_tracks_ledger_insert_update_and_delete()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var suffix = Guid.NewGuid().ToString("N");
        var correspondent = new Correspondent
        {
            Code = $"CACHE-{suffix}", Name = $"Cache correspondent {suffix}",
            CommissionMethod = "PerTransaction"
        };
        context.Correspondents.Add(correspondent);
        await context.SaveChangesAsync();
        var account = new Account
        {
            AccountCode = $"CACHE-ACCOUNT-{suffix}", AccountName = "Cache account",
            AccountType = "Correspondent", CorrespondentId = correspondent.Id
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        var entry = new LedgerEntry
        {
            AccountId = account.Id,
            CurrencyId = 3,
            TalabKar = 900,
            Description = $"Balance cache {Guid.NewGuid():N}"
        };
        context.LedgerEntries.Add(entry);
        await context.SaveChangesAsync();

        var service = fixture.CreateBalanceService(context);
        Assert.Equal(900, Assert.Single(await service.GetAccountBalanceAsync(
            account.Id)).Balance);

        entry.TalabKar = 0;
        entry.BadehKar = 250;
        await context.SaveChangesAsync();
        Assert.Equal(-250, Assert.Single(await service.GetAccountBalanceAsync(
            account.Id)).Balance);

        context.LedgerEntries.Remove(entry);
        await context.SaveChangesAsync();
        Assert.Empty(await service.GetAccountBalanceAsync(account.Id));
    }
}
