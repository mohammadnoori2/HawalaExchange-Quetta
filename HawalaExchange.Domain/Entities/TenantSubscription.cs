using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("TenantSubscriptions")]
public sealed class TenantSubscription
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime? TrialEndAt { get; set; }
    public DateTime? GracePeriodEndAt { get; set; }
    public bool AutoRenew { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AgreedPrice { get; set; }

    [Required, MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    public DateTime? LastPaymentAt { get; set; }
    public DateTime? NextPaymentAt { get; set; }

    [MaxLength(1000)]
    public string? AdministrativeNote { get; set; }

    [MaxLength(500)]
    public string? SuspensionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Tenant Tenant { get; set; } = null!;
    public SubscriptionPlan Plan { get; set; } = null!;
    public ICollection<SubscriptionPayment> Payments { get; set; } = new List<SubscriptionPayment>();
    public ICollection<SubscriptionInvoice> Invoices { get; set; } = new List<SubscriptionInvoice>();
}
