using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Transactions")]
    public class Transaction : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TenantId { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransactionNo { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransactionType { get; set; } // Exchange, HawalaSend, HawalaReceive, Transfer, Expense, Adjustment

        [Required]
        public long BranchId { get; set; }

        public long? CustomerId { get; set; }

        [MaxLength(200)]
        public string? CustomerFullName { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } // Pending, Paid, Cancel

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        [Required]
        public long CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public long? CancelledBy { get; set; }

        public DateTime? CancelledAt { get; set; }

        [MaxLength(1000)]
        public string? CancelReason { get; set; }

        public long? ReversedTransactionId { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(BranchId))]
        public virtual Branch? Branch { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        [ForeignKey(nameof(CreatedBy))]
        public virtual ApplicationUser? CreatedByUser { get; set; }

        [ForeignKey(nameof(CancelledBy))]
        public virtual ApplicationUser? CancelledByUser { get; set; }

        [ForeignKey(nameof(ReversedTransactionId))]
        public virtual Transaction? ReversedTransaction { get; set; }

        public virtual ICollection<TransactionDetail>? TransactionDetails { get; set; }
        public virtual ICollection<LedgerEntry>? LedgerEntries { get; set; }
        public virtual ICollection<Transfer>? Transfers { get; set; }
        public virtual ICollection<Expense>? Expenses { get; set; }
        public virtual ICollection<Document>? Documents { get; set; }
        public virtual ICollection<AuditLog>? AuditLogs { get; set; }

        public virtual Hawala? Hawala { get; set; }

    }
}
