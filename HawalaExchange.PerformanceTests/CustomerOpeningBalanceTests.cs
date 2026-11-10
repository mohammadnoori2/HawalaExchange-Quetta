using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class CustomerOpeningBalanceTests(SqlServerPerformanceFixture fixture)
{
    private CustomerService Service(HawalaExchange.Infrastructure.Data.ApplicationDbContext db)
        => new(db, new MapperConfiguration(x => x.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper(),
            fixture.CreateAccountService(db), Mock.Of<ILedgerService>(), Mock.Of<IAuditLogService>(),
            Mock.Of<IAccountBadehkarLimitService>(), NullLogger<CustomerService>.Instance);

    [Fact]
    public async Task Creation_uses_equity_and_edit_delete_preserve_journals_without_touching_cash_or_hawalas()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        Assert.False(db.Database.HasPendingModelChanges());
        var service = Service(db); var hawalas = await db.Hawalas.CountAsync();
        var owner = await service.CreateAsync(new() { FullName = "Opening customer " + Guid.NewGuid(), OpenAccount = true, HasInitialBalance = true,
            InitialBalances = [new() { CurrencyId = 2, Amount = 250.5m, Direction = "Credit", OppositeAccountId = fixture.SourceAccount.Id }] });
        var usd = Assert.Single(await service.GetOpeningBalancesAsync(owner.Id));
        Assert.Equal(251, usd.Amount); Assert.Equal("Credit", usd.Direction);
        var account = await db.Accounts.SingleAsync(x => x.CustomerId == owner.Id);
        var original = await db.LedgerEntries.Include(x => x.Account).Where(x => x.TransactionId == usd.TransactionId).ToListAsync();
        Assert.Equal(2, original.Count); Assert.Contains(original, x => x.Account!.AccountCode == "5901" && x.BadehKar == 251);
        Assert.DoesNotContain(original, x => x.AccountId == fixture.SourceAccount.Id);
        await service.AddOpeningBalanceAsync(owner.Id, new() { CurrencyId = 1, Amount = 65000, Direction = "Debit" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddOpeningBalanceAsync(owner.Id, new() { CurrencyId = 2, Amount = 10 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddOpeningBalanceAsync(owner.Id, new() { CurrencyId = 1, Amount = -1 }));
        await service.UpdateOpeningBalanceAsync(owner.Id, usd.Id, new() { CurrencyId = 2, Amount = 1200, Direction = "Debit", Description = "Corrected" });
        var rows = await service.GetOpeningBalancesAsync(owner.Id);
        Assert.Equal("Edited", rows.Single(x => x.Id == usd.Id).State);
        var replacement = rows.Single(x => x.CurrencyId == 2 && x.State == "Active");
        Assert.Equal(1200, await db.LedgerEntries.Where(x => x.AccountId == account.Id && x.CurrencyId == 2).SumAsync(x => x.BadehKar - x.TalabKar));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateOpeningBalanceAsync(owner.Id, usd.Id, new() { CurrencyId = 2, Amount = 1 }));
        await service.DeleteOpeningBalanceAsync(owner.Id, replacement.Id);
        var count = await db.LedgerEntries.CountAsync(); await service.DeleteOpeningBalanceAsync(owner.Id, replacement.Id);
        Assert.Equal(count, await db.LedgerEntries.CountAsync());
        Assert.Equal(0, await db.LedgerEntries.Where(x => x.AccountId == account.Id && x.CurrencyId == 2).SumAsync(x => x.BadehKar - x.TalabKar));
        Assert.Equal(65000, await db.LedgerEntries.Where(x => x.AccountId == account.Id && x.CurrencyId == 1).SumAsync(x => x.BadehKar - x.TalabKar));
        Assert.Equal(2, await db.LedgerEntries.CountAsync(x => x.TransactionId == usd.TransactionId));
        Assert.Equal("Paid", (await db.Transactions.FindAsync(usd.TransactionId))!.Status);
        Assert.Equal(hawalas, await db.Hawalas.CountAsync());
        foreach (var id in await db.LedgerEntries.Where(x => x.AccountId == account.Id).Select(x => x.TransactionId).Distinct().ToListAsync())
        {
            var entries = await db.LedgerEntries.Include(x => x.Account).Where(x => x.TransactionId == id).ToListAsync();
            Assert.Equal(entries.Sum(x => x.BadehKar), entries.Sum(x => x.TalabKar));
            Assert.All(entries, x => Assert.True(x.AccountId == account.Id || x.Account!.AccountCode == "5901"));
        }
        var generic = new TransactionService(db, Mock.Of<IMapper>(), Mock.Of<ILedgerService>(), Mock.Of<IAccountService>(),
            Mock.Of<IExchangeRateService>(), Mock.Of<IAuditLogService>(), Mock.Of<ISubscriptionAccessService>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.UpdateAsync(usd.TransactionId, new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.DeleteAsync(usd.TransactionId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.CancelTransactionAsync(usd.TransactionId, new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generic.ReverseTransactionAsync(usd.TransactionId, new()));
        using (db.UseTenantScope(999999))
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOpeningBalancesAsync(owner.Id));
    }

    [Fact]
    public async Task Legacy_opening_reverses_exact_old_counterparty_and_keeps_other_currencies()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var service = Service(db);
        var owner = await service.CreateAsync(new() { FullName = "Legacy " + Guid.NewGuid(), OpenAccount = true });
        var account = await db.Accounts.SingleAsync(x => x.CustomerId == owner.Id);
        var tx = new Transaction { TransactionNo = "OP-CUST-" + Guid.NewGuid(), TransactionType = "OpeningBalance", BranchId = 1, CreatedBy = fixture.UserId, Status = "Paid" };
        db.Add(tx); await db.SaveChangesAsync();
        var usd = new LedgerEntry { TransactionId = tx.Id, AccountId = account.Id, CurrencyId = 2, BadehKar = 300, Description = "موجودی اولیه مشتری: یادداشت" };
        db.AddRange(usd, new LedgerEntry { TransactionId = tx.Id, AccountId = fixture.SourceAccount.Id, CurrencyId = 2, TalabKar = 300, Description = "طرف مقابل موجودی اولیه مشتری" },
            new LedgerEntry { TransactionId = tx.Id, AccountId = account.Id, CurrencyId = 1, TalabKar = 10000 },
            new LedgerEntry { TransactionId = tx.Id, AccountId = fixture.SourceAccount.Id, CurrencyId = 1, BadehKar = 10000 });
        await db.SaveChangesAsync();
        var old = (await service.GetOpeningBalancesAsync(owner.Id)).Single(x => x.Id == usd.Id);
        Assert.True(old.HasLegacyCounterparty); Assert.Equal("یادداشت", old.Description);
        await service.DeleteOpeningBalanceAsync(owner.Id, usd.Id);
        var rows = await service.GetOpeningBalancesAsync(owner.Id);
        Assert.Equal("Deleted", rows.Single(x => x.CurrencyId == 2).State);
        Assert.Equal("Active", rows.Single(x => x.CurrencyId == 1).State);
        var reversal = await db.CustomerOpeningBalanceChanges.SingleAsync(x => x.OriginalLedgerEntryId == usd.Id);
        Assert.True(await db.LedgerEntries.AnyAsync(x => x.TransactionId == reversal.ReversalTransactionId && x.AccountId == fixture.SourceAccount.Id && x.CurrencyId == 2 && x.BadehKar == 300));
        Assert.Equal(4, await db.LedgerEntries.CountAsync(x => x.TransactionId == tx.Id));
        var other = await service.CreateAsync(new() { FullName = "Other " + Guid.NewGuid(), OpenAccount = true });
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteOpeningBalanceAsync(other.Id, usd.Id));
    }

    [Fact]
    public async Task Missing_accounts_archived_customers_and_invalid_creation_do_not_post_opening_entries()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var service = Service(db);
        var receiver = await service.CreateAsync(new() { FullName = "Receiver " + Guid.NewGuid() });
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AddOpeningBalanceAsync(receiver.Id, new() { CurrencyId = 2, Amount = 100 }));
        var owner = await service.CreateAsync(new() { FullName = "Archived " + Guid.NewGuid(), OpenAccount = true });
        await service.ArchiveAsync(owner.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddOpeningBalanceAsync(owner.Id, new() { CurrencyId = 2, Amount = 100 }));
        var before = await db.Customers.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new() { FullName = "Invalid " + Guid.NewGuid(), OpenAccount = true, HasInitialBalance = true,
            InitialBalances = [new() { CurrencyId = 2, Amount = 0.1m }] }));
        Assert.Equal(before, await db.Customers.CountAsync());
        var retry = await service.CreateAsync(new() { FullName = "Retry " + Guid.NewGuid(), OpenAccount = true, HasInitialBalance = true,
            InitialBalances = [new() { CurrencyId = 2, Amount = 25, Direction = "Credit" }] });
        Assert.Equal(25, Assert.Single(await service.GetOpeningBalancesAsync(retry.Id)).Amount);
    }
}
