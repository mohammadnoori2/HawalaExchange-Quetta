using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("CorrespondentCommissionBatches")]
public sealed class CorrespondentCommissionBatch : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long TenantId { get; set; }
    public long CorrespondentId { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal CommissionPerLakhAfn { get; set; }
    [Column(TypeName = "decimal(18,8)")] public decimal UsdToAfnRate { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal TotalBaseAfn { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal TotalCommissionAfn { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal TotalCommissionUsd { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Posted";
    public long PostingTransactionId { get; set; }
    public long? ReversalTransactionId { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? ReversedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    [MaxLength(500)] public string? ReversalReason { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];

    public Correspondent Correspondent { get; set; } = null!;
    public Transaction PostingTransaction { get; set; } = null!;
    public Transaction? ReversalTransaction { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ApplicationUser? ReversedByUser { get; set; }
    public ICollection<CorrespondentCommissionBatchItem> Items { get; set; } = [];
}

[Table("CorrespondentCommissionBatchItems")]
public sealed class CorrespondentCommissionBatchItem : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long TenantId { get; set; }
    public long BatchId { get; set; }
    public long HawalaId { get; set; }
    public long SourceCurrencyId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal SourceAmount { get; set; }
    [Column(TypeName = "decimal(18,8)")] public decimal SourceToAfnRate { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal AfnEquivalent { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal CommissionAfn { get; set; }
    public bool IsActive { get; set; } = true;

    public CorrespondentCommissionBatch Batch { get; set; } = null!;
    public Hawala Hawala { get; set; } = null!;
    public Currency SourceCurrency { get; set; } = null!;
}
