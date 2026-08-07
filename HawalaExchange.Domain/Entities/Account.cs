using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Accounts")]
    public class Account : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TenantId { get; set; }

        [Required]
        [MaxLength(50)]
        public string AccountCode { get; set; }

        [Required]
        [MaxLength(200)]
        public string AccountName { get; set; }

        [Required]
        [MaxLength(50)]
        public string AccountType { get; set; } // Cash, Bank, Customer, Correspondent, Income, Expense, Equity

        public long? CustomerId { get; set; }
        public long? CorrespondentId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        [ForeignKey(nameof(CorrespondentId))]
        public virtual Correspondent? Correspondent { get; set; }

        // Compatibility façade for existing DTO/UI code. These fields are not persisted.
        [NotMapped]
        public string? ReferenceType
        {
            get => CustomerId.HasValue ? "Customer" : CorrespondentId.HasValue ? "Correspondent" : null;
            set
            {
                if (value == "Customer") CorrespondentId = null;
                else if (value == "Correspondent") CustomerId = null;
                else { CustomerId = null; CorrespondentId = null; }
            }
        }

        [NotMapped]
        public long? ReferenceId
        {
            get => CustomerId ?? CorrespondentId;
            set
            {
                if (ReferenceType == "Customer") CustomerId = value;
                else if (ReferenceType == "Correspondent") CorrespondentId = value;
                else if (value is null) { CustomerId = null; CorrespondentId = null; }
            }
        }


        public bool IsArchived { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

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
