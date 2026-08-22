using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Transfers")]
    public class Transfer : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TenantId { get; set; }

        [Required]
        public long FromAccountId { get; set; }

        [Required]
        public long ToAccountId { get; set; }

        [Required]
        public long CurrencyId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        /// <summary>
        /// Reporting currency used to carry an inbound correspondent transfer at historical cost.
        /// </summary>
        public long? ProfitCurrencyId { get; set; }

        /// <summary>
        /// Total historical carrying value of the transferred currency in <see cref="ProfitCurrencyId"/>.
        /// This is only populated when currency enters an internal account from a correspondent account.
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? ProfitCurrencyAmount { get; set; }

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

        [ForeignKey(nameof(ProfitCurrencyId))]
        public virtual Currency? ProfitCurrency { get; set; }

        public virtual ICollection<LedgerEntry>? LedgerEntries { get; set; } = new List<LedgerEntry>();
    }
}
