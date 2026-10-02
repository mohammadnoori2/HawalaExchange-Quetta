using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

public sealed class OwnerWithdrawalTests(SqlServerPerformanceFixture fixture) : IClassFixture<SqlServerPerformanceFixture>
{
    [Fact]
    public async Task WithdrawalBalancesJournalAndReducesCashAndEquityNotProfit()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var setup = await SeedAsync(context);
        var report = fixture.CreateFinancialReportService(context);
        var before = await report.GetBalanceSheetAsync(DateTime.Today);
        var profitBefore = await report.GetProfitLossStatementAsync(DateTime.Today.AddDays(-3), DateTime.Today);
        var id = await setup.Service.CreateAsync(Request(setup, 200));
        var entries = await context.LedgerEntries.AsNoTracking().Where(x => x.CapitalInvestmentId == id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(200, entries.Single(x => x.AccountId == setup.Cash.Id).TalabKar);
        var drawing = entries.Single(x => x.AccountId != setup.Cash.Id);
        Assert.Equal(200, drawing.BadehKar);
        var account = await context.Accounts.SingleAsync(x => x.Id == drawing.AccountId);
        Assert.Equal("Equity", account.AccountType);
        Assert.StartsWith("OWNER-DRAW-", account.AccountCode);
        Assert.NotEqual(setup.Owner.Id, account.Id);
        Assert.Equal(entries.Sum(x => x.BadehKar), entries.Sum(x => x.TalabKar));
        Assert.Equal(800, await CashBalanceAsync(context, setup));
        var after = await report.GetBalanceSheetAsync(DateTime.Today);
        var profitAfter = await report.GetProfitLossStatementAsync(DateTime.Today.AddDays(-3), DateTime.Today);
        Assert.Equal(before.TotalEquity - 200, after.TotalEquity);
        Assert.Equal(profitBefore.NetProfit, profitAfter.NetProfit);
        Assert.Equal(before.UnrealizedExchangeAdjustment, after.UnrealizedExchangeAdjustment);
        Assert.True(after.IsBalanced);
        var journal = await fixture.CreateJournalService(context).GetJournalAsync(DateTime.Today.AddDays(-3), DateTime.Today);
        Assert.Contains(journal.Operations, x => x.SourceId == id && x.SourceType == "برداشت مالک");
    }

    [Fact]
    public async Task CancellationKeepsOriginalJournalAndRestoresFundsWithSeparateReversal()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var setup = await SeedAsync(context);
        var id = await setup.Service.CreateAsync(Request(setup, 200));
        var originals = await context.LedgerEntries.AsNoTracking().Where(x => x.CapitalInvestmentId == id)
            .Select(x => new { x.Id, x.AccountId, x.TalabKar, x.BadehKar }).ToListAsync();
        await setup.Service.DeleteAsync(id);
        Assert.Equal(originals, await context.LedgerEntries.AsNoTracking().Where(x => x.CapitalInvestmentId == id)
            .Select(x => new { x.Id, x.AccountId, x.TalabKar, x.BadehKar }).ToListAsync());
        var reversal = await context.Transactions.SingleAsync(x => x.TransactionNo == $"OW-REV-{id}");
        Assert.Equal("OwnerWithdrawalReversal", reversal.TransactionType);
        var entries = await context.LedgerEntries.Where(x => x.TransactionId == reversal.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(200, entries.Single(x => x.AccountId == setup.Cash.Id).BadehKar);
        Assert.Equal(1000, await CashBalanceAsync(context, setup));
        Assert.NotNull((await setup.Service.GetByIdAsync(id))!.CancelledAt);
        Assert.Contains(await setup.Service.GetAllAsync(), x => x.Id == id && x.CancelledAt.HasValue);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.DeleteAsync(id));
        Assert.Equal(1, await context.Transactions.CountAsync(x => x.TransactionNo == $"OW-REV-{id}"));
    }

    [Fact]
    public async Task InsufficientFundsNegativeAmountAndFutureDateLeaveNoWithdrawalOrLedger()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var setup = await SeedAsync(context);
        var count = await context.CapitalInvestments.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateAsync(Request(setup, 1001)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateAsync(Request(setup, -1)));
        var future = Request(setup, 100); future.InvestmentDate = DateTime.UtcNow.AddDays(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateAsync(future));
        Assert.Equal(count, await context.CapitalInvestments.CountAsync());
        Assert.Equal(1000, await CashBalanceAsync(context, setup));
    }

    [Fact]
    public async Task BackdatedWithdrawalCannotMakeSubsequentCashBalanceNegative()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var setup = await SeedAsync(context);
        await setup.Service.CreateAsync(Request(setup, 900));
        var backdated = Request(setup, 200);
        backdated.InvestmentDate = DateTime.UtcNow.AddDays(-1.5);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateAsync(backdated));
        Assert.Equal(100, await CashBalanceAsync(context, setup));
    }

    [Fact]
    public async Task WithdrawalUsesMovingAverageCostAndCancellationRestoresInventoryWithoutProfit()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var currency = new Currency { Code = "W" + Guid.NewGuid().ToString("N")[..7], Name = "Withdrawal inventory test" };
        context.Currencies.Add(currency); await context.SaveChangesAsync();
        var setup = await SeedAsync(context, currency.Id, 70_000);
        var id = await setup.Service.CreateAsync(Request(setup, 200));
        var withdrawal = await setup.Service.GetByIdAsync(id);
        Assert.Equal(14_000, withdrawal!.ProfitCurrencyAmount);
        var costs = new CurrencyCostService(context);
        var position = Assert.Single((await costs.GetPositionsAsync()).Where(x => x.CurrencyId == currency.Id));
        Assert.Equal(800, position.Quantity);
        Assert.Equal(56_000, position.CarryingAmount);
        await setup.Service.DeleteAsync(id);
        position = Assert.Single((await costs.GetPositionsAsync()).Where(x => x.CurrencyId == currency.Id));
        Assert.Equal(1000, position.Quantity);
        Assert.Equal(70_000, position.CarryingAmount);
    }

    [Fact]
    public async Task WithdrawalsCannotBeEditedAndClosedCashDaysCannotBeChanged()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var setup = await SeedAsync(context);
        var id = await setup.Service.CreateAsync(Request(setup, 100));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.UpdateAsync(id, new UpdateCapitalInvestmentDto
        {
            Amount = 1, CurrencyId = setup.CurrencyId, ProfitCurrencyId = 1, ProfitCurrencyAmount = 1,
            ReceivingAccountId = setup.Cash.Id, CapitalAccountId = setup.Owner.Id, InvestmentDate = DateTime.UtcNow.AddDays(-1)
        }));
        context.CashDailyBalances.Add(new CashDailyBalance { AccountId = setup.Cash.Id, CurrencyId = setup.CurrencyId,
            JournalDate = DateTime.Today, OpeningBalance = 900, IsClosed = true });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateAsync(Request(setup, 100)));
        Assert.Equal(900, await CashBalanceAsync(context, setup));
    }

    private async Task<Setup> SeedAsync(ApplicationDbContext context, long currencyId = 1, decimal carryingAmount = 1000)
    {
        if (!await context.CompanySettings.AnyAsync())
        {
            context.CompanySettings.Add(new CompanySetting { CompanyName = "Owner withdrawal tests", DefaultProfitCurrencyId = 1,
                OwnPaymentLocationId = fixture.OwnLocation.Id });
        }
        var cash = new Account { AccountCode = "WC-" + Guid.NewGuid().ToString("N"), AccountName = "Withdrawal cash", AccountType = "Cash" };
        var owner = new Account { AccountCode = "WO-" + Guid.NewGuid().ToString("N"), AccountName = "Withdrawal owner", AccountType = "Equity" };
        context.Accounts.AddRange(cash, owner); await context.SaveChangesAsync();
        var mapper = new MapperConfiguration(config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var service = new CapitalInvestmentService(context, mapper, Mock.Of<IAuditLogService>(), new CurrencyCostService(context));
        await service.CreateAsync(new CreateCapitalInvestmentDto { Amount = 1000, CurrencyId = currencyId,
            ProfitCurrencyId = 1, ProfitCurrencyAmount = carryingAmount, ReceivingAccountId = cash.Id,
            CapitalAccountId = owner.Id, InvestmentDate = DateTime.UtcNow.AddDays(-2) });
        return new Setup(cash, owner, currencyId, service);
    }

    [Fact]
    public async Task FundingInvestmentCannotBeRemovedOrReducedBelowExistingWithdrawals()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var setup = await SeedAsync(context);
        var funding = await context.CapitalInvestments.SingleAsync(x => !x.IsWithdrawal && x.ReceivingAccountId == setup.Cash.Id);
        await setup.Service.CreateAsync(Request(setup, 200));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.DeleteAsync(funding.Id));
        Assert.Equal(800, await CashBalanceAsync(context, setup));
        Assert.False((await context.CapitalInvestments.AsNoTracking().SingleAsync(x => x.Id == funding.Id)).IsDeleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.UpdateAsync(funding.Id, new UpdateCapitalInvestmentDto
        {
            Amount = 100, CurrencyId = 1, ProfitCurrencyId = 1, ProfitCurrencyAmount = 100,
            ReceivingAccountId = setup.Cash.Id, CapitalAccountId = setup.Owner.Id, InvestmentDate = DateTime.UtcNow.AddDays(-2)
        }));
        Assert.Equal(800, await CashBalanceAsync(context, setup));
    }

    [Fact]
    public async Task CrossTenantAccountsAndNonEquityOwnerAccountsAreRejected()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var setup = await SeedAsync(context);
        var invalid = Request(setup, 100); invalid.CapitalAccountId = setup.Cash.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateAsync(invalid));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(context.Database.GetConnectionString()).Options;
        await using var otherContext = new ApplicationDbContext(options, new TestCurrentTenant { TenantId = 2, UserId = fixture.UserId });
        var mapper = new MapperConfiguration(config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var otherService = new CapitalInvestmentService(otherContext, mapper, Mock.Of<IAuditLogService>(), new CurrencyCostService(otherContext));
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherService.CreateAsync(Request(setup, 100)));
        Assert.Equal(1000, await CashBalanceAsync(context, setup));
    }

    private static CreateCapitalInvestmentDto Request(Setup setup, decimal amount) => new()
    {
        IsWithdrawal = true, Amount = amount, CurrencyId = setup.CurrencyId, ProfitCurrencyId = 1,
        ReceivingAccountId = setup.Cash.Id, CapitalAccountId = setup.Owner.Id,
        InvestmentDate = DateTime.UtcNow.AddDays(-1)
    };

    private static Task<decimal> CashBalanceAsync(ApplicationDbContext context, Setup setup) => context.LedgerEntries
        .Where(x => x.AccountId == setup.Cash.Id && x.CurrencyId == setup.CurrencyId).SumAsync(x => x.BadehKar - x.TalabKar);

    private sealed record Setup(Account Cash, Account Owner, long CurrencyId, CapitalInvestmentService Service);
}
