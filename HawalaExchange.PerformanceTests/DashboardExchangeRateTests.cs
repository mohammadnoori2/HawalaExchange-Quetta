using AutoMapper;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class DashboardExchangeRateTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Latest_rate_per_pair_uses_tenant_today_excludes_future_and_inactive_and_breaks_timestamp_ties()
    {
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var service=new ExchangeRateService(db,Mock.Of<IMapper>());
        var clock=await service.GetDashboardRatesAsync();var today=clock.Today;
        var key=Guid.NewGuid().ToString("N")[..5];
        var a=new Currency{Code="A"+key,Name="A",IsActive=true};var b=new Currency{Code="B"+key,Name="B",IsActive=true};
        var inactive=new Currency{Code="I"+key,Name="Inactive",IsActive=false};db.AddRange(a,b,inactive);await db.SaveChangesAsync();
        ExchangeRate Rate(long from,DateTime date,decimal value)=>new(){FromCurrencyId=from,ToCurrencyId=2,BuyRate=value,SellRate=value+1,EffectiveDate=date,CreatedBy=fixture.UserId};
        db.ExchangeRates.AddRange(Rate(a.Id,today.AddDays(-2),10),Rate(a.Id,today.AddDays(1),999),Rate(b.Id,today.AddHours(1),20),Rate(inactive.Id,today,50));await db.SaveChangesAsync();
        db.ExchangeRates.Add(Rate(b.Id,today.AddHours(1),30));await db.SaveChangesAsync();
        var result=await service.GetDashboardRatesAsync();
        Assert.Equal(today,result.Today);
        var old=Assert.Single(result.Rates,x=>x.FromCurrencyId==a.Id);Assert.Equal(10,old.BuyRate);Assert.Equal(today.AddDays(-2),old.EffectiveDate);
        var current=Assert.Single(result.Rates,x=>x.FromCurrencyId==b.Id);Assert.Equal(30,current.BuyRate);Assert.Equal(today,current.EffectiveDate.Date);
        Assert.DoesNotContain(result.Rates,x=>x.FromCurrencyId==inactive.Id);
        Assert.Equal("Rates.View",PermissionOperationMap.Resolve("IExchangeRateService","GetDashboardRatesAsync"));
        await tx.RollbackAsync();
    }
}
