using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Web.Helpers;

/// <summary>Applies only the latest paid-receipt location selection to its original form.</summary>
public sealed class HawalaPaymentAccountSelection
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private int version;
    private int resolvingVersion = -1;
    public bool IsResolving => resolvingVersion == version;
    public void Invalidate() => version++;

    public async Task<AccountDto?> ResolveAsync(CreateHawalaDto model, Func<CreateHawalaDto> current,
        Func<long, Task<AccountDto?>> resolve)
    {
        var request = ++version;
        if (model.HawalaType != "HawalaReceive" || model.Status != "Paid") return null;
        model.FromAccountId = null;
        model.GeneratedSendHawalaNumber = null;
        if (!model.PaymentLocationId.HasValue) return null;
        var location = model.PaymentLocationId.Value;
        bool IsCurrent() => request == version && ReferenceEquals(model, current()) &&
            model.HawalaType == "HawalaReceive" && model.Status == "Paid" && model.PaymentLocationId == location;
        resolvingVersion = request;
        await gate.WaitAsync();
        try
        {
            if (!IsCurrent()) return null;
            var account = await resolve(location);
            if (!IsCurrent()) return null;
            if (account is null || account.IsArchived || account.Id <= 0)
                throw new InvalidOperationException("حساب فعال نمایندگی مسئول یافت نشد.");
            model.FromAccountId = account.Id;
            return account;
        }
        catch (Exception) when (!IsCurrent()) { return null; }
        finally { if (resolvingVersion == request) resolvingVersion = -1; gate.Release(); }
    }
}
