using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Transfers")]
    public class Transfer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long FromAccountId { get; set; }

        [Required]
        public long ToAccountId { get; set; }

        [Required]
        public long CurrencyId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransferMethod { get; set; } // Cash, Bank, Hawala

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        [ForeignKey(nameof(FromAccountId))]
        public virtual Account? FromAccount { get; set; }

        [ForeignKey(nameof(ToAccountId))]
        public virtual Account? ToAccount { get; set; }

        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }

        public virtual ICollection<LedgerEntry>? LedgerEntries { get; set; } = new List<LedgerEntry>();
    }
}