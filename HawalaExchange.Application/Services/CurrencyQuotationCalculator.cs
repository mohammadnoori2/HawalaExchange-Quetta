namespace HawalaExchange.Application.Services;

public static class CurrencyQuotationCalculator
{
    public static CurrencyQuotationResult Calculate(
        long fromCurrencyId,
        string fromCurrencyCode,
        int fromPriority,
        decimal fromAmount,
        long toCurrencyId,
        string toCurrencyCode,
        int toPriority,
        decimal toAmount)
    {
        if (fromCurrencyId == toCurrencyId)
            throw new InvalidOperationException("The currencies must be different.");
        if (fromAmount <= 0 || toAmount <= 0)
            throw new InvalidOperationException("Both currency amounts must be greater than zero.");

        var fromIsBase = fromPriority < toPriority ||
            (fromPriority == toPriority &&
             string.Compare(fromCurrencyCode, toCurrencyCode, StringComparison.OrdinalIgnoreCase) < 0);

        return fromIsBase
            ? new CurrencyQuotationResult(
                fromCurrencyId, fromCurrencyCode,
                toCurrencyId, toCurrencyCode,
                toAmount / fromAmount)
            : new CurrencyQuotationResult(
                toCurrencyId, toCurrencyCode,
                fromCurrencyId, fromCurrencyCode,
                fromAmount / toAmount);
    }
}

public sealed record CurrencyQuotationResult(
    long BaseCurrencyId,
    string BaseCurrencyCode,
    long QuoteCurrencyId,
    string QuoteCurrencyCode,
    decimal Rate);
