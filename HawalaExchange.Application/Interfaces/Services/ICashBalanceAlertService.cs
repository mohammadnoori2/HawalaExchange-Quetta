using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ICashBalanceAlertService
{
    event Action? AlertsChanged;
    Task<IReadOnlyList<CashBalanceAlertSettingDto>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<CashBalanceAlertSettingDto?> GetSettingAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashBalanceAlertDto>> GetActiveAlertsAsync(bool onlyForCurrentUser = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashBalanceAlertUserDto>> GetRecipientUsersAsync(CancellationToken cancellationToken = default);
    Task<CashBalanceAlertSettingDto> CreateAsync(SaveCashBalanceAlertSettingDto dto, CancellationToken cancellationToken = default);
    Task<CashBalanceAlertSettingDto> UpdateAsync(long id, SaveCashBalanceAlertSettingDto dto, CancellationToken cancellationToken = default);
    Task SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default);
}
