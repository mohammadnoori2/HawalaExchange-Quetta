using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace HawalaExchange.PerformanceTests;
public sealed class CorrespondentMoneyExchangeTests : IAsyncLifetime
{
    private readonly SqlServerPerformanceFixture fixture=new();
    public Task InitializeAsync()=>fixture.InitializeAsync();
    public Task DisposeAsync()=>fixture.DisposeAsync();
    [Theory]
    [InlineData(false,64000,1000)]
    [InlineData(false,66000,-1000)]
    [InlineData(true,64000,0)]
    public async Task Same_agency_conversion_posts_the_right_direction_and_profit_and_can_be_reversed(bool debt,decimal dollarCost,decimal expectedProfit)
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var afn=new Currency { Code="TFA",Name="Test AFN",IsActive=true,QuotationPriority=20 };
        var usd=new Currency { Code="TUS",Name="Test USD",IsActive=true,QuotationPriority=10 };
        db.Currencies.AddRange(afn,usd); await db.SaveChangesAsync();
        var setting=await db.CompanySettings.FirstAsync(); setting.DefaultProfitCurrencyId=afn.Id;
        var cash=await db.Accounts.FirstAsync(x=>x.AccountType=="Cash" && !x.IsArchived);
        var capital=new Account { AccountCode="T-EQ",AccountName="Test equity",AccountType="Equity" };
        db.Accounts.Add(capital); await db.SaveChangesAsync();
        db.CapitalInvestments.Add(new() { CurrencyId=usd.Id,Amount=1000,ProfitCurrencyId=afn.Id,ProfitCurrencyAmount=dollarCost,
            ReceivingAccountId=cash.Id,CapitalAccountId=capital.Id,InvestmentDate=DateTime.UtcNow.AddDays(-1),CreatedBy=fixture.UserId });
        var account=fixture.DestinationAccount.Id;
        db.LedgerEntries.Add(new() { AccountId=account,CurrencyId=afn.Id,TalabKar=debt?0:65000,BadehKar=debt?65000:0,Description="Test balance" });
        await db.SaveChangesAsync();
        var mapper=new MapperConfiguration(c=>c.AddProfile<MappingProfile>(),NullLoggerFactory.Instance).CreateMapper();
        var service=new MoneyExchangeOperationService(db,mapper,new CurrencyCostService(db));
        var request=new CreateMoneyExchangeOperationDto { FromAccountId=account,ToAccountId=account,
            FromCurrencyId=afn.Id,ToCurrencyId=usd.Id,FromAmount=65000,ToAmount=1000,ExchangeRate=65,
            OperationType=debt?"CorrespondentDebt":"CorrespondentCredit",ExchangeDate=DateTime.UtcNow };
        var result=await service.CreateAsync(request);
        var entries=await db.LedgerEntries.AsNoTracking().Where(x=>x.MoneyExchangeOperationId==result.Id && x.AccountId==account).ToListAsync();
        Assert.Equal(debt?65000:-65000,entries.Where(x=>x.CurrencyId==afn.Id).Sum(x=>x.TalabKar-x.BadehKar));
        Assert.Equal(debt?-1000:1000,entries.Where(x=>x.CurrencyId==usd.Id).Sum(x=>x.TalabKar-x.BadehKar));
        Assert.False(await db.LedgerEntries.AnyAsync(x=>x.MoneyExchangeOperationId==result.Id && x.AccountId==cash.Id));
        Assert.Equal(expectedProfit,result.RealizedProfit);
        var summary=await service.GetProfitSummaryAsync(afn.Id);
        Assert.Equal(expectedProfit,summary.CorrespondentRealizedProfit);
        var additional=await service.CreateAsync(request);
        await service.DeleteAsync(additional.Id);
        var updated=await service.UpdateAsync(result.Id,new UpdateMoneyExchangeOperationDto {
            FromAccountId=account,ToAccountId=account,FromCurrencyId=afn.Id,ToCurrencyId=usd.Id,
            FromAmount=32500,ToAmount=500,ExchangeRate=65,OperationType=request.OperationType,ExchangeDate=request.ExchangeDate });
        Assert.Equal(expectedProfit/2,updated.RealizedProfit);
        Assert.Equal(debt?-32500:32500,await db.LedgerEntries.Where(x=>x.AccountId==account && x.CurrencyId==afn.Id).SumAsync(x=>x.TalabKar-x.BadehKar));
        await service.DeleteAsync(result.Id);
        Assert.False(await db.LedgerEntries.AnyAsync(x=>x.MoneyExchangeOperationId==result.Id));
        Assert.Equal(debt?-65000:65000,await db.LedgerEntries.Where(x=>x.AccountId==account && x.CurrencyId==afn.Id).SumAsync(x=>x.TalabKar-x.BadehKar));
        db.CorrespondentAccountPeriods.Add(new() { CorrespondentId=fixture.DestinationCorrespondent.Id,PeriodFrom=DateTime.UtcNow.AddDays(-1),PeriodTo=DateTime.UtcNow,ClosedAt=DateTime.UtcNow,ClosedBy=fixture.UserId });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CreateAsync(request));
    }

    [Theory]
    [InlineData(5251,10000,5251)]
    [InlineData(0,0,10000)]
    [InlineData(12000,0,22000)]
    public async Task Converting_a_specific_debt_is_not_capped_by_the_net_balance(decimal oldCredit,decimal receivedDebt,decimal expectedUsd)
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        var account=fixture.DestinationAccount.Id;
        if(oldCredit>0) db.LedgerEntries.Add(new() { AccountId=account,CurrencyId=2,TalabKar=oldCredit });
        if(receivedDebt>0) db.LedgerEntries.Add(new() { AccountId=account,CurrencyId=2,BadehKar=receivedDebt });
        db.LedgerEntries.Add(new() { AccountId=account,CurrencyId=1,TalabKar=1615263 });
        await db.SaveChangesAsync();
        var mapper=new MapperConfiguration(c=>c.AddProfile<MappingProfile>(),NullLoggerFactory.Instance).CreateMapper();
        var service=new MoneyExchangeOperationService(db,mapper,new CurrencyCostService(db));
        await service.CreateAsync(new() { FromAccountId=account,ToAccountId=account,FromCurrencyId=2,ToCurrencyId=1,
            FromAmount=10000,ToAmount=651400,ExchangeRate=65.14m,OperationType=MoneyExchangeMode.CorrespondentDebt,ExchangeDate=DateTime.UtcNow });
        Assert.Equal(expectedUsd,await db.LedgerEntries.Where(x=>x.AccountId==account && x.CurrencyId==2).SumAsync(x=>x.TalabKar-x.BadehKar));
        Assert.Equal(963863,await db.LedgerEntries.Where(x=>x.AccountId==account && x.CurrencyId==1).SumAsync(x=>x.TalabKar-x.BadehKar));
    }
}
