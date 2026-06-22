using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
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
        public string TransactionType { get; set; }

        [Required]
        public long BranchId { get; set; }

        public long? CustomerId { get; set; }

        [MaxLength(200)]
        public string CustomerFullName { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; }

        [MaxLength(1000)]
        public string Remarks { get; set; }

        [Required]
        public long CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public long? CancelledBy { get; set; }

        public DateTime? CancelledAt { get; set; }

        [MaxLength(1000)]
        public string CancelReason { get; set; }

        public long? ReversedTransactionId { get; set; }

        // Navigation Properties
        [ForeignKey("BranchId")]
        public virtual Branch Branch { get; set; }

        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; }

        [ForeignKey("CancelledBy")]
        public virtual User CancelledByUser { get; set; }

        public virtual ICollection<TransactionDetail> TransactionDetails { get; set; }
        public virtual ICollection<LedgerEntry> LedgerEntries { get; set; }
        public virtual ICollection<Transfer> Transfers { get; set; }
        public virtual ICollection<Expense> Expenses { get; set; }
    }
}