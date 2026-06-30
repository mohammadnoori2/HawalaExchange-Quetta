using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    /// <summary>
    /// گزارش روزانه
    /// </summary>
    [Table("DailyReports")]
    public class DailyReport
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public DateTime Date { get; set; }

        [Required]
        public long BranchId { get; set; }

        [Required]
        [MaxLength(200)]
        public string BranchName { get; set; }

        public int TotalTransactions { get; set; }

        public decimal TotalSendAmount { get; set; }

        public decimal TotalReceiveAmount { get; set; }

        public decimal TotalCommission { get; set; }

        public decimal TotalExpenses { get; set; }

        public decimal NetIncome { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey(nameof(BranchId))]
        public virtual Branch? Branch { get; set; }
    }

    /// <summary>
    /// گزارش تراکنش‌ها
    /// </summary>
    [Table("TransactionReports")]
    public class TransactionReport
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransactionNo { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransactionType { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [MaxLength(200)]
        public string CustomerName { get; set; }

        [MaxLength(200)]
        public string SenderName { get; set; }

        [MaxLength(200)]
        public string ReceiverName { get; set; }

        [MaxLength(10)]
        public string FromCurrency { get; set; }

        public decimal FromAmount { get; set; }

        [MaxLength(10)]
        public string ToCurrency { get; set; }

        public decimal ToAmount { get; set; }

        public decimal Commission { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; }

        public DateTime ReportGeneratedAt { get; set; } = DateTime.UtcNow;

        public long? TransactionId { get; set; }

        [ForeignKey(nameof(TransactionId))]
        public virtual Transaction? Transaction { get; set; }
    }

    /// <summary>
    /// گزارش کارمزد
    /// </summary>
    [Table("CommissionReports")]
    public class CommissionReport
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public DateTime Date { get; set; }

        [Required]
        public long BranchId { get; set; }

        [Required]
        [MaxLength(200)]
        public string BranchName { get; set; }

        public decimal TotalCommission { get; set; }

        public decimal TotalAgentCommission { get; set; }

        public decimal NetCommission { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey(nameof(BranchId))]
        public virtual Branch? Branch { get; set; }
    }

    /// <summary>
    /// تراز آزمایشی
    /// </summary>
    [Table("TrialBalances")]
    public class TrialBalance
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long AccountId { get; set; }

        [Required]
        [MaxLength(50)]
        public string AccountCode { get; set; }

        [Required]
        [MaxLength(200)]
        public string AccountName { get; set; }

        public decimal TotalDebit { get; set; }

        public decimal TotalCredit { get; set; }

        public decimal Balance { get; set; }

        [Required]
        [MaxLength(10)]
        public string BalanceType { get; set; } // Debit or Credit

        public DateTime AsOfDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(AccountId))]
        public virtual Account? Account { get; set; }
    }
}