using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    public class ApplicationUser : IdentityUser<long>
    {
        public string FullName { get; set; } = string.Empty;
        public long BranchId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiryTime { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(BranchId))]
        public virtual Branch? Branch { get; set; }

        public virtual ICollection<Transaction>? CreatedTransactions { get; set; }
        public virtual ICollection<Transaction>? CancelledTransactions { get; set; }
        public virtual ICollection<ExchangeRate>? ExchangeRates { get; set; }
        public virtual ICollection<AccountBadehkarLimit>? AccountLimits { get; set; }
        public virtual ICollection<AuditLog>? AuditLogs { get; set; }
        public virtual ICollection<Hawala>? CreatedHawalas { get; set; }
        public virtual ICollection<Hawala>? PaidHawalas { get; set; }
        public virtual ICollection<Hawala>? CancelledHawalas { get; set; }
    }
}