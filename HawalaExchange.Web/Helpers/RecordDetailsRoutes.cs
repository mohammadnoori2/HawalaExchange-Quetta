using System.Globalization;

namespace HawalaExchange.Web.Helpers;

public static class RecordDetailsRoutes
{
    public static string? Get(string? kind, long? id)
    {
        if (id is null or <= 0) return null;
        var prefix = kind switch
        {
            "Account" => "/accounts/", "Customer" => "/customers/",
            "Correspondent" => "/correspondents/", "PaymentLocation" => "/payment-locations/",
            "Branch" => "/branches/", "Currency" => "/currencies/", _ => null
        };
        return prefix is null ? null : prefix + id.Value.ToString(CultureInfo.InvariantCulture);
    }
}
