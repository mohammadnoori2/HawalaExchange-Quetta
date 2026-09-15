using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("PaymentLocations")]
    public class PaymentLocation : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TenantId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string NormalizedName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Address { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(200)]
        public string? ContactPerson { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public long CreatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public long? UpdatedBy { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(CreatedBy))]
        public virtual ApplicationUser? CreatedByUser { get; set; }

        [ForeignKey(nameof(UpdatedBy))]
        public virtual ApplicationUser? UpdatedByUser { get; set; }

        // ارتباط با حواله‌ها (یک آدرس می‌تواند در چند حواله استفاده شود)
        public virtual ICollection<Hawala>? Hawalas { get; set; }
        public virtual ICollection<PaymentLocationAlias> Aliases { get; set; } = [];
    }
}
