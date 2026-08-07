using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

public enum SaasNotificationType { SubscriptionExpiring = 1, SubscriptionExpired = 2, GracePeriodEnding = 3, InvoiceCreated = 4, PaymentOverdue = 5, QuotaWarning = 6, SubscriptionRenewed = 7, SubscriptionSuspended = 8 }
public enum SaasNotificationSeverity { Info = 1, Warning = 2, Critical = 3, Success = 4 }
public enum SaasNotificationDeliveryStatus { Pending = 1, Sent = 2, Skipped = 3, Failed = 4 }
public enum SaasAutomationRunStatus { Running = 1, Completed = 2, Failed = 3, Skipped = 4 }

[Table("SaasAutomationSettings")]
public sealed class SaasAutomationSettings
{
    [Key] public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public bool AutoCreateInvoices { get; set; } = true;
    public bool SendEmailNotifications { get; set; }
    public int RunIntervalMinutes { get; set; } = 360;
    public int ExpiryWarningDays { get; set; } = 30;
    public int GracePeriodDays { get; set; } = 7;
    public int GraceWarningDays { get; set; } = 2;
    public int InvoiceLeadDays { get; set; } = 7;
    public int InvoiceDueDays { get; set; } = 7;
    public int QuotaWarningPercent { get; set; } = 80;
    public DateTime? LastRunAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

[Table("SaasNotifications")]
public sealed class SaasNotification
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long? TenantId { get; set; }
    public long? SubscriptionId { get; set; }
    public long? InvoiceId { get; set; }
    public SaasNotificationType Type { get; set; }
    public SaasNotificationSeverity Severity { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string Message { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string DeduplicationKey { get; set; } = string.Empty;
    [MaxLength(256)] public string? RecipientEmail { get; set; }
    public SaasNotificationDeliveryStatus DeliveryStatus { get; set; } = SaasNotificationDeliveryStatus.Pending;
    [MaxLength(2000)] public string? DeliveryError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public Tenant? Tenant { get; set; }
    public TenantSubscription? Subscription { get; set; }
    public SubscriptionInvoice? Invoice { get; set; }
}

[Table("SaasAutomationRuns")]
public sealed class SaasAutomationRun
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    [Required, MaxLength(100)] public string JobName { get; set; } = "subscription-automation";
    [Required, MaxLength(150)] public string RunKey { get; set; } = string.Empty;
    public SaasAutomationRunStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ProcessedSubscriptions { get; set; }
    public int CreatedInvoices { get; set; }
    public int CreatedNotifications { get; set; }
    [MaxLength(4000)] public string? Error { get; set; }
}
