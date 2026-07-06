using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Transactions")]
    public class Transaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransactionNo { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransactionType { get; set; } // Exchange, HawalaSend, HawalaReceive, Transfer, Expense, Adjustment

        //[Required]
        //public long BranchId { get; set; }

        [MaxLength(50)]
        public string PaymentLocation { get; set; } // Branch, Correspondent, Other

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } // Pending, Paid, Cancel

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        [Required]
        public long CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public long? CancelledBy { get; set; }

        public DateTime? CancelledAt { get; set; }

        [MaxLength(1000)]
        public string? CancelReason { get; set; }

        public long? ReversedTransactionId { get; set; }

        // Navigation Properties
        //[ForeignKey(nameof(BranchId))]
        //public virtual Branch? Branch { get; set; }

        [ForeignKey(nameof(Id))]
        public long CustomerId { get; set; }

        [ForeignKey(nameof(CreatedBy))]
        public virtual User? CreatedByUser { get; set; }

        [ForeignKey(nameof(CancelledBy))]
        public virtual User? CancelledByUser { get; set; }

        [ForeignKey(nameof(ReversedTransactionId))]
        public virtual Transaction? ReversedTransaction { get; set; }

        public virtual ICollection<TransactionDetail>? TransactionDetails { get; set; }
        public virtual ICollection<LedgerEntry>? LedgerEntries { get; set; }
        public virtual ICollection<Transfer>? Transfers { get; set; }
        public virtual ICollection<Expense>? Expenses { get; set; }
        public virtual ICollection<Document>? Documents { get; set; }
        public virtual ICollection<AuditLog>? AuditLogs { get; set; }
        public virtual ICollection<Customer>? Customers { get; set; }

        public virtual Hawala? Hawala { get; set; }

    }
}