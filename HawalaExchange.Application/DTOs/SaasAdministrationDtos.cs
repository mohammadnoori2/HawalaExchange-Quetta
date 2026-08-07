using System.ComponentModel.DataAnnotations;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.DTOs;

public sealed class SaasDashboardDto
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int TrialTenants { get; set; }
    public int SuspendedTenants { get; set; }
    public int ExpiredTenants { get; set; }
    public int ExpiringSoonTenants { get; set; }
    public int TotalUsers { get; set; }
    public decimal CollectedRevenueThisMonth { get; set; }
    public IReadOnlyList<SaasTenantDto> RecentTenants { get; set; } = [];
}

public sealed class SaasTenantDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public int UserCount { get; set; }
    public int BranchCount { get; set; }
    public long? SubscriptionId { get; set; }
    public long? PlanId { get; set; }
    public string? PlanName { get; set; }
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public DateTime? SubscriptionStartAt { get; set; }
    public DateTime? SubscriptionEndAt { get; set; }
    public int? RemainingDays { get; set; }
}

public sealed class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int TrialDays { get; set; }
    public int MaxUsers { get; set; }
    public int MaxBranches { get; set; }
    public long MaxStorageBytes { get; set; }
    public int MaxMonthlyTransactions { get; set; }
    public bool IncludesAdvancedReports { get; set; }
    public bool IncludesDocumentManagement { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class SaveSubscriptionPlanDto
{
    [Required, StringLength(50)]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "کد پلن فقط می‌تواند شامل حروف انگلیسی، عدد، خط تیره و زیرخط باشد.")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Range(0, double.MaxValue)]
    public decimal MonthlyPrice { get; set; }

    [Range(0, double.MaxValue)]
    public decimal AnnualPrice { get; set; }

    [Required, StringLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    [Range(0, 365)] public int TrialDays { get; set; }
    [Range(1, int.MaxValue)] public int MaxUsers { get; set; } = 5;
    [Range(1, int.MaxValue)] public int MaxBranches { get; set; } = 1;
    [Range(0, long.MaxValue)] public long MaxStorageBytes { get; set; }
    [Range(1, int.MaxValue)] public int MaxMonthlyTransactions { get; set; } = 1000;
    public bool IncludesAdvancedReports { get; set; }
    public bool IncludesDocumentManagement { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public sealed class ChangeSubscriptionDto
{
    [Range(1, long.MaxValue)] public long PlanId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public DateTime EndAt { get; set; } = DateTime.UtcNow.AddMonths(1);
    public DateTime? TrialEndAt { get; set; }
    public DateTime? GracePeriodEndAt { get; set; }
    public bool AutoRenew { get; set; }
    [Range(0, double.MaxValue)] public decimal AgreedPrice { get; set; }
    [Required, StringLength(10)] public string CurrencyCode { get; set; } = "USD";
    [StringLength(1000)] public string? AdministrativeNote { get; set; }
    [StringLength(500)] public string? SuspensionReason { get; set; }
}

public sealed class RecordSubscriptionPaymentDto
{
    [Range(1, long.MaxValue)] public long SubscriptionId { get; set; }
    [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    [Required, StringLength(10)] public string CurrencyCode { get; set; } = "USD";
    public DateTime DueAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public SubscriptionPaymentStatus Status { get; set; } = SubscriptionPaymentStatus.Paid;
    [StringLength(100)] public string? PaymentMethod { get; set; }
    [StringLength(150)] public string? ReferenceNumber { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
}
