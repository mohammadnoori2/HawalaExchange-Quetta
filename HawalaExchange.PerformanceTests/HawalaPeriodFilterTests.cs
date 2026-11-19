using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class HawalaPeriodFilterTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Repeated_serials_are_separated_by_membership_in_lists_ranges_and_totals()
    {
        await using var db = fixture.CreateContext();
        using var bypass = db.BypassSubscriptionEnforcement();
        await using var tx = await db.Database.BeginTransactionAsync();
        var key = "Periods-" + Guid.NewGuid().ToString("N");
        var corr = fixture.SourceCorrespondent.Id;
        var date = new DateTime(2087, 1, 1);
        Hawala H(decimal amount) => new() { Number = 1, HawalaType = "HawalaSend", CorrespondentId = corr,
            SenderName = key, ReceiverName = "Recipient", ReferenceNumber = key, FromAmount = amount, ToAmount = amount,
            FromCurrencyId = 2, ToCurrencyId = 2, Status = "Paid", CreatedAt = date, CreatedBy = fixture.UserId };
        var first = H(100); var second = H(200); var current = H(300);
        db.Hawalas.AddRange(first, second, current);
        var number = (await db.CorrespondentAccountPeriods.Where(x => x.CorrespondentId == corr).MaxAsync(x => (int?)x.PeriodNumber) ?? 0) + 1;
        var p1 = new CorrespondentAccountPeriod { CorrespondentId = corr, PeriodNumber = number, PeriodFrom = date, PeriodTo = date.AddDays(1), ClosedBy = fixture.UserId, ClosedAt = date.AddDays(2) };
        var p2 = new CorrespondentAccountPeriod { CorrespondentId = corr, PeriodNumber = number + 1, PeriodFrom = date.AddDays(2), PeriodTo = date.AddDays(3), ClosedBy = fixture.UserId, ClosedAt = date.AddDays(4) };
        db.CorrespondentAccountPeriods.AddRange(p1,p2);
        await db.SaveChangesAsync();
        db.CorrespondentAccountPeriodHawalas.AddRange(new() { PeriodId = p1.Id, HawalaId = first.Id }, new() { PeriodId = p2.Id, HawalaId = second.Id });
        await db.SaveChangesAsync();
        var service = fixture.CreateService(db);
        foreach (var pair in new[] { (Period: p1.Id, Hawala: first), (Period: p2.Id, Hawala: second), (Period: 0L, Hawala: current) })
        {
            var filter = new HawalaFilterDto { CorrespondentId = corr, SearchTerm = key, Extra = new() { PeriodId = pair.Period, MinNumber = 1, MaxNumber = 1 } };
            var list = await service.GetHawalasAsync(filter);
            Assert.Equal(pair.Hawala.Id, Assert.Single(list.Items).Id);
            var totals = await service.GetAmountSummaryAsync(new() { Filter = filter });
            Assert.Equal(pair.Hawala.ToAmount, Assert.Single(totals.Originals).Amount);
            var range = await service.GetCorrespondentRangeAsync(new() { CorrespondentId = corr, PeriodId = pair.Period, StartNumber = 1, EndNumber = 1 });
            Assert.Contains(range.Hawalas,x => x.Id == pair.Hawala.Id);
            Assert.DoesNotContain(range.Hawalas,x => x.Id != pair.Hawala.Id && new[] { first.Id, second.Id, current.Id }.Contains(x.Id));
        }
        Assert.Equal(3,(await service.GetHawalasAsync(new() { SearchTerm = key })).TotalCount);
        Assert.Empty((await service.GetHawalasAsync(new() { CorrespondentId = fixture.DestinationCorrespondent.Id, Extra = new() { PeriodId = p1.Id } })).Items);
        Assert.Equal(1,new HawalaExtraFilter { PeriodId = 0 }.Copy().Count);
        await tx.RollbackAsync();
    }
}
