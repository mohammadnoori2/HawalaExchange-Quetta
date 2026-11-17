using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using HawalaExchange.Infrastructure.Services;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class HawalaAmountSummaryTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Summary_aggregates_all_filtered_rows_stored_principal_and_selections_without_fees_or_duplicate_serials()
    {
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var key="Summary-"+Guid.NewGuid().ToString("N");
        await db.Correspondents.Where(x=>x.Id==fixture.SourceCorrespondent.Id).ExecuteUpdateAsync(x=>x.SetProperty(c=>c.SettlementCurrencyId,(long?)2));
        Hawala H(decimal amount,long currency,string status="Paid")=>new(){Number=1,HawalaType="HawalaReceive",CorrespondentId=fixture.SourceCorrespondent.Id,
            FromCurrencyId=currency,ToCurrencyId=currency,FromAmount=amount,ToAmount=amount,SenderName=key,ReceiverName="Recipient",ReferenceNumber=key,
            Status=status,CreatedBy=fixture.UserId,CreatedAt=DateTime.UtcNow};
        var converted=H(6500,1);var native=H(100,2);var pending=H(3250,1);var cancelled=H(6500,1,"Cancel");
        await db.Correspondents.Where(x=>x.Id==fixture.DestinationCorrespondent.Id).ExecuteUpdateAsync(x=>x.SetProperty(c=>c.SettlementCurrencyId,(long?)1));
        var other=H(50,1);other.CorrespondentId=fixture.DestinationCorrespondent.Id;
        db.AddRange(converted,native,pending,cancelled,other);await db.SaveChangesAsync();
        var transaction=new Transaction{TransactionNo=key,TransactionType="CorrespondentSettlement",Status="Paid",CreatedBy=fixture.UserId,BranchId=await db.GetDefaultBranchIdAsync()};
        db.Transactions.Add(transaction);await db.SaveChangesAsync();
        var conversion=new CorrespondentSettlementConversion{CorrespondentId=fixture.SourceCorrespondent.Id,TargetCurrencyId=2,TransactionId=transaction.Id,CreatedBy=fixture.UserId};
        db.Add(conversion);await db.SaveChangesAsync();
        db.CorrespondentSettlementConversionHawalas.Add(new(){ConversionId=conversion.Id,HawalaId=converted.Id});
        db.CorrespondentSettlementConversionHawalaItems.Add(new(){ConversionId=conversion.Id,HawalaId=converted.Id,SourceCurrencyId=1,SourceBadehKar=7150,TargetBadehKar=110,ExchangeRate=65});
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.CorrespondentSettlementValuations(TenantId,HawalaId,ConversionId,CorrespondentId,RateDate,SourceCurrencyId,TargetCurrencyId,SourceAmount,ExchangeRate,UnitToTarget,TargetAmount,UsdAmount) VALUES({db.CurrentTenantId},{converted.Id},{conversion.Id},{fixture.SourceCorrespondent.Id},{DateTime.Today},{1},{2},{6500m},{65m},{1m/65m},{100m},{100m})");
        var ledgerCount=await db.LedgerEntries.CountAsync();var service=fixture.CreateService(db);
        var browserRows=await service.GetSelectionAmountsAsync([converted.Id,native.Id,pending.Id,cancelled.Id]);
        Assert.Equal(4,browserRows.Count);
        Assert.Equal(100m,decimal.Parse(Assert.Single(browserRows,x=>x.Id==converted.Id).Converted!,System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("USD",Assert.Single(browserRows,x=>x.Id==native.Id).SettlementCurrency);
        Assert.True(Assert.Single(browserRows,x=>x.Id==cancelled.Id).Cancelled);
        var page=await service.GetHawalasAsync(new(){SearchTerm=key,PageSize=1,IncludeBrowserAmounts=true});
        Assert.Equal(Assert.Single(page.Items).Id,Assert.Single(page.AmountRows).Id);
        var filter=new HawalaFilterDto{SearchTerm=key,PageSize=1,PageNumber=99,Extra=new(){MinNumber=1,MaxNumber=1}};
        var report=await service.GetAmountSummaryAsync(new(){Filter=filter});
        Assert.Equal(5,report.Count);Assert.Equal(9800,Assert.Single(report.Originals,x=>x.Currency=="AFN"&&!x.Cancelled).Amount);
        Assert.Equal(6500,Assert.Single(report.Originals,x=>x.Cancelled).Amount);
        var active=Assert.Single(report.Settlements,x=>!x.Cancelled&&x.CorrespondentId==fixture.SourceCorrespondent.Id);Assert.Equal(100,active.NativeAmount);Assert.Equal(100,active.ConvertedAmount);Assert.Equal(200,active.Total);Assert.Equal(1,active.UnconvertedCount);
        var otherTotal=Assert.Single(report.Settlements,x=>x.CorrespondentId==fixture.DestinationCorrespondent.Id);Assert.Equal("AFN",otherTotal.Currency);Assert.Equal(50,otherTotal.Total);
        var selected=await service.GetAmountSummaryAsync(new(){Filter=new(){SearchTerm="not matching"},SelectedIds=[converted.Id,native.Id]});
        Assert.Equal(2,selected.Count);Assert.True(selected.SelectedOnly);Assert.Equal(200,Assert.Single(selected.Settlements).Total);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GetAmountSummaryAsync(new(){SelectedIds=[converted.Id],ScopeCorrespondentId=fixture.DestinationCorrespondent.Id}));
        transaction.Status="Cancel";await db.SaveChangesAsync();
        var reversed=await service.GetAmountSummaryAsync(new(){Filter=filter});Assert.Equal(0,Assert.Single(reversed.Settlements,x=>!x.Cancelled&&x.CorrespondentId==fixture.SourceCorrespondent.Id).ConvertedAmount);
        Assert.Equal(ledgerCount,await db.LedgerEntries.CountAsync());
        Assert.Equal("Hawalas.View",PermissionOperationMap.Resolve("IHawalaService","GetAmountSummaryAsync"));
        Assert.Equal("Hawalas.View",PermissionOperationMap.Resolve("IHawalaService","GetSelectionAmountsAsync"));
        await tx.RollbackAsync();
    }
}
