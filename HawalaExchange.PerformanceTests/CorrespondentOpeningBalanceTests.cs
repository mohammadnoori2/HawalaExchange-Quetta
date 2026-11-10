using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using HawalaExchange.Application.Services;
using HawalaExchange.Application.Interfaces.Services;
using AutoMapper;
using HawalaSystem.Mappings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class CorrespondentOpeningBalanceTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Creating_a_correspondent_uses_only_opening_equity_even_if_old_client_supplies_another_account()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var config = new MapperConfiguration(x => x.AddProfile<MappingProfile>(), NullLoggerFactory.Instance);
        var service = new CorrespondentService(db, config.CreateMapper(), fixture.CreateAccountService(db), Mock.Of<ILedgerService>(),
            Mock.Of<IAuditLogService>(), NullLogger<CorrespondentService>.Instance);
        var owner = await service.CreateAsync(new CreateCorrespondentDto { Name = "Opening create " + Guid.NewGuid(), HasInitialBalance = true,
            InitialBalances = [new() { CurrencyId = 2, Amount = 250, Direction = "Credit", OppositeAccountId = fixture.SourceAccount.Id }] });
        var row = Assert.Single(await service.GetOpeningBalancesAsync(owner.Id));
        Assert.Equal(250, row.Amount); Assert.Equal("Credit", row.Direction);
        var entries = await db.LedgerEntries.Include(x => x.Account).Where(x => x.TransactionId == row.TransactionId).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, x => x.Account!.AccountCode == "5901" && x.BadehKar == 250);
        Assert.DoesNotContain(entries, x => x.AccountId == fixture.SourceAccount.Id);
    }

    [Fact]
    public async Task Legacy_multi_currency_opening_is_corrected_per_currency_without_destroying_the_original_transaction()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var owner = new Correspondent { Code = "OLD-OB-" + Guid.NewGuid().ToString("N")[..8], Name = "Legacy opening" };
        db.Add(owner); await db.SaveChangesAsync();
        var account = new Account { AccountCode = owner.Code, AccountName = owner.Name, AccountType = "Correspondent", CorrespondentId = owner.Id };
        db.Add(account); await db.SaveChangesAsync(); await db.EnsureSystemAccountsAsync(db.CurrentTenantId);
        var equity = await db.Accounts.SingleAsync(x => x.AccountCode == "5901");
        var transaction = new Transaction { TransactionNo = "OP-legacy-" + Guid.NewGuid().ToString("N"), TransactionType = "OpeningBalance", BranchId = 1, CreatedBy = fixture.UserId, Status = "Paid" };
        db.Add(transaction); await db.SaveChangesAsync();
        var usd = new LedgerEntry { TransactionId = transaction.Id, AccountId = account.Id, CurrencyId = 2, BadehKar = 125 };
        db.LedgerEntries.AddRange(usd,
            new() { TransactionId = transaction.Id, AccountId = equity.Id, CurrencyId = 2, TalabKar = 125 },
            new() { TransactionId = transaction.Id, AccountId = account.Id, CurrencyId = 1, TalabKar = 12000 },
            new() { TransactionId = transaction.Id, AccountId = equity.Id, CurrencyId = 1, BadehKar = 12000 });
        await db.SaveChangesAsync();
        var service = fixture.CreateCorrespondentService(db);
        await service.DeleteOpeningBalanceAsync(owner.Id, usd.Id);
        Assert.Equal("Paid", (await db.Transactions.FindAsync(transaction.Id))!.Status);
        Assert.Equal(4, await db.LedgerEntries.CountAsync(x => x.TransactionId == transaction.Id));
        var history = await service.GetOpeningBalancesAsync(owner.Id);
        Assert.Equal("Deleted", history.Single(x => x.CurrencyId == 2).State);
        Assert.Equal("Active", history.Single(x => x.CurrencyId == 1).State);
        Assert.Equal(-12000, await db.LedgerEntries.Where(x => x.AccountId == account.Id && x.CurrencyId == 1).SumAsync(x => x.BadehKar - x.TalabKar));
        var generic = new TransactionService(db, Mock.Of<IMapper>(), Mock.Of<ILedgerService>(), Mock.Of<IAccountService>(),
            Mock.Of<IExchangeRateService>(), Mock.Of<IAuditLogService>(), Mock.Of<ISubscriptionAccessService>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.UpdateAsync(transaction.Id, new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.DeleteAsync(transaction.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.CancelTransactionAsync(transaction.Id, new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.ReverseTransactionAsync(transaction.Id, new()));
    }

    [Fact]
    public async Task Opening_edit_and_delete_preserve_journals_other_activity_and_never_create_commission()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var correspondent = new Correspondent { Code = "OB-" + Guid.NewGuid().ToString("N")[..10], Name = "Opening test", CommissionMethod = "PeriodicPerLakh", SettlementCurrencyId = 2 };
        db.Correspondents.Add(correspondent); await db.SaveChangesAsync();
        var account = new Account { AccountCode = correspondent.Code, AccountName = correspondent.Name, AccountType = "Correspondent", CorrespondentId = correspondent.Id };
        db.Accounts.Add(account); await db.SaveChangesAsync();
        var service = fixture.CreateCorrespondentService(db);
        var hawalaCount = await db.Hawalas.CountAsync();
        await service.AddOpeningBalanceAsync(correspondent.Id, new() { CurrencyId = 2, Amount = 1000, Direction = "Debit", Description = "Prior debt" });
        await service.AddOpeningBalanceAsync(correspondent.Id, new() { CurrencyId = 1, Amount = 65000, Direction = "Credit" });
        Assert.Equal(hawalaCount, await db.Hawalas.CountAsync());
        var entries = await service.GetOpeningBalancesAsync(correspondent.Id);
        Assert.Equal(2, entries.Count);
        var usd = entries.Single(x => x.CurrencyId == 2);
        var originalIds = await db.LedgerEntries.Where(x => x.TransactionId == usd.TransactionId).Select(x => x.Id).ToArrayAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddOpeningBalanceAsync(correspondent.Id, new() { CurrencyId = 2, Amount = 5 }));
        var old = DateTime.UtcNow.AddDays(-2);
        await db.LedgerEntries.Where(x => x.TransactionId == usd.TransactionId).ExecuteUpdateAsync(x => x.SetProperty(e => e.CreatedAt, old));
        var period = new CorrespondentAccountPeriod { CorrespondentId = correspondent.Id, PeriodNumber = 1, PeriodFrom = old.AddDays(-1), PeriodTo = old.AddDays(1), ClosedAt = old.AddDays(1), ClosedBy = fixture.UserId,
            Balances = [new() { CurrencyId = 2, BadehKar = 1000 }] };
        db.CorrespondentAccountPeriods.Add(period); await db.SaveChangesAsync();
        await service.UpdateOpeningBalanceAsync(correspondent.Id, usd.Id, new() { CurrencyId = 2, Amount = 1200, Direction = "Credit", OpeningDate = old.Date, Description = "Corrected balance" });
        entries = await service.GetOpeningBalancesAsync(correspondent.Id);
        Assert.Equal("Edited", entries.Single(x => x.Id == usd.Id).State);
        var replacement = entries.Single(x => x.CurrencyId == 2 && x.State == "Active");
        Assert.True(replacement.Date > period.PeriodTo);
        Assert.Equal(-1200, await db.LedgerEntries.Where(x => x.AccountId == account.Id && x.CurrencyId == 2).SumAsync(x => x.BadehKar - x.TalabKar));
        Assert.Equal(1000, (await db.CorrespondentAccountPeriodBalances.SingleAsync(x => x.PeriodId == period.Id)).BadehKar);
        Assert.Equal(2, await db.LedgerEntries.CountAsync(x => originalIds.Contains(x.Id)));
        var nextCommission = await fixture.CreateCommissionService(db).PreviewAsync(new() { CorrespondentId = correspondent.Id, CommissionScope = "Incoming", HawalaType = "HawalaReceive", AllUncalculated = true, CommissionPerLakhAfn = 400 });
        Assert.Empty(nextCommission.Items);
        await service.DeleteOpeningBalanceAsync(correspondent.Id, replacement.Id, "Remove corrected opening");
        var count = await db.LedgerEntries.CountAsync();
        await service.DeleteOpeningBalanceAsync(correspondent.Id, replacement.Id);
        Assert.Equal(count, await db.LedgerEntries.CountAsync());
        Assert.Equal(0, await db.LedgerEntries.Where(x => x.AccountId == account.Id && x.CurrencyId == 2).SumAsync(x => x.BadehKar - x.TalabKar));
        Assert.Equal(-65000, await db.LedgerEntries.Where(x => x.AccountId == account.Id && x.CurrencyId == 1).SumAsync(x => x.BadehKar - x.TalabKar));
        foreach (var transaction in await db.LedgerEntries.Where(x => x.AccountId == account.Id).Select(x => x.TransactionId).Distinct().ToListAsync())
        {
            var rows = await db.LedgerEntries.Where(x => x.TransactionId == transaction).ToListAsync();
            Assert.Equal(rows.Sum(x => x.BadehKar), rows.Sum(x => x.TalabKar));
            Assert.All(rows, x => Assert.True(x.AccountId == account.Id || db.Accounts.Any(a => a.Id == x.AccountId && a.AccountCode == "5901")));
        }
        using (db.UseTenantScope(999999))
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOpeningBalancesAsync(correspondent.Id));
    }
}
