using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
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

        [Required]
        public decimal BadehkarLimit { get; set; } // Credit limit

        public bool IsActive { get; set; } = true;

        public long? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey(nameof(AccountId))]
        public virtual Account? Account { get; set; }

        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }

        [ForeignKey(nameof(CreatedBy))]
        public virtual ApplicationUser? CreatedByUser { get; set; }
    }
}