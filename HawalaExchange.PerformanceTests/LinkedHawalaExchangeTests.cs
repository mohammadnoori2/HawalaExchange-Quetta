using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace HawalaExchange.PerformanceTests;
public sealed class LinkedHawalaExchangeTests : IAsyncLifetime
{
    private readonly SqlServerPerformanceFixture fixture=new();
    public Task InitializeAsync()=>fixture.InitializeAsync();
    public Task DisposeAsync()=>fixture.DisposeAsync();
    [Theory]
    [InlineData("HawalaReceive")]
    [InlineData("HawalaSend")]
    public async Task Linked_conversion_preserves_hawala_and_posts_once_with_the_correct_direction(string type)
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        Assert.False(db.Database.HasPendingModelChanges());
        var pkr=new Currency {Code="TPK",Name="Test rupees",IsActive=true,QuotationPriority=30};
        db.Currencies.Add(pkr); await db.SaveChangesAsync();
        var h=new Hawala {Number=900001,HawalaType=type,CorrespondentId=fixture.SourceCorrespondent.Id,
            FromCurrencyId=pkr.Id,ToCurrencyId=pkr.Id,FromAmount=14000,ToAmount=14000,Status="Pending",
            SenderName="S",ReceiverName="R",CreatedBy=fixture.UserId};
        db.Hawalas.Add(h); await db.SaveChangesAsync();
        var mapper=new MapperConfiguration(c=>c.AddProfile<MappingProfile>(),NullLoggerFactory.Instance).CreateMapper();
        var service=new MoneyExchangeOperationService(db,mapper,new CurrencyCostService(db));
        var request=new CreateMoneyExchangeOperationDto {LinkedHawalaId=h.Id,ConfirmNoPreviousManualConversion=true,FromAccountId=fixture.SourceAccount.Id,
            ToAccountId=fixture.SourceAccount.Id,FromCurrencyId=pkr.Id,ToCurrencyId=1,FromAmount=14000,ToAmount=3234,ExchangeRate=.231m,
            OperationType=type=="HawalaReceive"?MoneyExchangeMode.CorrespondentDebt:MoneyExchangeMode.CorrespondentCredit,ExchangeDate=DateTime.UtcNow};
        var result=await service.CreateAsync(request);
        Assert.Equal(3234,result.ToAmount); Assert.Equal(h.Id,result.LinkedHawalaId);
        Assert.Equal(14000,(await db.Hawalas.AsNoTracking().SingleAsync(x=>x.Id==h.Id)).FromAmount);
        Assert.True((await fixture.CreateService(db).GetHawalaByIdAsync(h.Id))!.HasIndependentExchange);
        Assert.Contains((await fixture.CreateService(db).GetHawalasAsync(new() {CorrespondentId=fixture.SourceCorrespondent.Id,PageSize=1000})).Items,x=>x.Id==h.Id && x.HasIndependentExchange);
        Assert.Contains((await fixture.CreateService(db).GetHawalasAsync(new() {CorrespondentId=fixture.SourceCorrespondent.Id,Extra=new() {IndependentExchange="Converted"},PageSize=1000})).Items,x=>x.Id==h.Id);
        Assert.DoesNotContain((await fixture.CreateService(db).GetHawalasAsync(new() {CorrespondentId=fixture.SourceCorrespondent.Id,Extra=new() {IndependentExchange="Original"},PageSize=1000})).Items,x=>x.Id==h.Id);
        Assert.Equal(result.Id,(await service.GetByLinkedHawalaAsync(h.Id))!.Id);
        var sign=type=="HawalaReceive"?1:-1;
        Assert.Equal(sign*14000,await db.LedgerEntries.Where(x=>x.MoneyExchangeOperationId==result.Id && x.AccountId==fixture.SourceAccount.Id && x.CurrencyId==pkr.Id).SumAsync(x=>x.TalabKar-x.BadehKar));
        Assert.Equal(-sign*3234,await db.LedgerEntries.Where(x=>x.MoneyExchangeOperationId==result.Id && x.AccountId==fixture.SourceAccount.Id && x.CurrencyId==1).SumAsync(x=>x.TalabKar-x.BadehKar));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CreateAsync(request));
        await service.DeleteAsync(result.Id);
        Assert.False((await fixture.CreateService(db).GetHawalaByIdAsync(h.Id))!.HasIndependentExchange);
        Assert.Null(await service.GetByLinkedHawalaAsync(h.Id));
        Assert.False(await db.LedgerEntries.AnyAsync(x=>x.MoneyExchangeOperationId==result.Id));
        request.FromAmount=13000;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CreateAsync(request));
    }
}
