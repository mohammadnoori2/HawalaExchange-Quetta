using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("LedgerEntries")]
    public class LedgerEntry : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long TenantId { get; set; }
        public long? TransferId { get; set; }
        //[Required]
        public long? HawalaId { get; set; }

        public Hawala? Hawala { get; set; }
        public long? TransactionId { get; set; }
        public long? CapitalInvestmentId { get; set; }
        public long? ExpenseId { get; set; }
        public long? AccountMoneyOperationId { get; set; }
        public long? MoneyExchangeOperationId { get; set; }


        [Required]
        public long AccountId { get; set; }

        [Required]
        public long CurrencyId { get; set; }

        public decimal TalabKar { get; set; } = 0; // Credit

        public decimal BadehKar { get; set; } = 0; // Debit

        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // Navigation Properties
        [ForeignKey(nameof(TransactionId))]
        public virtual Transaction? Transaction { get; set; }

        [ForeignKey(nameof(AccountId))]
        public virtual Account? Account { get; set; }

        [ForeignKey(nameof(TransferId))]
        public virtual Transfer? Transfer { get; set; }

        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }
        public CapitalInvestment? CapitalInvestment { get; set; }
        public Expense? Expense { get; set; }
        public AccountMoneyOperation? AccountMoneyOperation { get; set; }
      
        public MoneyExchangeOperation? MoneyExchangeOperation { get; set; }

    }
}
