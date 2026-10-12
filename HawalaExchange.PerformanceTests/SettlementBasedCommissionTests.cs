using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class SettlementBasedCommissionTests : IAsyncLifetime
{
    private readonly SqlServerPerformanceFixture fixture = new();
    public Task InitializeAsync()=>fixture.InitializeAsync();
    public Task DisposeAsync()=>fixture.DisposeAsync();

    [Fact]
    public async Task Incoming_uses_actual_afn_pkr_settlements_and_never_charges_an_overlapping_period_twice()
    {
        await using var context=fixture.CreateContext();
        using var bypass=context.BypassSubscriptionEnforcement();
        var pkr=await context.Currencies.SingleAsync(x=>x.Code=="PKR");
        var day=DateTime.Today.AddDays(-1);
        var afn=await Seed(context,1,100000m,day,1); var pk=await Seed(context,pkr.Id,60000m,day,2); var usd=await Seed(context,2,1000m,day,3);
        var commissions=fixture.CreateCommissionService(context);
        var request=new CorrespondentCommissionPreviewRequestDto{CorrespondentId=fixture.SourceCorrespondent.Id,CommissionScope="Incoming",HawalaType="HawalaReceive",PeriodFrom=day,PeriodTo=day,CommissionPerLakhAfn=400};
        await Assert.ThrowsAnyAsync<Exception>(()=>commissions.PreviewAsync(request));
        var conversion=await fixture.CreateSettlementService(context).ConvertHawalasAsync(new()
        {CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[afn.Id,pk.Id],HawalaRates=[new(){HawalaId=afn.Id,SourceCurrencyId=1,Rate=65},new(){HawalaId=pk.Id,SourceCurrencyId=pkr.Id,Rate=228}]});
        var rates=await fixture.CreateSettlementService(context).GetRatesAsync(fixture.SourceCorrespondent.Id,day,day);
        Assert.Equal(2,rates.Count); Assert.Equal(228m,rates.Single(x=>x.SourceCurrencyId==pkr.Id).ExchangeRate);
        var preview=await commissions.PreviewAsync(request);
        Assert.Equal(3,preview.HawalaCount); Assert.InRange(preview.TotalBaseAfn,2801.6193m,2801.6195m); Assert.Equal(11m,preview.TotalCommissionUsd);
        var posted=await commissions.PostAsync(request); Assert.Equal(3,posted.AccountingVersion);
        Assert.Equal(0,(await commissions.PreviewAsync(request)).HawalaCount);
        await Assert.ThrowsAnyAsync<Exception>(()=>commissions.PostAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.CreateSettlementService(context).ReverseAsync(fixture.SourceCorrespondent.Id,conversion.ConversionId,"Protected commission"));
        await commissions.ReverseAsync(posted.Id,"Authorized reversal");
        await fixture.CreateSettlementService(context).ReverseAsync(fixture.SourceCorrespondent.Id,conversion.ConversionId,"Undo settlement");
        var history=await fixture.CreateSettlementService(context).GetHistoryDetailsAsync(fixture.SourceCorrespondent.Id,conversion.ConversionId);
        Assert.Equal("Reversed",history.Status); Assert.Equal(2,history.Items.Count);
        Assert.All(history.Items,x=>Assert.Equal(fixture.OwnLocation.Id,x.PaymentLocationId));
        Assert.Equal(2,(await fixture.CreateSettlementService(context).GetHawalaPreviewAsync(fixture.SourceCorrespondent.Id,[afn.Id,pk.Id])).Count);
    }

    [Fact]
    public async Task Days_are_independent_and_a_failed_second_day_does_not_undo_or_duplicate_first_day()
    {
        await using var context=fixture.CreateContext(); using var bypass=context.BypassSubscriptionEnforcement();
        var first=await Seed(context,1,65000m,DateTime.Today.AddDays(-2),10);
        var second=await Seed(context,1,70000m,DateTime.Today.AddDays(-1),11);
        var service=fixture.CreateSettlementService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConvertHawalasAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[first.Id,second.Id],HawalaRates=[new(){HawalaId=first.Id,SourceCurrencyId=1,Rate=65},new(){HawalaId=second.Id,SourceCurrencyId=1,Rate=70}]}));
        await service.ConvertHawalasAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[first.Id],HawalaRates=[new(){HawalaId=first.Id,SourceCurrencyId=1,Rate=65}]});
        await Assert.ThrowsAnyAsync<Exception>(()=>service.ConvertHawalasAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[second.Id],HawalaRates=[new(){HawalaId=second.Id,SourceCurrencyId=1,Rate=0}]}));
        Assert.Equal(1,await context.CorrespondentSettlementConversions.CountAsync());
        await service.ConvertHawalasAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[second.Id],HawalaRates=[new(){HawalaId=second.Id,SourceCurrencyId=1,Rate=70}]});
        Assert.Equal(2,(await service.GetHistoryAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id})).Count);
        var rates=await service.GetRatesAsync(fixture.SourceCorrespondent.Id);
        Assert.Equal(65m,rates.Single(x=>x.RateDate==first.CreatedAt.ToLocalTime().Date).ExchangeRate);
        Assert.Equal(70m,rates.Single(x=>x.RateDate==second.CreatedAt.ToLocalTime().Date).ExchangeRate);
        using(context.UseTenantScope(999999)) Assert.Empty(await service.GetHistoryAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id}));
    }

    [Fact]
    public async Task Pkr_destination_and_forwarding_commissions_are_independent_and_deferred_until_close()
    {
        await using var context=fixture.CreateContext();using var bypass=context.BypassSubscriptionEnforcement();
        var pkr=await context.Currencies.SingleAsync(x=>x.Code=="PKR");var day=DateTime.Today.AddDays(-1);
        context.CompanySettings.Add(new CompanySetting{CompanyName="Test",OwnPaymentLocationId=fixture.OwnLocation.Id});await context.SaveChangesAsync();
        var parent=await Seed(context,pkr.Id,60000m,day,20);
        await fixture.CreateSettlementService(context).ConvertHawalasAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[parent.Id],HawalaRates=[new(){HawalaId=parent.Id,SourceCurrencyId=pkr.Id,Rate=228}]});
        var sent=new Hawala{Number=89_000_021,HawalaType="HawalaSend",CorrespondentId=fixture.DestinationCorrespondent.Id,SourceHawalaId=parent.Id,IsSystemGenerated=true,
            PaymentLocationId=fixture.RemoteLocation.Id,FromCurrencyId=pkr.Id,ToCurrencyId=pkr.Id,FromAmount=60000,ToAmount=60000,Status="Paid",CreatedBy=fixture.UserId,CreatedAt=parent.CreatedAt};
        context.Hawalas.Add(sent);await context.SaveChangesAsync();
        var service=fixture.CreateCommissionService(context);
        var forwarding=new CorrespondentCommissionPreviewRequestDto{CorrespondentId=fixture.SourceCorrespondent.Id,CommissionScope="Forwarding",HawalaType="HawalaSend",PeriodFrom=day,PeriodTo=day,CommissionPerLakhAfn=400,
            PaymentLocationRates=[new(){PaymentLocationId=fixture.RemoteLocation.Id,PerLakhRate=400}]};
        var destination=new CorrespondentCommissionPreviewRequestDto{CorrespondentId=fixture.DestinationCorrespondent.Id,CommissionScope="Destination",HawalaType="HawalaSend",PeriodFrom=day,PeriodTo=day,CommissionPerLakhAfn=400,
            LocationCurrencyRates=[new(){PaymentLocationId=fixture.RemoteLocation.Id,CurrencyId=pkr.Id,PerLakhRate=1000}]};
        var before=await fixture.CreateBalanceService(context).GetAccountBalanceAsync(fixture.SourceAccount.Id);
        var sourceBatch=await service.PostAsync(forwarding);
        Assert.Equal(1m,sourceBatch.TotalCommissionUsd);
        var paid=await service.PostAsync(destination);
        Assert.Equal(600m,paid.CurrencyTotals.Single(x=>x.CurrencyCode=="PKR").CommissionAmount);
        var postingId=await context.CorrespondentCommissionBatches.Where(x=>x.Id==paid.Id).Select(x=>x.PostingTransactionId).SingleAsync();
        var entries=await context.LedgerEntries.Where(x=>x.TransactionId==postingId).ToListAsync();
        Assert.Equal(2,entries.Count);Assert.All(entries,x=>Assert.Equal(pkr.Id,x.CurrencyId));
        Assert.Equal(600m,entries.Sum(x=>x.TalabKar));Assert.Equal(600m,entries.Sum(x=>x.BadehKar));
        Assert.Equal(0,(await service.PreviewAsync(forwarding)).HawalaCount);Assert.Equal(0,(await service.PreviewAsync(destination)).HawalaCount);
        var pending=await fixture.CreateBalanceService(context).GetAccountBalanceAsync(fixture.SourceAccount.Id);
        Assert.Equal(before.Single(x=>x.CurrencyId==2).Balance,pending.Single(x=>x.CurrencyId==2).Balance);
        Assert.Equal(1m,pending.Single(x=>x.CurrencyId==2).PendingCommissionDebit);
        var details=await service.GetDetailsAsync(paid.Id);Assert.Equal(600m,details.CurrencyTotals.Single(x=>x.CurrencyCode=="PKR").CommissionAmount);
        await fixture.CreateCorrespondentService(context).ClosePeriodAsync(fixture.SourceCorrespondent.Id,new());
        Assert.Equal(0m,(await fixture.CreateBalanceService(context).GetAccountBalanceAsync(fixture.SourceAccount.Id)).Single(x=>x.CurrencyId==2).PendingCommissionDebit);
    }

    [Fact]
    public async Task Different_rate_requires_confirmation_preserves_old_valuation_and_update_history_keeps_previous_values()
    {
        await using var context=fixture.CreateContext();using var bypass=context.BypassSubscriptionEnforcement();
        var day=DateTime.Today.AddDays(-1);var first=await Seed(context,1,65000,day,30);var next=await Seed(context,1,70000,day,31);
        var service=fixture.CreateSettlementService(context);
        var original=await service.ConvertHawalasAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[first.Id],HawalaRates=[new(){HawalaId=first.Id,SourceCurrencyId=1,Rate=65}]});
        var second=new ConvertHawalasToSettlementDto{CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[next.Id],HawalaRates=[new(){HawalaId=next.Id,SourceCurrencyId=1,Rate=70}]};
        await Assert.ThrowsAnyAsync<Exception>(()=>service.ConvertHawalasAsync(second));
        Assert.Equal(1,await context.CorrespondentSettlementConversions.CountAsync());
        second.ConfirmDifferentDailyRate=true;await service.ConvertHawalasAsync(second);
        await service.UpdateHawalaRateAsync(new(){HawalaId=first.Id,SourceCurrencyId=1,Rate=66});
        var details=await service.GetHistoryDetailsAsync(fixture.SourceCorrespondent.Id,original.ConversionId);
        Assert.Equal(2,details.Revisions.Count);Assert.Equal(65m,details.Revisions[0].Items.Single().ExchangeRate);Assert.Equal(66m,details.Items.Single().ExchangeRate);
        using(context.UseTenantScope(999999))Assert.Empty(await service.GetRatesAsync(fixture.SourceCorrespondent.Id));
    }

    [Fact]
    public async Task Closed_period_prevents_reversing_or_repricing_its_settlement()
    {
        await using var context=fixture.CreateContext();using var bypass=context.BypassSubscriptionEnforcement();
        var row=await Seed(context,1,65000,DateTime.Today.AddDays(-1),40);
        var service=fixture.CreateSettlementService(context);
        var conversion=await service.ConvertHawalasAsync(new(){CorrespondentId=fixture.SourceCorrespondent.Id,HawalaIds=[row.Id],HawalaRates=[new(){HawalaId=row.Id,SourceCurrencyId=1,Rate=65}]});
        await fixture.CreateCorrespondentService(context).ClosePeriodAsync(fixture.SourceCorrespondent.Id,new());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReverseAsync(fixture.SourceCorrespondent.Id,conversion.ConversionId,"Closed period"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UpdateHawalaRateAsync(new(){HawalaId=row.Id,SourceCurrencyId=1,Rate=70}));
        Assert.Equal(65m,(await service.GetHistoryDetailsAsync(fixture.SourceCorrespondent.Id,conversion.ConversionId)).Items.Single().ExchangeRate);
    }

    [Fact]
    public async Task Non_usd_agreed_currency_requires_and_uses_the_recorded_same_day_usd_bridge()
    {
        await using var context=fixture.CreateContext();using var bypass=context.BypassSubscriptionEnforcement();
        var owner=await context.Correspondents.SingleAsync(x=>x.Id==fixture.SourceCorrespondent.Id);
        owner.SettlementCurrencyId=1;await context.SaveChangesAsync();
        var day=DateTime.Today.AddDays(-1);var afn=await Seed(context,1,65000,day,50);var usd=await Seed(context,2,1000,day,51);
        var request=new CorrespondentCommissionPreviewRequestDto{CorrespondentId=owner.Id,CommissionScope="Incoming",HawalaType="HawalaReceive",PeriodFrom=day,PeriodTo=day,CommissionPerLakhAfn=400};
        var commissions=fixture.CreateCommissionService(context);
        await Assert.ThrowsAnyAsync<Exception>(()=>commissions.PreviewAsync(request));
        await fixture.CreateSettlementService(context).ConvertHawalasAsync(new(){CorrespondentId=owner.Id,HawalaIds=[usd.Id],HawalaRates=[new(){HawalaId=usd.Id,SourceCurrencyId=2,Rate=65}]});
        var preview=await commissions.PreviewAsync(request);
        Assert.Equal(2,preview.HawalaCount);Assert.Equal(2000m,preview.TotalBaseAfn);Assert.Equal(8m,preview.TotalCommissionUsd);
    }

    private async Task<Hawala> Seed(HawalaExchange.Infrastructure.Data.ApplicationDbContext context,long currency,decimal amount,DateTime day,long number)
    {
        var row=new Hawala{Number=89_000_000+number,HawalaType="HawalaReceive",CorrespondentId=fixture.SourceCorrespondent.Id,
            PaymentLocationId=fixture.OwnLocation.Id,FromCurrencyId=currency,ToCurrencyId=currency,FromAmount=amount,ToAmount=amount,
            SenderName="Sender",ReceiverName="Receiver",Status="Paid",CreatedBy=fixture.UserId,CreatedAt=day.AddHours(12).ToUniversalTime()};
        context.Hawalas.Add(row); await context.SaveChangesAsync();
        context.LedgerEntries.AddRange(new LedgerEntry{HawalaId=row.Id,AccountId=fixture.SourceAccount.Id,CurrencyId=currency,TalabKar=amount},
            new LedgerEntry{HawalaId=row.Id,AccountId=fixture.DestinationAccount.Id,CurrencyId=currency,BadehKar=amount});
        await context.SaveChangesAsync(); return row;
    }
}
