using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;

namespace HawalaExchange.Web.Services;

public sealed class UiReferenceDataCache(
    ICurrentTenant currentTenant,
    ICurrencyService currencyService,
    IPaymentLocationService paymentLocationService,
    ICompanySettingService companySettingService)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim gate = new(1, 1);
    private long tenantId;
    private DateTime expiresAt;
    private IReadOnlyList<CurrencyDto>? activeCurrencies;
    private IReadOnlyList<CurrencyDto>? allCurrencies;
    private IReadOnlyList<PaymentLocationDto>? paymentLocations;
    private CompanySettingDto? companySetting;

    public Task<IReadOnlyList<CurrencyDto>> GetActiveCurrenciesAsync() =>
        GetAsync<IReadOnlyList<CurrencyDto>>(() => activeCurrencies, value => activeCurrencies = value,
            async () => (await currencyService.GetActiveCurrenciesAsync()).ToList());

    public Task<IReadOnlyList<CurrencyDto>> GetAllCurrenciesAsync() =>
        GetAsync<IReadOnlyList<CurrencyDto>>(() => allCurrencies, value => allCurrencies = value,
            async () => (await currencyService.GetAllAsync()).ToList());

    public Task<IReadOnlyList<PaymentLocationDto>> GetPaymentLocationsAsync() =>
        GetAsync<IReadOnlyList<PaymentLocationDto>>(() => paymentLocations, value => paymentLocations = value,
            async () => (await paymentLocationService.GetAllAsync()).ToList());

    public Task<CompanySettingDto> GetCompanySettingAsync() =>
        GetAsync(() => companySetting, value => companySetting = value, companySettingService.GetAsync);

    public void Invalidate()
    {
        activeCurrencies = null;
        allCurrencies = null;
        paymentLocations = null;
        companySetting = null;
        expiresAt = default;
    }

    private async Task<T> GetAsync<T>(Func<T?> read, Action<T> write, Func<Task<T>> load)
        where T : class
    {
        ResetForTenantOrExpiry();
        var cached = read();
        if (cached is not null)
            return cached;

        await gate.WaitAsync();
        try
        {
            ResetForTenantOrExpiry();
            cached = read();
            if (cached is not null)
                return cached;

            var value = await load();
            write(value);
            expiresAt = DateTime.UtcNow.Add(Lifetime);
            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    private void ResetForTenantOrExpiry()
    {
        if (tenantId == currentTenant.TenantId && DateTime.UtcNow < expiresAt)
            return;

        tenantId = currentTenant.TenantId;
        Invalidate();
    }
}
