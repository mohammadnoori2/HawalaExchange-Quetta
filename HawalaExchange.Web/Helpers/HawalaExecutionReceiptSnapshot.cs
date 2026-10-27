using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Web.Helpers;

public static class HawalaExecutionReceiptSnapshot
{
    public static HawalaDto WithDisplayDetails(HawalaDto paid, HawalaDto? source)
    {
        // The payment result is authoritative. Navigation labels may be absent when
        // its entity was mapped without loading related currencies/correspondent.
        if (source?.Id == paid.Id)
        {
            if (string.IsNullOrWhiteSpace(paid.CorrespondentName)) paid.CorrespondentName = source.CorrespondentName;
            if (string.IsNullOrWhiteSpace(paid.FromCurrencyCode)) paid.FromCurrencyCode = source.FromCurrencyCode;
            if (string.IsNullOrWhiteSpace(paid.FromCurrencyName)) paid.FromCurrencyName = source.FromCurrencyName;
            if (string.IsNullOrWhiteSpace(paid.ToCurrencyCode)) paid.ToCurrencyCode = source.ToCurrencyCode;
            if (string.IsNullOrWhiteSpace(paid.ToCurrencyName)) paid.ToCurrencyName = source.ToCurrencyName;
        }
        return paid;
    }
}
