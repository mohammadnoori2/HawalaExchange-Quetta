using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests.Infrastructure;

// Existing commission tests seed abstract balances, not full transfer journals. Add
// their historical conversion snapshots without changing those balance fixtures.
internal static class RecordedSettlementTestData
{
    internal static async Task SeedAsync(ApplicationDbContext context,long? owner=null)
    {
        var afn=await context.Currencies.Where(x=>x.Code=="AFN").Select(x=>x.Id).SingleAsync();
        var usd=await context.Currencies.Where(x=>x.Code=="USD").Select(x=>x.Id).SingleAsync();
        var rows=await context.Hawalas.Where(x=>x.HawalaType=="HawalaReceive" && x.FromCurrencyId==afn && x.CorrespondentId!=null &&
            x.Status!="Cancel" && !x.SettlementConversionLinks.Any() && (owner==null || x.CorrespondentId==owner)).ToListAsync();
        var rates=await context.CorrespondentDailyCommissionRates.AsNoTracking().ToListAsync();
        await using var transaction=context.Database.CurrentTransaction==null?await context.Database.BeginTransactionAsync():null;
        foreach(var group in rows.Where(x=>rates.Any(r=>r.CorrespondentId==x.CorrespondentId && r.RateDate==x.CreatedAt.ToLocalTime().Date))
                    .GroupBy(x=>new{Owner=x.CorrespondentId!.Value,Day=x.CreatedAt.ToLocalTime().Date}))
        {
            var rate=rates.Single(x=>x.CorrespondentId==group.Key.Owner && x.RateDate==group.Key.Day).UsdToAfnRate;
            var posting=new Transaction{TransactionNo="TEST-SET-"+Guid.NewGuid().ToString("N"),TransactionType="CorrespondentSettlementConversion",BranchId=1,CreatedBy=context.RequireCurrentUserId(),Status="Paid"};
            context.Transactions.Add(posting);await context.SaveChangesAsync();
            var conversion=new CorrespondentSettlementConversion{CorrespondentId=group.Key.Owner,TargetCurrencyId=usd,TransactionId=posting.Id,CreatedBy=context.RequireCurrentUserId(),SourceMode="Hawalas",
                Hawalas=group.Select(x=>new CorrespondentSettlementConversionHawala{HawalaId=x.Id}).ToList(),
                HawalaItems=group.Select(x=>new CorrespondentSettlementConversionHawalaItem{HawalaId=x.Id,SourceCurrencyId=afn,SourceTalabKar=x.FromAmount,ExchangeRate=rate,TargetTalabKar=decimal.Round(x.FromAmount/rate,4,MidpointRounding.AwayFromZero)}).ToList()};
            context.CorrespondentSettlementConversions.Add(conversion);await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"EXEC dbo.usp_CaptureCorrespondentSettlement_v1 @TenantId={context.CurrentTenantId},@ConversionId={conversion.Id},@UserId={context.RequireCurrentUserId()},@UtcOffsetMinutes={(int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes},@ConfirmDifferentDailyRate=1,@Backfill=1");
        }
        if(transaction!=null)await transaction.CommitAsync();
    }
}
