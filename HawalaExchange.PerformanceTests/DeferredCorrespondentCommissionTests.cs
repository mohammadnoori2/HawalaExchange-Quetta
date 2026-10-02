using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class DeferredCorrespondentCommissionTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Three_commissions_wait_for_their_own_correspondents_close_without_reposting_journals()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var data = await SeedAsync(context);
        var commission = fixture.CreateCommissionService(context);
        await commission.PostAsync(Request(data, "Incoming"));
        await commission.PostAsync(Request(data, "Forwarding"));
        await commission.PostAsync(Request(data, "Destination"));
        context.ChangeTracker.Clear();
        var balanceService = fixture.CreateBalanceService(context);
        var periods = fixture.CreateCorrespondentService(context);
        var source = Assert.Single(await balanceService.GetAccountBalanceAsync(data.SourceAccountId));
        Assert.Equal(1_000m, source.Balance);
        Assert.Equal(660m, source.PendingCommissionDebit);
        Assert.Equal(0m, source.PendingCommissionCredit);
        Assert.Equal(340m, source.TotalIncludingCommission);
        var destination = (await balanceService.GetAccountBalanceAsync(data.DestinationAccountId)).ToList();
        Assert.Equal(-5_000m, destination.Single(x => x.CurrencyId == 1).Balance);
        Assert.Equal(700m, destination.Single(x => x.CurrencyId == 1).PendingCommissionCredit);
        Assert.Equal(-100m, destination.Single(x => x.CurrencyId == 2).Balance);
        Assert.Equal(50m, destination.Single(x => x.CurrencyId == 2).PendingCommissionCredit);
        var status = await periods.GetStatusPageAsync(data.SourceId);
        Assert.Equal(source.Balance, Assert.Single(status!.Balances).Balance);
        Assert.Equal(source.PendingCommissionDebit, status.Balances.Single().PendingCommissionDebit);
        var accountBalances = (await fixture.CreateAccountService(context).GetAllAccountBalancesAsync(data.SourceAccountId)).ToList();
        Assert.Equal(source.Balance, Assert.Single(accountBalances).Balance);
        var count = await context.LedgerEntries.CountAsync();
        var beforeClose = DateTime.UtcNow;
        var preview = await periods.GetPeriodClosePreviewAsync(data.SourceId);
        Assert.Equal(340m, Assert.Single(preview.ClosingBalances).Net);
        // Preview does not recognize anything.
        Assert.Equal(660m, Assert.Single(await balanceService.GetAccountBalanceAsync(data.SourceAccountId)).PendingCommissionDebit);
        var closed = await periods.ClosePeriodAsync(data.SourceId, new() { Note = "Recognize source only" });
        Assert.Equal(340m, Assert.Single(closed.Balances).Net);
        var after = Assert.Single(await balanceService.GetAccountBalanceAsync(data.SourceAccountId));
        Assert.Equal(340m, after.Balance);
        Assert.Equal(0m, after.PendingCommissionDebit);
        Assert.Equal(count, await context.LedgerEntries.CountAsync());
        Assert.Equal(700m, (await balanceService.GetAccountBalanceAsync(data.DestinationAccountId)).Single(x => x.CurrencyId == 1).PendingCommissionCredit);
        var historical = Assert.Single(await balanceService.GetAccountBalanceAsync(data.SourceAccountId, beforeClose));
        Assert.Equal(1_000m, historical.Balance);
        Assert.Equal(660m, historical.PendingCommissionDebit);
        await periods.ClosePeriodAsync(data.SourceId, new());
        Assert.Equal(340m, Assert.Single(await balanceService.GetAccountBalanceAsync(data.SourceAccountId)).Balance);
        await periods.ClosePeriodAsync(data.DestinationId, new());
        var settledDestination = (await balanceService.GetAccountBalanceAsync(data.DestinationAccountId)).ToList();
        Assert.Equal(-4_300m, settledDestination.Single(x => x.CurrencyId == 1).Balance);
        Assert.Equal(-50m, settledDestination.Single(x => x.CurrencyId == 2).Balance);
        Assert.All(settledDestination, x => Assert.Equal(0m, x.PendingCommissionCredit));
        Assert.Equal(count, await context.LedgerEntries.CountAsync());
        using (context.UseTenantScope(2))
            Assert.Empty(await balanceService.GetAccountBalanceAsync(data.SourceAccountId));
    }

    [Fact]
    public async Task Commission_calculated_after_close_is_pending_even_for_an_old_hawala_day()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var data = await SeedAsync(context);
        var service = fixture.CreateCorrespondentService(context);
        await service.ClosePeriodAsync(data.SourceId, new());
        await fixture.CreateCommissionService(context).PostAsync(Request(data, "Incoming"));
        var balances = fixture.CreateBalanceService(context);
        var pending = Assert.Single(await balances.GetAccountBalanceAsync(data.SourceAccountId));
        Assert.Equal(1_000m, pending.Balance);
        Assert.Equal(440m, pending.PendingCommissionDebit);
        await service.ClosePeriodAsync(data.SourceId, new());
        var recognized = Assert.Single(await balances.GetAccountBalanceAsync(data.SourceAccountId));
        Assert.Equal(560m, recognized.Balance);
        Assert.Equal(0m, recognized.PendingCommissionDebit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reversal_before_close_cancels_pending_fee_and_after_close_waits_until_next_close(bool closeFirst)
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var data = await SeedAsync(context);
        var commissions = fixture.CreateCommissionService(context);
        var batch = await commissions.PostAsync(Request(data, "Incoming"));
        var periods = fixture.CreateCorrespondentService(context);
        if (closeFirst) await periods.ClosePeriodAsync(data.SourceId, new());
        await commissions.ReverseAsync(batch.Id, "Check deferred reversal");
        var balances = fixture.CreateBalanceService(context);
        var current = Assert.Single(await balances.GetAccountBalanceAsync(data.SourceAccountId));
        Assert.Equal(closeFirst ? 560m : 1_000m, current.Balance);
        Assert.Equal(0m, current.PendingCommissionDebit);
        Assert.Equal(closeFirst ? 440m : 0m, current.PendingCommissionCredit);
        Assert.Equal(1_000m, current.TotalIncludingCommission);
        var count = await context.LedgerEntries.CountAsync();
        await periods.ClosePeriodAsync(data.SourceId, new());
        Assert.Equal(1_000m, Assert.Single(await balances.GetAccountBalanceAsync(data.SourceAccountId)).Balance);
        Assert.Equal(count, await context.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Currency_conversion_leaves_pending_commission_in_original_currency_until_close()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var data = await SeedAsync(context);
        await fixture.CreateCommissionService(context).PostAsync(Request(data, "Destination"));
        var conversions = fixture.CreateSettlementService(context);
        var preview = await conversions.GetPreviewAsync(data.DestinationId);
        Assert.Equal(5_000m, Assert.Single(preview.Balances).BadehKar);
        await conversions.ConvertBalanceAsync(new()
        {
            CorrespondentId = data.DestinationId,
            Rates = [new() { SourceCurrencyId = 1, Rate = 70m }]
        });
        var balances = fixture.CreateBalanceService(context);
        var afterConversion = (await balances.GetAccountBalanceAsync(data.DestinationAccountId)).ToList();
        var afn = afterConversion.Single(x => x.CurrencyId == 1);
        Assert.Equal(0m, afn.Balance);
        Assert.Equal(700m, afn.PendingCommissionCredit);
        Assert.Equal(700m, afn.TotalIncludingCommission);
        Assert.Empty((await conversions.GetPreviewAsync(data.DestinationId)).Balances);
        await fixture.CreateCorrespondentService(context).ClosePeriodAsync(data.DestinationId, new());
        var afterClose = (await balances.GetAccountBalanceAsync(data.DestinationAccountId)).ToList();
        Assert.Equal(700m, afterClose.Single(x => x.CurrencyId == 1).Balance);
        Assert.All(afterClose, x => Assert.Equal(0m, x.PendingCommissionCredit));
    }

    [Fact]
    public async Task Concurrent_close_and_post_are_serialized_and_never_partially_recognize_a_commission()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var data = await SeedAsync(context);
        var postTask = Task.Run(async () =>
        {
            await using var worker = fixture.CreateContext();
            using var workerBypass = worker.BypassSubscriptionEnforcement();
            return await fixture.CreateCommissionService(worker).PostAsync(Request(data, "Incoming"));
        });
        var closeTask = Task.Run(async () =>
        {
            await using var worker = fixture.CreateContext();
            using var workerBypass = worker.BypassSubscriptionEnforcement();
            return await fixture.CreateCorrespondentService(worker).ClosePeriodAsync(data.SourceId, new());
        });
        await Task.WhenAll(postTask, closeTask);
        var balance = Assert.Single(await fixture.CreateBalanceService(context).GetAccountBalanceAsync(data.SourceAccountId));
        Assert.True(balance.PendingCommissionDebit == 0m || balance.PendingCommissionDebit == 440m);
        Assert.Equal(balance.PendingCommissionDebit == 0m ? 560m : 1_000m, balance.Balance);
        Assert.Equal(560m, balance.TotalIncludingCommission);
        Assert.Equal(balance.Balance, Assert.Single(closeTask.Result.Balances).Net);
    }

    [Fact]
    public async Task Pending_only_currency_is_visible_when_original_ledger_net_is_zero()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var data = await SeedAsync(context);
        await fixture.CreateCommissionService(context).PostAsync(Request(data, "Incoming"));
        context.LedgerEntries.Add(new() { AccountId = data.SourceAccountId, CurrencyId = 2, BadehKar = 560m });
        await context.SaveChangesAsync();
        var balances = fixture.CreateBalanceService(context);
        var current = Assert.Single(await balances.GetAccountBalanceAsync(data.SourceAccountId));
        Assert.Equal(440m, current.Balance);
        Assert.Equal(440m, current.PendingCommissionDebit);
        Assert.Equal(0m, current.TotalIncludingCommission);
        var status = await fixture.CreateCorrespondentService(context).GetStatusPageAsync(data.SourceId);
        Assert.Equal(440m, Assert.Single(status!.Balances).Balance);
        await fixture.CreateCorrespondentService(context).ClosePeriodAsync(data.SourceId, new());
        Assert.Empty(await balances.GetAccountBalanceAsync(data.SourceAccountId));
    }

    private CorrespondentCommissionPreviewRequestDto Request(TestData data, string scope) => new()
    {
        CorrespondentId = scope == "Destination" ? data.DestinationId : data.SourceId,
        PeriodFrom = data.Day, PeriodTo = data.Day,
        HawalaType = scope == "Incoming" ? "HawalaReceive" : "HawalaSend", CommissionScope = scope, CommissionPerLakhAfn = 400m,
        PaymentLocationRates = scope == "Forwarding" ? [new() { PaymentLocationId = fixture.RemoteLocation.Id, PerLakhRate = 400m }] : [],
        LocationCurrencyRates = scope == "Destination" ?
            [new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 1, PerLakhRate = 200m },
             new() { PaymentLocationId = fixture.RemoteLocation.Id, CurrencyId = 2, PerLakhRate = 100m }] : []
    };

    private async Task<TestData> SeedAsync(ApplicationDbContext context)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var source = new Correspondent { Code = $"DEF-S-{suffix}", Name = "Deferred source", CommissionMethod = "PeriodicPerLakh", SettlementCurrencyId = 2 };
        var destination = new Correspondent { Code = $"DEF-D-{suffix}", Name = "Deferred destination", SettlementCurrencyId = 2 };
        context.Correspondents.AddRange(source, destination);
        await context.SaveChangesAsync();
        var sourceAccount = new Account { AccountCode = $"DEF-S-{suffix}", AccountName = source.Name, AccountType = "Correspondent", CorrespondentId = source.Id };
        var destinationAccount = new Account { AccountCode = $"DEF-D-{suffix}", AccountName = destination.Name, AccountType = "Correspondent", CorrespondentId = destination.Id };
        context.Accounts.AddRange(sourceAccount, destinationAccount);
        var setting = await context.CompanySettings.FirstOrDefaultAsync();
        if (setting == null) { setting = new() { CompanyName = "Deferred tests" }; context.CompanySettings.Add(setting); }
        setting.OwnPaymentLocationId = fixture.OwnLocation.Id;
        await context.SaveChangesAsync();
        var now = DateTime.UtcNow.AddMinutes(-10);
        var day = now.ToLocalTime().Date;
        var first = new Hawala { Number = 1, HawalaType = "HawalaReceive", CorrespondentId = source.Id, FromCurrencyId = 2, ToCurrencyId = 2,
            FromAmount = 100_000m, ToAmount = 100_000m, PaymentLocationId = fixture.OwnLocation.Id, CreatedAt = now, CreatedBy = fixture.UserId, Status = "Paid" };
        var second = new Hawala { Number = 2, HawalaType = "HawalaReceive", CorrespondentId = source.Id, FromCurrencyId = 1, ToCurrencyId = 1,
            FromAmount = 700_000m, ToAmount = 700_000m, PaymentLocationId = fixture.OwnLocation.Id, CreatedAt = now, CreatedBy = fixture.UserId, Status = "Paid" };
        context.Hawalas.AddRange(first, second);
        context.CorrespondentDailyCommissionRates.Add(new() { CorrespondentId = source.Id, RateDate = day, UsdToAfnRate = 70m, CreatedBy = fixture.UserId });
        context.LedgerEntries.AddRange(new LedgerEntry { AccountId = sourceAccount.Id, CurrencyId = 2, TalabKar = 1_000m, CreatedAt = now },
            new LedgerEntry { AccountId = destinationAccount.Id, CurrencyId = 1, BadehKar = 5_000m, CreatedAt = now },
            new LedgerEntry { AccountId = destinationAccount.Id, CurrencyId = 2, BadehKar = 100m, CreatedAt = now });
        await context.SaveChangesAsync();
        context.Hawalas.AddRange(new Hawala { Number = 1, HawalaType = "HawalaSend", CorrespondentId = destination.Id, SourceHawalaId = first.Id,
            FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 50_000m, ToAmount = 50_000m, PaymentLocationId = fixture.RemoteLocation.Id,
            CreatedAt = now, CreatedBy = fixture.UserId, Status = "Paid" },
            new Hawala { Number = 2, HawalaType = "HawalaSend", CorrespondentId = destination.Id, SourceHawalaId = second.Id,
            FromCurrencyId = 1, ToCurrencyId = 1, FromAmount = 350_000m, ToAmount = 350_000m, PaymentLocationId = fixture.RemoteLocation.Id,
            CreatedAt = now, CreatedBy = fixture.UserId, Status = "Paid" });
        await context.SaveChangesAsync();
        return new(source.Id, sourceAccount.Id, destination.Id, destinationAccount.Id, day);
    }

    private sealed record TestData(long SourceId, long SourceAccountId, long DestinationId, long DestinationAccountId, DateTime Day);
}
