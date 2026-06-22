using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
{
    [Table("AccountBadehkarLimits")]
    public class AccountBadehkarLimit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long AccountId { get; set; }

        [Required]
        public long CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal BadehkarLimit { get; set; }

        public bool IsActive { get; set; } = true;

        public long? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey("AccountId")]
        public virtual Account Account { get; set; }

        [ForeignKey("CurrencyId")]
        public virtual Currency Currency { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; }
    }
}