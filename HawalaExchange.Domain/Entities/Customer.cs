using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Customers")]
    public class Customer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string CustomerCode { get; set; }

        [Required]
        [MaxLength(200)]
        public string FullName { get; set; }

        [MaxLength(200)]
        public string? FatherName { get; set; }

        [MaxLength(50)]
        public string? PhoneNumber { get; set; }

        [MaxLength(100)]
        public string? TazkiraNumber { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [MaxLength(500)]
        public string? TazkiraImagePath { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        public bool IsArchived { get; set; } = false;

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public virtual ICollection<Transaction>? Transactions { get; set; }
    }
}
