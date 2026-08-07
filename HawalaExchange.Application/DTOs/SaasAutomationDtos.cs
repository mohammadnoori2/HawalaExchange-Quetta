using System.ComponentModel.DataAnnotations;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.DTOs;

public sealed class SaasAutomationSettingsDto
{
    public bool IsEnabled { get; set; } = true; public bool AutoCreateInvoices { get; set; } = true; public bool SendEmailNotifications { get; set; }
    [Range(5, 1440)] public int RunIntervalMinutes { get; set; } = 360;
    [Range(1, 180)] public int ExpiryWarningDays { get; set; } = 30;
    [Range(0, 90)] public int GracePeriodDays { get; set; } = 7;
    [Range(0, 30)] public int GraceWarningDays { get; set; } = 2;
    [Range(0, 60)] public int InvoiceLeadDays { get; set; } = 7;
    [Range(0, 60)] public int InvoiceDueDays { get; set; } = 7;
    [Range(1, 100)] public int QuotaWarningPercent { get; set; } = 80;
    public DateTime? LastRunAt { get; set; }
}
public sealed class SaasNotificationDto
{
    public long Id { get; set; } public string TenantName { get; set; } = string.Empty; public SaasNotificationType Type { get; set; }
    public SaasNotificationSeverity Severity { get; set; } public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty;
    public SaasNotificationDeliveryStatus DeliveryStatus { get; set; } public DateTime CreatedAt { get; set; } public DateTime? ReadAt { get; set; }
}
public sealed class SaasAutomationRunDto
{
    public long Id { get; set; } public string RunKey { get; set; } = string.Empty; public SaasAutomationRunStatus Status { get; set; }
    public DateTime StartedAt { get; set; } public DateTime? CompletedAt { get; set; } public int ProcessedSubscriptions { get; set; }
    public int CreatedInvoices { get; set; } public int CreatedNotifications { get; set; } public string? Error { get; set; }
}
public sealed class SaasAutomationDashboardDto
{
    public SaasAutomationSettingsDto Settings { get; set; } = new(); public IReadOnlyList<SaasNotificationDto> Notifications { get; set; } = [];
    public IReadOnlyList<SaasAutomationRunDto> Runs { get; set; } = []; public int UnreadCount { get; set; } public int CriticalCount { get; set; }
}
