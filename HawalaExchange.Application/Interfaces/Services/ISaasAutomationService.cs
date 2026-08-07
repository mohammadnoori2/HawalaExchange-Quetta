using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services;
public interface ISaasAutomationService
{
    Task<SaasAutomationDashboardDto> GetDashboardAsync(string? search = null, SaasNotificationType? type = null, CancellationToken cancellationToken = default);
    Task<SaasAutomationSettingsDto> SaveSettingsAsync(SaasAutomationSettingsDto dto, CancellationToken cancellationToken = default);
    Task<SaasAutomationRunDto> RunAsync(bool force = false, CancellationToken cancellationToken = default);
    Task MarkReadAsync(long notificationId, CancellationToken cancellationToken = default);
}
public interface IPlatformMessageSender
{
    Task SendEmailAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default);
    bool IsConfigured { get; }
}
