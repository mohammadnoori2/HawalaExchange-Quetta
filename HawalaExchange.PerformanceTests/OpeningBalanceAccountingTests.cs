using HawalaExchange.Application.DTOs;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class OpeningBalanceAccountingTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Creating_cash_with_opening_balance_creates_one_balanced_system_entry()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var afn = await context.Currencies.SingleAsync(x => x.Code == "AFN");
        var openingDate = new DateTime(2026, 9, 1);
        var service = fixture.CreateAccountService(context);

        var cash = await service.CreateAsync(new CreateAccountDto
        {
            AccountName = $"Opening cash {Guid.NewGuid():N}",
            AccountType = "Cash",
            HasInitialBalance = true,
            InitialBalanceDate = openingDate,
            InitialBalances =
            [
                new InitialBalanceDto
                {
                    CurrencyId = afn.Id,
                    Amount = 1_250_000m,
                    Direction = "Debit"
                }
            ]
        });

        var openingAccounts = await context.Accounts
            .Where(x => x.AccountCode == ApplicationDbContext.OpeningBalanceEquityAccountCode)
            .ToListAsync();
        var openingAccount = Assert.Single(openingAccounts);
        Assert.Equal("OpeningBalanceEquity", openingAccount.AccountType);

        var transaction = await context.Transactions
            .SingleAsync(x => x.TransactionType == "OpeningBalance" &&
                              x.Remarks == $"موجودی اولیه حساب {cash.AccountName}");
        var entries = await context.LedgerEntries
            .Where(x => x.TransactionId == transaction.Id)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal(entries.Sum(x => x.BadehKar), entries.Sum(x => x.TalabKar));
        Assert.Equal(1_250_000m, entries.Single(x => x.AccountId == cash.Id).BadehKar);
        Assert.Equal(1_250_000m, entries.Single(x => x.AccountId == openingAccount.Id).TalabKar);
        Assert.All(entries, x => Assert.Equal(openingDate, x.CreatedAt));

        var balanceSheet = await fixture.CreateFinancialReportService(context)
            .GetBalanceSheetAsync(openingDate);
        Assert.Equal(1_250_000m, balanceSheet.OpeningBalanceEquity);
        Assert.Contains(
            balanceSheet.LiabilityAndEquityLines,
            line => line.Description == "انتقال مانده افتتاحیه" && line.Amount == 1_250_000m);
        Assert.True(balanceSheet.IsBalanced);
    }

    [Fact]
    public async Task Existing_cash_can_receive_manual_opening_balance_only_once_per_currency()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var usd = await context.Currencies.SingleAsync(x => x.Code == "USD");
        var service = fixture.CreateAccountService(context);
        var cash = await service.CreateAsync(new CreateAccountDto
        {
            AccountName = $"Existing cash {Guid.NewGuid():N}",
            AccountType = "Cash"
        });
        var balances = new[]
        {
            new InitialBalanceDto
            {
                CurrencyId = usd.Id,
                Amount = 18_500m,
                Direction = "Debit"
            }
        };

        await service.AddOpeningBalancesAsync(cash.Id, new DateTime(2026, 9, 2), balances);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddOpeningBalancesAsync(cash.Id, new DateTime(2026, 9, 3), balances));
        Assert.Contains("قبلاً موجودی افتتاحیه", error.Message);
    }
}
