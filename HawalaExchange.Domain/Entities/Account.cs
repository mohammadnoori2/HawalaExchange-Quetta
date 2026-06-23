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
        public string AccountCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string AccountName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string AccountType { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? ReferenceType { get; set; }

        public long? ReferenceId { get; set; }

        public bool IsArchived { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();

        public virtual ICollection<Transfer> FromTransfers { get; set; } = new List<Transfer>();

        public virtual ICollection<Transfer> ToTransfers { get; set; } = new List<Transfer>();

        public virtual ICollection<AccountBadehkarLimit> AccountBadehkarLimits { get; set; } = new List<AccountBadehkarLimit>();
    }
}