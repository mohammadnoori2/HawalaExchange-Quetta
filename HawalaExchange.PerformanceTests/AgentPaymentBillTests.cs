using HawalaExchange.Application.DTOs;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class AgentPaymentBillTests : IAsyncLifetime
{
    [Fact]
    public async Task Historical_receipt_never_changes_financial_entries_or_execution_and_cannot_be_paid()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var id = await AddSentAsync(db);
        var service = fixture.CreateService(db);
        var before = await db.LedgerEntries.CountAsync();
        var transactions = await db.Transactions.CountAsync();
        var original = await db.Hawalas.AsNoTracking().SingleAsync(x => x.Id == id);
        var request = new AgentBillHistoricalDto { RequestId = Guid.NewGuid(), Bill = new() { HistoricalReceipt = true,
            CorrespondentId = fixture.DestinationCorrespondent.Id, HawalaIds = [id], RecipientName = "Old agent",
            ConfirmPreviouslySettled = true, PreviousSettlementReference = "Manual payment 123" } };
        request.Bill.PreviewToken = (await service.PreviewAgentBillAsync(request.Bill)).ConfirmationToken;
        var receipt = await service.CreateHistoricalAgentReceiptAsync(request);
        Assert.Equal("Historical", receipt.Status); Assert.True(receipt.IsHistoricalReceipt);
        Assert.Empty(receipt.PaymentLedgerEntryIds); Assert.Empty(receipt.PaymentAccounts); Assert.Null(receipt.PaidAt);
        Assert.Equal("Manual payment 123", receipt.PreviousSettlementReference);
        Assert.Equal(receipt.Id, (await service.CreateHistoricalAgentReceiptAsync(request)).Id);
        Assert.Single(await service.GetAgentBillsForHawalasAsync(fixture.DestinationCorrespondent.Id, [id]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayAgentBillAsync(new() { BillId = receipt.Id, ConfirmExecution = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAndCreateAgentBillAsync(new() { RequestId = Guid.NewGuid(), Bill = request.Bill }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAgentBillAsync(request.Bill));
        await service.CancelAgentBillAsync(receipt.Id);
        await service.CancelAgentBillAsync(receipt.Id);
        Assert.True((await service.GetAgentBillAsync(receipt.Id)).IsHistoricalReceipt);
        Assert.Equal("Cancelled", (await service.CreateHistoricalAgentReceiptAsync(request)).Status);
        Assert.Equal(before, await db.LedgerEntries.CountAsync()); Assert.Equal(transactions, await db.Transactions.CountAsync());
        var current = await db.Hawalas.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(original.RowVersion, current.RowVersion); Assert.Equal(original.PaidAt, current.PaidAt); Assert.Equal(original.Status, current.Status);
        Assert.Empty(await service.GetAgentBillsForHawalasAsync(fixture.DestinationCorrespondent.Id, [id]));
        request.RequestId = Guid.NewGuid(); request.Bill.PreviewToken = (await service.PreviewAgentBillAsync(request.Bill)).ConfirmationToken;
        Assert.NotEqual(receipt.Id, (await service.CreateHistoricalAgentReceiptAsync(request)).Id);
        Assert.Equal(before, await db.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Historical_receipt_requires_explicit_confirmation_and_executed_hawalas()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var id = await AddSentAsync(db, true); var service = fixture.CreateService(db);
        var request = new AgentBillHistoricalDto { RequestId = Guid.NewGuid(), Bill = new() { HistoricalReceipt = true,
            CorrespondentId = fixture.DestinationCorrespondent.Id, HawalaIds = [id], RecipientName = "Old agent", PreviousSettlementReference = "Cash book 42" } };
        var before = await db.LedgerEntries.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateHistoricalAgentReceiptAsync(request));
        request.Bill.ConfirmPreviouslySettled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAgentBillAsync(request.Bill));
        Assert.Equal("Pending", await db.Hawalas.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
        Assert.Equal(before, await db.LedgerEntries.CountAsync()); Assert.False(await db.AgentPaymentBillItems.AnyAsync(x => x.HawalaId == id));
    }

    private readonly SqlServerPerformanceFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Manual_setting_is_saved_for_existing_agencies_and_only_changes_new_referrals()
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var previous=await AddSentAsync(db);
        var correspondents=fixture.CreateCorrespondentService(db);
        await correspondents.UpdateAsync(fixture.DestinationCorrespondent.Id,new UpdateCorrespondentDto {
            Code=fixture.DestinationCorrespondent.Code,Name=fixture.DestinationCorrespondent.Name,
            SettlementCurrencyId=2,RequiresManualExecution=true });
        Assert.True((await correspondents.GetDetailsPageAsync(fixture.DestinationCorrespondent.Id))!.Correspondent.RequiresManualExecution);
        var received=await fixture.CreateService(db).CreateHawalaAsync(new CreateHawalaDto { HawalaType="HawalaReceive",Number=987656,
            CorrespondentId=fixture.SourceCorrespondent.Id,PaymentLocationId=fixture.OwnLocation.Id,FromAccountId=fixture.DestinationAccount.Id,
            FromCurrencyId=2,ToCurrencyId=2,FromAmount=100,ToAmount=100,SenderName="S",ReceiverName="R",Status="Paid" });
        Assert.Equal("Paid",await db.Hawalas.Where(x => x.Id==previous).Select(x => x.Status).SingleAsync());
        Assert.Equal("Pending",await db.Hawalas.Where(x => x.SourceHawalaId==received.Id).Select(x => x.Status).SingleAsync());
    }

    private async Task<long> AddSentAsync(HawalaExchange.Infrastructure.Data.ApplicationDbContext db, bool manual=false, long commissionCurrency=2)
    {
        await db.Correspondents.Where(x => x.Id==fixture.DestinationCorrespondent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiresManualExecution,manual));
        var account=await db.Accounts.Where(x => x.CorrespondentId==fixture.DestinationCorrespondent.Id).Select(x => x.Id).SingleAsync();
        var received=await fixture.CreateService(db).CreateHawalasAsync([new CreateHawalaDto { HawalaType="HawalaReceive",Number=987655,
            CorrespondentId=fixture.SourceCorrespondent.Id,PaymentLocationId=fixture.OwnLocation.Id,FromAccountId=account,
            FromCurrencyId=2,ToCurrencyId=2,FromAmount=100,ToAmount=100,SenderName="S",ReceiverName="R",Status="Paid",
            GeneratedSendAgentCommissionAmount=8,GeneratedSendAgentCommissionCurrencyId=commissionCurrency }]);
        return await db.Hawalas.Where(x => x.SourceHawalaId==received[0].Id).Select(x => x.Id).SingleAsync();
    }

    [Fact]
    public async Task Cancelled_bill_releases_selection_and_currency_validation_rolls_back_execution_and_payment()
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        Assert.False(db.Database.HasPendingModelChanges());
        var id=await AddSentAsync(db,true,1); var service=fixture.CreateService(db);
        var request=new AgentBillCreateDto { CorrespondentId=fixture.DestinationCorrespondent.Id,HawalaIds=[id],RecipientName="Agent",ConfirmUnsettled=true };
        var preview=await service.PreviewAgentBillAsync(request); request.PreviewToken=preview.ConfirmationToken;
        Assert.Equal(2,preview.Totals.Count); Assert.Equal(100,preview.Totals.Single(x => x.CurrencyId==2).Principal);
        Assert.Equal(8,preview.Totals.Single(x => x.CurrencyId==1).AgentCommission);
        var bill=await service.CreateAgentBillAsync(request); await service.CancelAgentBillAsync(bill.Id);
        var replacement=await service.CreateAgentBillAsync(request);
        var cash=await db.Accounts.FirstAsync(x => x.AccountType=="Cash" && !x.IsArchived);
        var count=await db.LedgerEntries.CountAsync();
        var pay=new AgentBillPayDto { BillId=replacement.Id, Accounts=[new() { CurrencyId=2,AccountId=cash.Id },new() { CurrencyId=1,AccountId=cash.Id }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayAgentBillAsync(pay));
        pay.ConfirmExecution=true; pay.Accounts.RemoveAt(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayAgentBillAsync(pay));
        Assert.Equal(count,await db.LedgerEntries.CountAsync());
        Assert.Equal("Pending",await db.Hawalas.Where(x => x.Id==id).Select(x => x.Status).SingleAsync());
        pay.Accounts.Add(new() { CurrencyId=1,AccountId=cash.Id });
        await service.PayAgentBillAsync(pay);
        Assert.Equal("Paid",await db.Hawalas.Where(x => x.Id==id).Select(x => x.Status).SingleAsync());
        Assert.Equal("Paid",(await service.GetAgentBillAsync(replacement.Id)).Status);
    }

    [Fact]
    public async Task Stale_preview_and_foreign_correspondent_cannot_create_a_bill()
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var id=await AddSentAsync(db); var service=fixture.CreateService(db);
        var request=new AgentBillCreateDto { CorrespondentId=fixture.DestinationCorrespondent.Id,HawalaIds=[id],RecipientName="Agent",ConfirmUnsettled=true };
        request.PreviewToken=(await service.PreviewAgentBillAsync(request)).ConfirmationToken;
        await db.Hawalas.Where(x => x.Id==id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ToAmount,125m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAgentBillAsync(request));
        request.CorrespondentId=fixture.SourceCorrespondent.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAgentBillAsync(request));
        Assert.Empty(await db.AgentPaymentBills.ToListAsync());
    }

    [Fact]
    public async Task Two_cashiers_paying_the_same_bill_only_post_one_settlement()
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var id=await AddSentAsync(db); var service=fixture.CreateService(db);
        var request=new AgentBillCreateDto { CorrespondentId=fixture.DestinationCorrespondent.Id,HawalaIds=[id],RecipientName="Agent",ConfirmUnsettled=true };
        request.PreviewToken=(await service.PreviewAgentBillAsync(request)).ConfirmationToken;
        var bill=await service.CreateAgentBillAsync(request);
        var cash=await db.Accounts.FirstAsync(x => x.AccountType=="Cash" && !x.IsArchived);
        var count=await db.LedgerEntries.CountAsync();
        var pay=new AgentBillPayDto { BillId=bill.Id,Accounts=[new() { CurrencyId=2,AccountId=cash.Id }] };
        await using var first=fixture.CreateContext(); using var bypass1=first.BypassSubscriptionEnforcement();
        await using var second=fixture.CreateContext(); using var bypass2=second.BypassSubscriptionEnforcement();
        await Task.WhenAll(fixture.CreateService(first).PayAgentBillAsync(pay),fixture.CreateService(second).PayAgentBillAsync(pay));
        Assert.Equal(count+4,await db.LedgerEntries.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bill_separates_execution_and_treasury_payment_and_never_duplicates_commission(bool manual)
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        await db.Correspondents.Where(x => x.Id == fixture.DestinationCorrespondent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiresManualExecution, manual));
        var account = await db.Accounts.Where(x => x.CorrespondentId == fixture.DestinationCorrespondent.Id).Select(x => x.Id).SingleAsync();
        var service = fixture.CreateService(db);
        var received = await service.CreateHawalasAsync([new CreateHawalaDto { HawalaType="HawalaReceive", Number=987654,
            CorrespondentId=fixture.SourceCorrespondent.Id, PaymentLocationId=fixture.OwnLocation.Id,
            FromAccountId=account, FromCurrencyId=2, ToCurrencyId=2, FromAmount=100, ToAmount=100,
            SenderName="Sender", ReceiverName="Receiver", Status="Paid", GeneratedSendAgentCommissionAmount=8, GeneratedSendAgentCommissionCurrencyId=2 }]);
        var sent = await db.Hawalas.AsNoTracking().SingleAsync(x => x.SourceHawalaId == received[0].Id);
        Assert.Equal(manual ? "Pending" : "Paid", sent.Status);
        if(manual) Assert.Null(sent.PaidAt);
        var before = await db.LedgerEntries.CountAsync();
        var request = new AgentBillCreateDto { CorrespondentId=fixture.DestinationCorrespondent.Id, HawalaIds=[sent.Id], RecipientName="Agent", ConfirmUnsettled=true };
        var preview = await service.PreviewAgentBillAsync(request);
        Assert.Equal(108, preview.Totals.Single().Total);
        request.PreviewToken=preview.ConfirmationToken;
        var bill = await service.CreateAgentBillAsync(request);
        Assert.Equal(before, await db.LedgerEntries.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAgentBillAsync(request));
        if(manual) await service.ConfirmAgentExecutionAsync(fixture.DestinationCorrespondent.Id, [sent.Id]);
        Assert.Equal(before, await db.LedgerEntries.CountAsync());
        var cash = await db.Accounts.FirstAsync(x => x.AccountType=="Cash" && !x.IsArchived);
        var payment = new AgentBillPayDto { BillId=bill.Id, Accounts=[new AgentBillPaymentAccountDto { CurrencyId=2, AccountId=cash.Id }] };
        await service.PayAgentBillAsync(payment);
        var count = await db.LedgerEntries.CountAsync();
        await service.PayAgentBillAsync(payment);
        Assert.Equal(count, await db.LedgerEntries.CountAsync());
        Assert.Equal("Paid", (await service.GetAgentBillAsync(bill.Id)).Status);
        Assert.Equal(0, await db.LedgerEntries.Where(x => x.HawalaId==sent.Id && x.AccountId==account && x.CurrencyId==2).SumAsync(x => x.TalabKar-x.BadehKar));
        await service.CancelAgentBillAsync(bill.Id);
        Assert.Equal("Cancelled", (await service.GetAgentBillAsync(bill.Id)).Status);
        Assert.Equal("Paid", await db.Hawalas.Where(x => x.Id == sent.Id).Select(x => x.Status).SingleAsync());
        using(db.UseTenantScope(999999)) await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAgentBillAsync(bill.Id));
    }
    [Fact]
    public async Task Combined_execution_is_atomic_idempotent_and_reversible_then_can_be_issued_again()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var id = await AddSentAsync(db, true, 1);
        var service = fixture.CreateService(db);
        var cash = await db.Accounts.FirstAsync(x => x.AccountType == "Cash" && !x.IsArchived);
        var request = new AgentBillExecuteDto { RequestId = Guid.NewGuid(), Bill = new() { CorrespondentId = fixture.DestinationCorrespondent.Id,
            HawalaIds = [id], RecipientName = "Agent", ConfirmUnsettled = true } };
        request.Bill.PreviewToken = (await service.PreviewAgentBillAsync(request.Bill)).ConfirmationToken;
        Assert.All(await service.GetAgentBillPaymentAccountsAsync(), x => Assert.Contains(x.AccountType, new[] { "Cash", "Bank" }));
        using (db.UseTenantScope(999999)) Assert.Empty(await service.GetAgentBillPaymentAccountsAsync());
        var before = await db.LedgerEntries.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAndCreateAgentBillAsync(request));
        Assert.Equal(before, await db.LedgerEntries.CountAsync());
        Assert.False(await db.AgentPaymentBillItems.AnyAsync(x => x.HawalaId == id));
        Assert.Equal("Pending", await db.Hawalas.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
        request.Accounts = [new() { CurrencyId = 1, AccountId = cash.Id }, new() { CurrencyId = 2, AccountId = cash.Id }];
        var bill = await service.ExecuteAndCreateAgentBillAsync(request);
        Assert.Equal("Paid", bill.Status); Assert.Equal(4, bill.PaymentLedgerEntryIds.Count);
        Assert.Equal("Paid", await db.Hawalas.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
        var count = await db.LedgerEntries.CountAsync();
        Assert.Equal(bill.Id, (await service.ExecuteAndCreateAgentBillAsync(request)).Id);
        Assert.Equal(count, await db.LedgerEntries.CountAsync());
        await service.CancelAgentBillAsync(bill.Id);
        var reversedCount = await db.LedgerEntries.CountAsync();
        await service.CancelAgentBillAsync(bill.Id);
        Assert.Equal(reversedCount, await db.LedgerEntries.CountAsync());
        Assert.Equal(0, await db.LedgerEntries.Where(x => x.HawalaId == id && x.AccountId == cash.Id).SumAsync(x => x.TalabKar - x.BadehKar));
        Assert.Equal("Cancelled", (await service.ExecuteAndCreateAgentBillAsync(request)).Status);
        request.RequestId = Guid.NewGuid();
        request.Bill.PreviewToken = (await service.PreviewAgentBillAsync(request.Bill)).ConfirmationToken;
        Assert.Equal("Paid", (await service.ExecuteAndCreateAgentBillAsync(request)).Status);
    }

    [Fact]
    public async Task Concurrent_combined_requests_pay_once_and_legacy_paid_bills_can_be_reversed()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var id = await AddSentAsync(db, true);
        var service = fixture.CreateService(db);
        var cash = await db.Accounts.FirstAsync(x => x.AccountType == "Cash" && !x.IsArchived);
        var request = new AgentBillExecuteDto { RequestId = Guid.NewGuid(), Bill = new() { CorrespondentId = fixture.DestinationCorrespondent.Id,
            HawalaIds = [id], RecipientName = "Agent", ConfirmUnsettled = true }, Accounts = [new() { CurrencyId = 2, AccountId = cash.Id }] };
        request.Bill.PreviewToken = (await service.PreviewAgentBillAsync(request.Bill)).ConfirmationToken;
        var count = await db.LedgerEntries.CountAsync();
        await using var second = fixture.CreateContext(); using var secondBypass = second.BypassSubscriptionEnforcement();
        var results = await Task.WhenAll(service.ExecuteAndCreateAgentBillAsync(request), fixture.CreateService(second).ExecuteAndCreateAgentBillAsync(request));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(count + 4, await db.LedgerEntries.CountAsync());
        var related = await service.GetAgentBillsForHawalasAsync(fixture.DestinationCorrespondent.Id, [id]);
        Assert.Equal(results[0].Id, Assert.Single(related).Id);
        var stored = await db.AgentPaymentBills.SingleAsync(x => x.Id == results[0].Id);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<AgentBillDto>(stored.SnapshotJson)!;
        snapshot.PaymentLedgerEntryIds.Clear(); // Bills saved before this release did not store ledger IDs.
        stored.SnapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot); await db.SaveChangesAsync();
        await service.CancelAgentBillAsync(stored.Id);
        Assert.Equal(0, await db.LedgerEntries.Where(x => x.HawalaId == id && x.AccountId == cash.Id).SumAsync(x => x.TalabKar - x.BadehKar));
        Assert.Empty(await service.GetAgentBillsForHawalasAsync(fixture.DestinationCorrespondent.Id, [id]));
    }
}
