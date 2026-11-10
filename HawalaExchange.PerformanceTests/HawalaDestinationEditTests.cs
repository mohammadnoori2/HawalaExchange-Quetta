using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Helpers;

namespace HawalaExchange.PerformanceTests;

public class HawalaDestinationEditTests
{
    [Fact]
    public void Location_and_automatically_selected_destination_do_not_change_source_snapshot()
    {
        var value = new CreateHawalaDto { PaymentLocationId = 1, FromAccountId = 2, FromAmount = 70000 };
        var before = HawalaDestinationEdit.Snapshot(value);
        value.PaymentLocationId = 3; value.FromAccountId = 4; value.GeneratedSendHawalaNumber = 12;
        Assert.Equal(before, HawalaDestinationEdit.Snapshot(value));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("notes")]
    [InlineData("owner")]
    public void Other_edits_cannot_be_silently_discarded(string field)
    {
        var value = new CreateHawalaDto { FromAmount = 70000 };
        var before = HawalaDestinationEdit.Snapshot(value);
        if (field == "amount") value.FromAmount++;
        if (field == "notes") value.Notes = "changed";
        if (field == "owner") value.CorrespondentId = 99;
        Assert.NotEqual(before, HawalaDestinationEdit.Snapshot(value));
    }
}
