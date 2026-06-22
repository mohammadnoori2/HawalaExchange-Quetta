using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
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
        public string AccountType { get; set; }

        [MaxLength(50)]
        public string ReferenceType { get; set; }

        public long? ReferenceId { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsArchived { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public virtual ICollection<LedgerEntry> LedgerEntries { get; set; }
        public virtual ICollection<Transfer> FromTransfers { get; set; }
        public virtual ICollection<Transfer> ToTransfers { get; set; }
        public virtual ICollection<AccountBadehkarLimit> AccountBadehkarLimits { get; set; }
    }
}