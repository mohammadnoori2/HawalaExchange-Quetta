using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("CorrespondentSettlementConversions")]
public sealed class CorrespondentSettlementConversion : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long TenantId { get; set; }
    public long CorrespondentId { get; set; }
    public long TargetCurrencyId { get; set; }
    public long TransactionId { get; set; }
    [Required, MaxLength(20)] public string SourceMode { get; set; } = "Hawalas";
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public Correspondent Correspondent { get; set; } = null!;
    public Currency TargetCurrency { get; set; } = null!;
    public Transaction Transaction { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<CorrespondentSettlementConversionItem> Items { get; set; } = [];
    public ICollection<CorrespondentSettlementConversionHawala> Hawalas { get; set; } = [];
}

[Table("CorrespondentSettlementConversionItems")]
public sealed class CorrespondentSettlementConversionItem : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    public long TenantId { get; set; }
    public long ConversionId { get; set; }
    public long SourceCurrencyId { get; set; }
    [Column(TypeName="decimal(18,4)")] public decimal SourceTalabKar { get; set; }
    [Column(TypeName="decimal(18,4)")] public decimal SourceBadehKar { get; set; }
    [Column(TypeName="decimal(18,8)")] public decimal ExchangeRate { get; set; }
    [Column(TypeName="decimal(18,4)")] public decimal TargetTalabKar { get; set; }
    [Column(TypeName="decimal(18,4)")] public decimal TargetBadehKar { get; set; }
    public CorrespondentSettlementConversion Conversion { get; set; } = null!;
    public Currency SourceCurrency { get; set; } = null!;
}

[Table("CorrespondentSettlementConversionHawalas")]
public sealed class CorrespondentSettlementConversionHawala : ITenantEntity
{
    public long TenantId { get; set; }
    public long ConversionId { get; set; }
    public long HawalaId { get; set; }
    public CorrespondentSettlementConversion Conversion { get; set; } = null!;
    public Hawala Hawala { get; set; } = null!;
}
