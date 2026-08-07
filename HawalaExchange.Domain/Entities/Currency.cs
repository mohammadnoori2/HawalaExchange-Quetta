using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Currencies")]
    public class Currency : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TenantId { get; set; }

        [Required]
        [MaxLength(10)]
        public string Code { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(20)]
        public string? Symbol { get; set; }

        public int DecimalPlaces { get; set; } = 2;

        /// <summary>
        /// Lower values make this currency the base currency when a canonical pair is formed.
        /// </summary>
        public int QuotationPriority { get; set; } = 1000;

        public bool IsActive { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // Navigation Properties
        public virtual ICollection<TransactionDetail>? FromTransactions { get; set; }
        public virtual ICollection<TransactionDetail>? ToTransactions { get; set; }
        public virtual ICollection<TransactionDetail>? CommissionTransactions { get; set; }
        public virtual ICollection<TransactionDetail>? AgentCommissionTransactions { get; set; }
        public virtual ICollection<LedgerEntry>? LedgerEntries { get; set; }
        public virtual ICollection<Transfer>? Transfers { get; set; }
        public virtual ICollection<ExchangeRate>? FromExchangeRates { get; set; }
        public virtual ICollection<ExchangeRate>? ToExchangeRates { get; set; }
        public virtual ICollection<Expense>? Expenses { get; set; }

        // ✅ Changed: AccountLimits → AccountBadehkarLimits (matches DbContext)
        public virtual ICollection<AccountBadehkarLimit>? AccountBadehkarLimits { get; set; }
    }
}
