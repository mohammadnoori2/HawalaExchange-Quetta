using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Web.Helpers;

public static class CommissionPreviewCalculator
{
    // The request is an isolated working copy owned by the calculation, not the live form.
    public static async Task<CorrespondentCommissionPreviewDto> CalculateAsync(
        CorrespondentCommissionPreviewRequestDto request,
        Func<CorrespondentCommissionPreviewRequestDto, CancellationToken, Task<CorrespondentCommissionPreviewDto>> previewAsync,
        CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(28));
        var preview = await previewAsync(request, budget.Token);
        if (request.CommissionScope != "Destination") return preview;

        var supplied = request.LocationCurrencyRates.Select(x => (x.PaymentLocationId, x.CurrencyId)).ToHashSet();
        var missing = preview.LocationCurrencyRates
            .Where(x => !supplied.Contains((x.PaymentLocationId, x.CurrencyId))).ToList();
        if (missing.Count == 0) return preview;

        foreach (var rate in missing)
            request.LocationCurrencyRates.Add(new LocationCurrencyCommissionRateDto
            {
                PaymentLocationId = rate.PaymentLocationId,
                CurrencyId = rate.CurrencyId,
                PerLakhRate = request.CommissionPerLakhAfn
            });

        // SQL discovery uses zero for unsupplied destination rates. Calculate again
        // with the proposed defaults so the very first displayed result is postable.
        preview = await previewAsync(request, budget.Token);
        supplied = request.LocationCurrencyRates.Select(x => (x.PaymentLocationId, x.CurrencyId)).ToHashSet();
        if (preview.LocationCurrencyRates.Any(x => !supplied.Contains((x.PaymentLocationId, x.CurrencyId))))
            throw new InvalidOperationException("حواله‌ها در جریان محاسبه تغییر کرده‌اند؛ دوباره نمایش حواله‌ها را بزنید.");
        return preview;
    }
}
