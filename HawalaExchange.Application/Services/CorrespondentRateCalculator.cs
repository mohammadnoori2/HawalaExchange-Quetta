namespace HawalaExchange.Application.Services;

/// <summary>Maps the daily USD/AFN rate to the canonical quotation used by settlement.</summary>
public static class CorrespondentRateCalculator
{
    public static bool Supports(string sourceCode, string targetCode) =>
        (sourceCode.Equals("USD", StringComparison.OrdinalIgnoreCase) && targetCode.Equals("AFN", StringComparison.OrdinalIgnoreCase)) ||
        (sourceCode.Equals("AFN", StringComparison.OrdinalIgnoreCase) && targetCode.Equals("USD", StringComparison.OrdinalIgnoreCase));

    public static decimal ToSettlementRate(string sourceCode, int sourcePriority,
        string targetCode, int targetPriority, decimal usdToAfnRate)
    {
        if (!Supports(sourceCode, targetCode))
            throw new ArgumentException("Only USD/AFN daily rates are supported.");
        var usdPriority = sourceCode.Equals("USD", StringComparison.OrdinalIgnoreCase) ? sourcePriority : targetPriority;
        var afnPriority = sourceCode.Equals("AFN", StringComparison.OrdinalIgnoreCase) ? sourcePriority : targetPriority;
        return decimal.Round(CurrencyQuotationCalculator.Calculate(
            1, "USD", usdPriority, 1, 2, "AFN", afnPriority, usdToAfnRate).Rate,
            8, MidpointRounding.AwayFromZero);
    }

    public static decimal ToDailyRate(string sourceCode, int sourcePriority,
        string targetCode, int targetPriority, decimal settlementRate)
    {
        if (!Supports(sourceCode, targetCode))
            throw new ArgumentException("Only USD/AFN daily rates are supported.");
        var usdPriority = sourceCode.Equals("USD", StringComparison.OrdinalIgnoreCase) ? sourcePriority : targetPriority;
        var afnPriority = sourceCode.Equals("AFN", StringComparison.OrdinalIgnoreCase) ? sourcePriority : targetPriority;
        return decimal.Round(CurrencyQuotationCalculator.ConvertFromAmount(
            1, "USD", usdPriority, 1, 2, "AFN", afnPriority, settlementRate).ToAmount,
            8, MidpointRounding.AwayFromZero);
    }
}
