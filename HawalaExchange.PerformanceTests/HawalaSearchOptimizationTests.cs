using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class HawalaSearchOptimizationTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Exact_identifiers_take_priority_and_fallback_preserves_text_amount_and_period_filters()
    {
        await using var db = fixture.CreateContext();
        using var bypass = db.BypassSubscriptionEnforcement();
        await using var tx = await db.Database.BeginTransactionAsync();
        var key = "Search-"+Guid.NewGuid().ToString("N");
        Hawala H(long number,string sender,string reference,decimal amount) => new() {
            Number=number, SenderName=sender, ReceiverName=key, ReferenceNumber=reference,
            CorrespondentId=fixture.SourceCorrespondent.Id, HawalaType="HawalaReceive", Status="Paid",
            FromCurrencyId=2, ToCurrencyId=2, FromAmount=amount, ToAmount=amount, CreatedAt=DateTime.UtcNow, CreatedBy=fixture.UserId };
        var exact=H(910000001,key,key+"-REF",100);
        var text=H(910000002,"Name 910000001",key+"-OTHER",910000001);
        var duplicate=H(910000001,key,key+"-DUP",200);
        db.Hawalas.AddRange(exact,text,duplicate);await db.SaveChangesAsync();
        var service=fixture.CreateService(db);
        HawalaFilterDto Filter(string term)=>new(){SearchTerm=term,PreferExactIdentifiers=true,Extra=new(){Receiver=key},PageSize=10};
        var serial=await service.GetHawalasAsync(Filter("910000001"));
        Assert.Equal(new[]{exact.Id,duplicate.Id}.Order(),serial.Items.Select(x=>x.Id).Order());
        var reference=await service.GetHawalasAsync(Filter(exact.ReferenceNumber!));Assert.Equal(exact.Id,Assert.Single(reference.Items).Id);
        var unicode=await service.GetHawalasAsync(Filter("۹۱۰۰۰۰۰۰۱"));Assert.Equal(2,unicode.TotalCount);
        var constrained=Filter("910000001");constrained.MinAmount=910000001;
        Assert.Equal(text.Id,Assert.Single((await service.GetHawalasAsync(constrained)).Items).Id);
        var partial=await service.GetHawalasAsync(Filter("OTHER"));Assert.Equal(text.Id,Assert.Single(partial.Items).Id);
        var name=await service.GetHawalasAsync(Filter("Name"));Assert.Equal(text.Id,Assert.Single(name.Items).Id);
        var amount=Filter("910000001.00");amount.SearchAmount=910000001;
        Assert.Equal(text.Id,Assert.Single((await service.GetHawalasAsync(amount)).Items).Id);
        var legacy=Filter("910000001");legacy.PreferExactIdentifiers=false;legacy.Number=910000001;legacy.SearchAmount=910000001;
        Assert.Equal(3,(await service.GetHawalasAsync(legacy)).TotalCount);
        var summary=await service.GetAmountSummaryAsync(new(){Filter=Filter("910000001")});Assert.Equal(2,summary.Count);Assert.Equal(300,Assert.Single(summary.Originals).Amount);
        var filter=Filter("910000001");filter.PageSize=1;filter.SortColumn="CreatedAt";
        var first=await service.GetHawalasAsync(filter);filter.PageNumber=2;var second=await service.GetHawalasAsync(filter);
        Assert.NotEqual(Assert.Single(first.Items).Id,Assert.Single(second.Items).Id);
        Assert.Empty(first.AmountRows);
        await tx.RollbackAsync();
    }
}
