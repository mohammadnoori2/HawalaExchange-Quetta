using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Users")]
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long BranchId { get; set; }

        [Required]
        [MaxLength(200)]
        public string FullName { get; set; }

        [Required]
        [MaxLength(100)]
        public string UserName { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        [Required]
        [MaxLength(50)]
        public string Role { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey(nameof(BranchId))]
        public virtual Branch? Branch { get; set; }

        public virtual ICollection<Transaction>? CreatedTransactions { get; set; }

        // ✅ Added: Transactions that this user cancelled
        public virtual ICollection<Transaction>? CancelledTransactions { get; set; }

        public virtual ICollection<ExchangeRate>? ExchangeRates { get; set; }
        public virtual ICollection<AccountBadehkarLimit>? AccountLimits { get; set; }
        public virtual ICollection<AuditLog>? AuditLogs { get; set; }
    }
}