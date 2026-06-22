using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
{
    [Table("Currencies")]
    public class Currency
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Code { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(20)]
        public string Symbol { get; set; }

        public int DecimalPlaces { get; set; } = 2;

        public bool IsActive { get; set; } = true;

        // Navigation Properties
        public virtual ICollection<LedgerEntry> LedgerEntries { get; set; }
        public virtual ICollection<ExchangeRate> FromCurrencyExchangeRates { get; set; }
        public virtual ICollection<ExchangeRate> ToCurrencyExchangeRates { get; set; }
        public virtual ICollection<TransactionDetail> FromCurrencyTransactionDetails { get; set; }
        public virtual ICollection<TransactionDetail> ToCurrencyTransactionDetails { get; set; }
        public virtual ICollection<TransactionDetail> CommissionCurrencyTransactionDetails { get; set; }
        public virtual ICollection<TransactionDetail> AgentCommissionCurrencyTransactionDetails { get; set; }
        public virtual ICollection<Transfer> Transfers { get; set; }
        public virtual ICollection<Expense> Expenses { get; set; }
        public virtual ICollection<AccountBadehkarLimit> AccountBadehkarLimits { get; set; }
    }
}