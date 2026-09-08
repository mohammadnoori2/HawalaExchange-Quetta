using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("HawalaImportBatches")]
    public class HawalaImportBatch : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long TenantId { get; set; }
        public long CorrespondentId { get; set; }
        [Required, MaxLength(260)] public string FileName { get; set; } = string.Empty;
        [Required, MaxLength(64)] public string FileHash { get; set; } = string.Empty;
        [Required, MaxLength(30)] public string Status { get; set; } = "Preview";
        public int RowCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public long CreatedBy { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public long? ConfirmedBy { get; set; }

        [ForeignKey(nameof(CorrespondentId))] public virtual Correspondent? Correspondent { get; set; }
        [ForeignKey(nameof(CreatedBy))] public virtual ApplicationUser? CreatedByUser { get; set; }
        [ForeignKey(nameof(ConfirmedBy))] public virtual ApplicationUser? ConfirmedByUser { get; set; }
        public virtual ICollection<HawalaImportRow> Rows { get; set; } = [];
    }
}
