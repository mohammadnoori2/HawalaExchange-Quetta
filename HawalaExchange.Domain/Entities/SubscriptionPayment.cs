using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("SubscriptionPayments")]
public sealed class SubscriptionPayment
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public long SubscriptionId { get; set; }
    public long? InvoiceId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required, MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    public SubscriptionPaymentStatus Status { get; set; } = SubscriptionPaymentStatus.Pending;
    public DateTime DueAt { get; set; }
    public DateTime? PaidAt { get; set; }

    [MaxLength(100)]
    public string? PaymentMethod { get; set; }

    [MaxLength(150)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(100)]
    public string? ProviderName { get; set; }

    [MaxLength(200)]
    public string? ProviderTransactionId { get; set; }

    [MaxLength(100)]
    public string? ReceiptNumber { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public TenantSubscription Subscription { get; set; } = null!;
    public SubscriptionInvoice? Invoice { get; set; }
}
