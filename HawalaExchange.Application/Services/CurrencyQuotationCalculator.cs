namespace HawalaExchange.Application.Services;

public static class CurrencyQuotationCalculator
{
    public static CurrencyConversionResult ConvertFromAmount(
        long fromCurrencyId,
        string fromCurrencyCode,
        int fromPriority,
        decimal fromAmount,
        long toCurrencyId,
        string toCurrencyCode,
        int toPriority,
        decimal rate)
    {
        if (fromCurrencyId == toCurrencyId)
            throw new InvalidOperationException("The currencies must be different.");
        if (fromAmount <= 0)
            throw new InvalidOperationException("The source amount must be greater than zero.");
        if (rate <= 0)
            throw new InvalidOperationException("The exchange rate must be greater than zero.");

        var fromIsBase = IsCanonicalBase(
            fromCurrencyCode, fromPriority,
            toCurrencyCode, toPriority);

        return fromIsBase
            ? new CurrencyConversionResult(
                fromCurrencyId, fromCurrencyCode,
                toCurrencyId, toCurrencyCode,
                fromAmount * rate)
            : new CurrencyConversionResult(
                toCurrencyId, toCurrencyCode,
                fromCurrencyId, fromCurrencyCode,
                fromAmount / rate);
    }

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

        var fromIsBase = IsCanonicalBase(
            fromCurrencyCode, fromPriority,
            toCurrencyCode, toPriority);

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

    private static bool IsCanonicalBase(
        string fromCurrencyCode,
        int fromPriority,
        string toCurrencyCode,
        int toPriority) =>
        fromPriority < toPriority ||
        (fromPriority == toPriority &&
         string.Compare(fromCurrencyCode, toCurrencyCode, StringComparison.OrdinalIgnoreCase) < 0);
}

public sealed record CurrencyConversionResult(
    long BaseCurrencyId,
    string BaseCurrencyCode,
    long QuoteCurrencyId,
    string QuoteCurrencyCode,
    decimal ToAmount);

public sealed record CurrencyQuotationResult(
    long BaseCurrencyId,
    string BaseCurrencyCode,
    long QuoteCurrencyId,
    string QuoteCurrencyCode,
    decimal Rate);
