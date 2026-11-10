using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace HawalaExchange.PerformanceTests;
public sealed class HawalaAccountingDateTests : IAsyncLifetime
{
    private readonly SqlServerPerformanceFixture fixture=new();
    public Task InitializeAsync()=>fixture.InitializeAsync();
    public Task DisposeAsync()=>fixture.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editing_a_paid_hawala_keeps_registration_and_payment_on_their_original_days(bool forwarded)
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var cash=new Account {AccountCode="DATE-CASH",AccountName="Date test cash",AccountType="Cash"};
        db.Accounts.Add(cash); await db.SaveChangesAsync();
        var receiptAt=DateTime.UtcNow.Date.AddDays(-3).AddHours(6);
        var paymentAt=receiptAt.AddDays(1).AddHours(2);
        db.LedgerEntries.Add(new() {AccountId=cash.Id,CurrencyId=2,BadehKar=50000,CreatedAt=receiptAt.AddDays(-1)});
        await db.SaveChangesAsync();
        var service=fixture.CreateService(db);
        var created=await service.CreateHawalaAsync(new() {HawalaType="HawalaReceive",Number=765432,
            CorrespondentId=fixture.SourceCorrespondent.Id,PaymentLocationId=fixture.OwnLocation.Id,
            FromAccountId=forwarded?fixture.DestinationAccount.Id:cash.Id,FromCurrencyId=2,ToCurrencyId=2,
            FromAmount=10000,ToAmount=10000,SenderName="Sender",ReceiverName="Receiver",Status="Paid"});
        await db.Hawalas.Where(x=>x.Id==created.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,receiptAt).SetProperty(x=>x.PaidAt,paymentAt));
        var generatedId=await db.Hawalas.Where(x=>x.SourceHawalaId==created.Id).Select(x=>(long?)x.Id).SingleOrDefaultAsync();
        await db.LedgerEntries.Where(x=>x.HawalaId==created.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,paymentAt));
        await db.LedgerEntries.Where(x=>x.HawalaId==created.Id && x.Description!.Contains("ثبت")).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,receiptAt));
        if(generatedId.HasValue) {
            await db.Hawalas.Where(x=>x.Id==generatedId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,paymentAt).SetProperty(x=>x.PaidAt,paymentAt));
            await db.LedgerEntries.Where(x=>x.HawalaId==generatedId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,paymentAt));
        }
        db.ChangeTracker.Clear();
        var journal=fixture.CreateJournalService(db);
        var before=(await journal.GetCashDailyBalancesAsync(DateTime.Today)).Single(x=>x.AccountId==cash.Id && x.CurrencyId==2).CurrentBalance;
        await service.UpdateHawalaAsync(created.Id,new() {FromCurrencyId=2,ToCurrencyId=2,FromAmount=10000,ToAmount=10000,
            CorrespondentId=fixture.SourceCorrespondent.Id,PaymentLocationId=fixture.OwnLocation.Id,
            FromAccountId=forwarded?fixture.DestinationAccount.Id:cash.Id,SenderName="Sender",ReceiverName="Edited receiver",Status="Paid"});
        var source=await db.Hawalas.AsNoTracking().SingleAsync(x=>x.Id==created.Id);
        Assert.Equal(receiptAt,source.CreatedAt); Assert.Equal(paymentAt,source.PaidAt);
        var sourceRows=await db.LedgerEntries.AsNoTracking().Where(x=>x.HawalaId==created.Id).ToListAsync();
        Assert.All(sourceRows.Where(x=>x.Description!.Contains("ثبت")),x=>Assert.Equal(receiptAt,x.CreatedAt));
        if(forwarded) {
            var sent=await db.Hawalas.AsNoTracking().SingleAsync(x=>x.SourceHawalaId==created.Id);
            Assert.Equal(paymentAt,sent.CreatedAt);
            Assert.All(await db.LedgerEntries.Where(x=>x.HawalaId==sent.Id).ToListAsync(),x=>Assert.Equal(paymentAt,x.CreatedAt));
        } else {
            Assert.All(sourceRows.Where(x=>!x.Description!.Contains("ثبت")),x=>Assert.Equal(paymentAt,x.CreatedAt));
        }
        Assert.Equal(before,(await journal.GetCashDailyBalancesAsync(DateTime.Today)).Single(x=>x.AccountId==cash.Id && x.CurrencyId==2).CurrentBalance);
    }

    [Fact]
    public async Task Automatic_opening_follows_corrected_prior_postings_but_manual_counts_and_closed_days_are_preserved()
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var cash=new Account {AccountCode="DATE-OPEN",AccountName="Opening date test",AccountType="Cash"};
        db.Accounts.Add(cash); await db.SaveChangesAsync();
        var today=DateTime.Today;
        db.LedgerEntries.Add(new() {AccountId=cash.Id,CurrencyId=2,BadehKar=50000,CreatedAt=today.AddDays(-2).ToUniversalTime()});
        await db.SaveChangesAsync(); var journal=fixture.CreateJournalService(db);
        Assert.Equal(50000,(await journal.GetCashDailyBalancesAsync(today)).Single(x=>x.AccountId==cash.Id && x.CurrencyId==2).CurrentBalance);
        db.LedgerEntries.Add(new() {AccountId=cash.Id,CurrencyId=2,TalabKar=10000,CreatedAt=today.AddDays(-1).ToUniversalTime()});
        await db.SaveChangesAsync();
        Assert.Equal(40000,(await journal.GetCashDailyBalancesAsync(today)).Single(x=>x.AccountId==cash.Id && x.CurrencyId==2).CurrentBalance);
        await journal.SaveCashOpeningBalancesAsync(today,[new() {AccountId=cash.Id,CurrencyId=2,OpeningBalance=12345}]);
        Assert.Equal(12345,(await journal.GetCashDailyBalancesAsync(today)).Single(x=>x.AccountId==cash.Id && x.CurrencyId==2).CurrentBalance);
        await journal.CloseCashDayAsync(today);
        db.LedgerEntries.Add(new() {AccountId=cash.Id,CurrencyId=2,TalabKar=1000,CreatedAt=today.AddDays(-1).ToUniversalTime()});
        await db.SaveChangesAsync();
        Assert.Equal(12345,(await journal.GetCashDailyBalancesAsync(today)).Single(x=>x.AccountId==cash.Id && x.CurrencyId==2).ClosingBalance);
        Assert.Equal(12345,(await journal.GetCashDailyBalancesAsync(today.AddDays(1))).Single(x=>x.AccountId==cash.Id && x.CurrencyId==2).OpeningBalance);
    }
}
