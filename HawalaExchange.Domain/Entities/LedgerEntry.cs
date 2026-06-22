using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
{
    [Table("LedgerEntries")]
    public class LedgerEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long TransactionId { get; set; }

        [Required]
        public long AccountId { get; set; }

        [Required]
        public long CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal TalabKar { get; set; } = 0;

        [Column(TypeName = "decimal(18,4)")]
        public decimal BadehKar { get; set; } = 0;

        [MaxLength(500)]
        public string Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey("TransactionId")]
        public virtual Transaction Transaction { get; set; }

        [ForeignKey("AccountId")]
        public virtual Account Account { get; set; }

        [ForeignKey("CurrencyId")]
        public virtual Currency Currency { get; set; }
    }
}