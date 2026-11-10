using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class EditAgentCommissionTests : IAsyncLifetime
{
    private readonly SqlServerPerformanceFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<long> CreateSentAsync(HawalaExchange.Infrastructure.Data.ApplicationDbContext db)
    {
        var received = await fixture.CreateService(db).CreateHawalasAsync([new CreateHawalaDto {
            HawalaType = "HawalaReceive", Number = 987659, CorrespondentId = fixture.SourceCorrespondent.Id,
            PaymentLocationId = fixture.OwnLocation.Id, FromAccountId = fixture.DestinationAccount.Id,
            FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 2070, ToAmount = 2070,
            SenderName = "Sender", ReceiverName = "Receiver", Status = "Paid",
            GeneratedSendAgentCommissionAmount = 12, GeneratedSendAgentCommissionCurrencyId = 2 }]);
        return await db.Hawalas.Where(x => x.SourceHawalaId == received[0].Id).Select(x => x.Id).SingleAsync();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Editing_generated_send_changes_only_its_commission_and_balances(long currency)
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var id = await CreateSentAsync(db);
        var sourceId = await db.Hawalas.Where(x => x.Id == id).Select(x => x.SourceHawalaId).SingleAsync();
        var originalIds = await db.LedgerEntries.Select(x => x.Id).ToArrayAsync();
        var result = await fixture.CreateService(db).UpdateAgentCommissionAsync(id,
            new UpdateHawalaAgentCommissionDto { Amount = 6, CurrencyId = currency, PreviousAmount = 12, PreviousCurrencyId = 2 });
        Assert.Equal(6, result.AgentCommissionAmount);
        Assert.Equal(currency, result.AgentCommissionCurrencyId);
        Assert.Equal(2070, result.FromAmount);
        Assert.Equal("Paid", result.Status);
        Assert.Equal(originalIds.Length, await db.LedgerEntries.CountAsync(x => originalIds.Contains(x.Id)));
        Assert.Null(await db.Hawalas.Where(x => x.Id == sourceId).Select(x => x.AgentCommissionAmount).SingleAsync());
        var entries = await db.LedgerEntries.Where(x => x.HawalaId == id).ToListAsync();
        Assert.All(entries.GroupBy(x => x.CurrencyId), group => Assert.Equal(0, group.Sum(x => x.TalabKar - x.BadehKar)));
        Assert.Equal(currency == 2 ? 2076 : 2070, entries.Where(x => x.AccountId == fixture.DestinationAccount.Id && x.CurrencyId == 2).Sum(x => x.TalabKar - x.BadehKar));
        if (currency == 1) Assert.Equal(6, entries.Where(x => x.AccountId == fixture.DestinationAccount.Id && x.CurrencyId == 1).Sum(x => x.TalabKar - x.BadehKar));
        var count = entries.Count;
        await fixture.CreateService(db).UpdateAgentCommissionAsync(id,
            new UpdateHawalaAgentCommissionDto { Amount = 6, CurrencyId = currency, PreviousAmount = 6, PreviousCurrencyId = currency });
        Assert.Equal(count, await db.LedgerEntries.CountAsync(x => x.HawalaId == id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService(db).UpdateAgentCommissionAsync(id,
            new UpdateHawalaAgentCommissionDto { Amount = 5, CurrencyId = 2, PreviousAmount = 12, PreviousCurrencyId = 2 }));
        Assert.Equal(count, await db.LedgerEntries.CountAsync(x => x.HawalaId == id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_send_keeps_customer_fee_and_original_commission_posting_account(bool addedLater)
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var service = fixture.CreateService(db);
        var cash = await db.Accounts.FirstAsync(x => x.AccountType == "Cash" && !x.IsArchived);
        var sent = await service.CreateHawalaAsync(new CreateHawalaDto {
            HawalaType = "HawalaSend", Number = 987660, CorrespondentId = fixture.DestinationCorrespondent.Id,
            FromAccountId = cash.Id, FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 2070, ToAmount = 2070,
            SenderName = "Sender", ReceiverName = "Receiver", Status = "Pending",
            CommissionAmount = 20, CommissionCurrencyId = 2,
            AgentCommissionAmount = addedLater ? null : 12, AgentCommissionCurrencyId = addedLater ? null : 2 });
        if (addedLater) await service.AddAgentCommissionAsync(sent.Id, new() { Amount = 12, CurrencyId = 2 });
        var account = await db.LedgerEntries.Where(x => x.HawalaId == sent.Id && x.BadehKar > 0 &&
            (x.Account.AccountType == "Income" || x.Account.AccountType == "Expense")).Select(x => x.AccountId).FirstAsync();
        var before = await db.LedgerEntries.Where(x => x.HawalaId == sent.Id && x.AccountId == account).SumAsync(x => x.BadehKar - x.TalabKar);
        await service.UpdateAgentCommissionAsync(sent.Id, new() { Amount = 6, CurrencyId = 2, PreviousAmount = 12, PreviousCurrencyId = 2 });
        Assert.Equal(before - 6, await db.LedgerEntries.Where(x => x.HawalaId == sent.Id && x.AccountId == account).SumAsync(x => x.BadehKar - x.TalabKar));
        Assert.Equal(2090, await db.LedgerEntries.Where(x => x.HawalaId == sent.Id && x.AccountId == cash.Id).SumAsync(x => x.BadehKar - x.TalabKar));
        var saved = await db.Hawalas.AsNoTracking().SingleAsync(x => x.Id == sent.Id);
        Assert.Equal(20, saved.CommissionAmount);
        Assert.Equal("Pending", saved.Status);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, 2)]
    [InlineData(6, 999999)]
    public async Task Invalid_values_do_not_change_commission(decimal amount, long currency)
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var id = await CreateSentAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService(db).UpdateAgentCommissionAsync(id,
            new UpdateHawalaAgentCommissionDto { Amount = amount, CurrencyId = currency, PreviousAmount = 12, PreviousCurrencyId = 2 }));
        Assert.Equal(12, await db.Hawalas.AsNoTracking().Where(x => x.Id == id).Select(x => x.AgentCommissionAmount).SingleAsync());
    }

    [Fact]
    public async Task Converted_send_is_reconverted_at_the_saved_rate_and_closed_period_is_protected()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var service = fixture.CreateService(db);
        var cash = await db.Accounts.FirstAsync(x => x.AccountType == "Cash" && !x.IsArchived);
        var sent = await service.CreateHawalaAsync(new CreateHawalaDto {
            HawalaType = "HawalaSend", Number = 987661, CorrespondentId = fixture.DestinationCorrespondent.Id,
            FromAccountId = cash.Id, FromCurrencyId = 1, ToCurrencyId = 1, FromAmount = 65000, ToAmount = 65000,
            SenderName = "Sender", ReceiverName = "Receiver", Status = "Paid",
            AgentCommissionAmount = 12, AgentCommissionCurrencyId = 1 });
        await fixture.CreateSettlementService(db).ConvertHawalasAsync(new() {
            CorrespondentId = fixture.DestinationCorrespondent.Id, HawalaIds = [sent.Id],
            HawalaRates = [new() { HawalaId = sent.Id, SourceCurrencyId = 1, Rate = 65 }] });
        await service.UpdateAgentCommissionAsync(sent.Id, new() { Amount = 6, CurrencyId = 1, PreviousAmount = 12, PreviousCurrencyId = 1 });
        var conversion = await db.CorrespondentSettlementConversionHawalaItems.AsNoTracking().SingleAsync(x => x.HawalaId == sent.Id);
        Assert.Equal(65, conversion.ExchangeRate);
        Assert.Equal(65006, conversion.SourceTalabKar - conversion.SourceBadehKar);
        await fixture.CreateCorrespondentService(db).ClosePeriodAsync(fixture.DestinationCorrespondent.Id, new());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAgentCommissionAsync(sent.Id,
            new() { Amount = 3, CurrencyId = 1, PreviousAmount = 6, PreviousCurrencyId = 1 }));
    }

    [Fact]
    public async Task Active_bill_and_foreign_tenant_cannot_edit_commission()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var id = await CreateSentAsync(db); var service = fixture.CreateService(db);
        var request = new AgentBillCreateDto { CorrespondentId = fixture.DestinationCorrespondent.Id, HawalaIds = [id], RecipientName = "Agent", ConfirmUnsettled = true };
        request.PreviewToken = (await service.PreviewAgentBillAsync(request)).ConfirmationToken;
        await service.CreateAgentBillAsync(request);
        var edit = new UpdateHawalaAgentCommissionDto { Amount = 6, CurrencyId = 2, PreviousAmount = 12, PreviousCurrencyId = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAgentCommissionAsync(id, edit));
        using (db.UseTenantScope(999999))
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAgentCommissionAsync(id, edit));
    }
}
