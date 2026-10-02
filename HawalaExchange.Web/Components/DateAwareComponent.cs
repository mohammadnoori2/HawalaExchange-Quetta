using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;

namespace HawalaExchange.Web.Components;

public abstract class DateAwareComponent : ComponentBase
{
    [Inject] protected AppDateDisplay DisplayDates { get; set; } = default!;

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        // Apply the original view synchronously: it preserves cascading parameter metadata
        // and must not be retained across an await (its renderer-owned lifetime is short).
        parameters.SetParameterProperties(this);
        await DisplayDates.EnsureLoadedAsync();
        // Parameters are already assigned; run normal lifecycle methods without reclassifying cascades.
        await base.SetParametersAsync(ParameterView.Empty);
    }
}
