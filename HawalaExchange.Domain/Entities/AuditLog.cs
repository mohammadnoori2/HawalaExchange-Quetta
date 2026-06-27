using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("AuditLogs")]
    public class AuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long? UserId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } // Insert, Update, Delete, Cancel, Reverse

        [Required]
        [MaxLength(100)]
        public string TableName { get; set; }

        [Required]
        public long RecordId { get; set; }

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }
    }
}