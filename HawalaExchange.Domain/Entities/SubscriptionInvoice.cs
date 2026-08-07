using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("SubscriptionInvoices")]
public sealed class SubscriptionInvoice
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    [Required, MaxLength(30)] public string InvoiceNumber { get; set; } = string.Empty;
    public long TenantId { get; set; }
    public long SubscriptionId { get; set; }
    public SubscriptionInvoiceStatus Status { get; set; } = SubscriptionInvoiceStatus.Issued;
    public DateTime IssuedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime ServicePeriodStart { get; set; }
    public DateTime ServicePeriodEnd { get; set; }
    public DateTime? PaidAt { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PaidAmount { get; set; }
    [Required, MaxLength(10)] public string CurrencyCode { get; set; } = "USD";
    [Required, MaxLength(200)] public string BillingName { get; set; } = string.Empty;
    [MaxLength(256)] public string? BillingEmail { get; set; }
    [MaxLength(50)] public string? BillingPhone { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool AutoRenewOnPayment { get; set; }
    public DateTime? RenewalAppliedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public Tenant Tenant { get; set; } = null!;
    public TenantSubscription Subscription { get; set; } = null!;
    public ICollection<SubscriptionInvoiceItem> Items { get; set; } = new List<SubscriptionInvoiceItem>();
    public ICollection<SubscriptionPayment> Payments { get; set; } = new List<SubscriptionPayment>();
}

[Table("SubscriptionInvoiceItems")]
public sealed class SubscriptionInvoiceItem
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long InvoiceId { get; set; }
    [Required, MaxLength(500)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")] public decimal Quantity { get; set; } = 1;
    [Column(TypeName = "decimal(18,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }
    public SubscriptionInvoice Invoice { get; set; } = null!;
}

[Table("BillingNumberSequences")]
public sealed class BillingNumberSequence
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public int Year { get; set; }
    [Required, MaxLength(10)] public string Prefix { get; set; } = "INV";
    public long NextValue { get; set; } = 1;
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
