using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("AedDeals")]
public sealed class AedDeal : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long TenantId { get; set; }
    [Required, MaxLength(50)] public string DealNumber { get; set; } = string.Empty;
    public long SourceCorrespondentId { get; set; }
    public long DubaiCorrespondentId { get; set; }
    public long SourceCurrencyId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal OriginalAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal ConvertedAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal TotalFinalUsd { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal TotalProfitUsd { get; set; }
    [Column(TypeName = "decimal(18,8)")] public decimal AedPerUsdRate { get; set; } = 3.67m;
    public int RoundingDecimalPlaces { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Held";
    public long HoldingTransactionId { get; set; }
    public long? ReversalTransactionId { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    [MaxLength(500)] public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? CancelledBy { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];

    public Correspondent SourceCorrespondent { get; set; } = null!;
    public Correspondent DubaiCorrespondent { get; set; } = null!;
    public Currency SourceCurrency { get; set; } = null!;
    public Transaction HoldingTransaction { get; set; } = null!;
    public Transaction? ReversalTransaction { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ApplicationUser? CancelledByUser { get; set; }
    public ICollection<AedDealConversion> Conversions { get; set; } = [];
}

[Table("AedDealConversions")]
public sealed class AedDealConversion : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long TenantId { get; set; }
    public long AedDealId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal SourceAmount { get; set; }
    [Column(TypeName = "decimal(18,8)")] public decimal AedPerUsdRate { get; set; } = 3.67m;
    [Column(TypeName = "decimal(18,4)")] public decimal ActualMarker { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal DeclaredMarker { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal ActualAdjustmentSource { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal DeclaredAdjustmentSource { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal FinalUsdAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal DeclaredUsdAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal ProfitUsd { get; set; }
    public long PostingTransactionId { get; set; }
    public long? ReversalTransactionId { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Posted";
    [MaxLength(500)] public string? Note { get; set; }
    [MaxLength(500)] public string? ReversalReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];

    public AedDeal Deal { get; set; } = null!;
    public Transaction PostingTransaction { get; set; } = null!;
    public Transaction? ReversalTransaction { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ApplicationUser? ReversedByUser { get; set; }
}
