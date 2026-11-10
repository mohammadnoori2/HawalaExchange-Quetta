using System.Text.Json;
using System.Text.Json.Nodes;
using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Web.Helpers;

public static class HawalaDestinationEdit
{
    public static string Snapshot(CreateHawalaDto value)
    {
        var fields = JsonSerializer.SerializeToNode(value)!.AsObject();
        // These fields are changed together by the existing location selector.
        fields.Remove(nameof(value.PaymentLocationId));
        fields.Remove(nameof(value.FromAccountId));
        fields.Remove(nameof(value.GeneratedSendHawalaNumber));
        return fields.ToJsonString();
    }
}
