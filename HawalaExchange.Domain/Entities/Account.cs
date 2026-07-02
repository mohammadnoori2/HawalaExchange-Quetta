using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Accounts")]
    public class Account
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string AccountCode { get; set; }

        [Required]
        [MaxLength(200)]
        public string AccountName { get; set; }

        [Required]
        [MaxLength(50)]
        public string AccountType { get; set; } // Cash, Bank, Customer, Correspondent, Income, Expense, Equity

        [MaxLength(50)]
        public string? ReferenceType { get; set; } // e.g., Customer, Correspondent

        public long? ReferenceId { get; set; } // Foreign key to Customer.Id or Correspondent.Id


        public bool IsArchived { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties

        // Ledger entries for this account
        public virtual ICollection<LedgerEntry>? LedgerEntries { get; set; }

        // ✅ Changed: OutgoingTransfers → FromTransfers (matches DbContext)
        public virtual ICollection<Transfer>? FromTransfers { get; set; }

        // ✅ Changed: IncomingTransfers → ToTransfers (matches DbContext)
        public virtual ICollection<Transfer>? ToTransfers { get; set; }

        // ✅ Changed: AccountLimits → AccountBadehkarLimits (matches DbContext)
        public virtual ICollection<AccountBadehkarLimit>? AccountBadehkarLimits { get; set; }
    }
}