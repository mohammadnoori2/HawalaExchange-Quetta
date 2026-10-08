using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

public sealed class HawalaPaymentBatch : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public DateTime ExecutedAt { get; set; }
    public long ExecutedBy { get; set; }
    public long PaidFromAccountId { get; set; }
    [MaxLength(200)] public string AccountName { get; set; } = "";
    [MaxLength(200)] public string ExecutedByName { get; set; } = "";
    public List<HawalaPaymentBatchItem> Items { get; set; } = [];
}

public sealed class HawalaPaymentBatchItem : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long BatchId { get; set; }
    [ForeignKey(nameof(BatchId))] public HawalaPaymentBatch Batch { get; set; } = default!;
    // Historical identifiers and snapshots deliberately have no FK to editable/deletable hawalas.
    public long HawalaId { get; set; }
    public long Number { get; set; }
    [MaxLength(100)] public string ReferenceNumber { get; set; } = "";
    [MaxLength(200)] public string SenderName { get; set; } = "";
    [MaxLength(200)] public string ReceiverName { get; set; } = "";
    [MaxLength(200)] public string CorrespondentName { get; set; } = "";
    [MaxLength(200)] public string PaymentLocation { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
    public long CurrencyId { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "";
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public long? CommissionCurrencyId { get; set; }
    [MaxLength(10)] public string CommissionCurrencyCode { get; set; } = "";
    [Column(TypeName = "decimal(18,2)")] public decimal AgentCommission { get; set; }
}
