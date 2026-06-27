using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Expenses")]
    public class Expense
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long TransactionId { get; set; }

        public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(200)]
        public string Title { get; set; }

        [Required]
        public long CurrencyId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(TransactionId))]
        public virtual Transaction? Transaction { get; set; }

        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }
    }
}