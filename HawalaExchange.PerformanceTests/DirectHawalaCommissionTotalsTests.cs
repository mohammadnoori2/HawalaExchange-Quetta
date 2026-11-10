using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class DirectHawalaCommissionTotalsTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Totals_cover_all_filtered_pages_separate_fee_directions_and_keep_preserved_cancelled_fees()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var name = "Direct totals " + Guid.NewGuid().ToString("N");
        for (var i = 0; i < 4; i++) db.Hawalas.Add(new Hawala { Number = 399000001 + i,
            HawalaType = "HawalaSend", CorrespondentId = fixture.DestinationCorrespondent.Id,
            FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 1000, ToAmount = 1000,
            CommissionAmount = i < 2 ? i + 3 : null, CommissionCurrencyId = 2,
            AgentCommissionAmount = i == 0 ? 100 : i == 1 ? 200 : i == 2 ? 900 : 5,
            AgentCommissionCurrencyId = i == 3 ? 2 : 1, SenderName = name, ReceiverName = "Receiver",
            Status = i < 2 ? "Paid" : "Cancel", CancellationAccountingMode = i == 3 ? "Immediate" : null, CreatedBy = fixture.UserId });
        await db.SaveChangesAsync();
        var filter = new HawalaFilterDto { CorrespondentId = fixture.DestinationCorrespondent.Id, HawalaType = "HawalaSend", SearchTerm = name, PageSize = 1, IncludeDirectCommissionTotals = true };
        var result = await fixture.CreateService(db).GetHawalasAsync(filter);
        Assert.Single(result.Items); Assert.Equal(4, result.TotalCount);
        var afn = Assert.Single(result.DirectCommissionTotals.Where(x => x.Currency == "AFN"));
        Assert.Equal(300, afn.Paid); Assert.Equal(0, afn.Received);
        var usd = Assert.Single(result.DirectCommissionTotals.Where(x => x.Currency == "USD"));
        Assert.Equal(7, usd.Received); Assert.Equal(5, usd.Paid);
        filter.Status = "Paid";
        Assert.Equal(0, (await fixture.CreateService(db).GetHawalasAsync(filter)).DirectCommissionTotals.Single(x => x.Currency == "USD").Paid);
        filter.IncludeDirectCommissionTotals = false;
        Assert.Empty((await fixture.CreateService(db).GetHawalasAsync(filter)).DirectCommissionTotals);
    }
}
