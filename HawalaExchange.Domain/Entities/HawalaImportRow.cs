using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("HawalaImportRows")]
    public class HawalaImportRow : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long TenantId { get; set; }
        public long BatchId { get; set; }
        public int ExcelRowNumber { get; set; }
        public long? HawalaNumber { get; set; }
        [MaxLength(100)] public string? ReferenceNumber { get; set; }
        [MaxLength(200)] public string? SenderName { get; set; }
        [MaxLength(200)] public string? ReceiverName { get; set; }
        [MaxLength(200)] public string? PaymentLocationText { get; set; }
        public long? PaymentLocationId { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? Amount { get; set; }
        [MaxLength(10)] public string? CurrencyCode { get; set; }
        public long? CurrencyId { get; set; }
        [MaxLength(1000)] public string? ValidationErrors { get; set; }
        public long? HawalaId { get; set; }

        [ForeignKey(nameof(BatchId))] public virtual HawalaImportBatch? Batch { get; set; }
        [ForeignKey(nameof(PaymentLocationId))] public virtual PaymentLocation? PaymentLocation { get; set; }
        [ForeignKey(nameof(CurrencyId))] public virtual Currency? Currency { get; set; }
        [ForeignKey(nameof(HawalaId))] public virtual Hawala? Hawala { get; set; }
    }
}
