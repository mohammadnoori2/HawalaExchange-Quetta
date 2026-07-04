using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("Hawalas")]
    public class Hawala
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long TransactionId { get; set; }

        [Required]
        [MaxLength(50)]
        public string HawalaType { get; set; } // HawalaSend, HawalaReceive, HawalaOther

        [Required]
        public long BranchId { get; set; }

        public long? CustomerId { get; set; }

        public long? CorrespondentId { get; set; }

        [MaxLength(200)]
        public string? SenderName { get; set; }

        [MaxLength(50)]
        public string? SenderPhone { get; set; }

        [MaxLength(50)]
        public string? SenderTazkiraNumber { get; set; }

        [MaxLength(200)]
        public string? ReceiverName { get; set; }

        [MaxLength(50)]
        public string? ReceiverPhone { get; set; }

        [MaxLength(50)]
        public string? ReceiverTazkiraNumber { get; set; }

        [Required]
        public long FromCurrencyId { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal FromAmount { get; set; }

        [Required]
        public long ToCurrencyId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ToAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? ExchangeRate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionAmount { get; set; }

        public long? CommissionCurrencyId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? AgentCommissionAmount { get; set; }

        public long? AgentCommissionCurrencyId { get; set; }

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        [MaxLength(500)]
        public string? SenderAddress { get; set; }

        [MaxLength(500)]
        public string? ReceiverAddress { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public long CreatedBy { get; set; }

        public DateTime? PaidAt { get; set; }

        public long? PaidBy { get; set; }

        public DateTime? CancelledAt { get; set; }

        public long? CancelledBy { get; set; }

        [MaxLength(500)]
        public string? CancelReason { get; set; }

        public long? ReversedTransactionId { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(TransactionId))]
        public virtual Transaction? Transaction { get; set; }

        [ForeignKey(nameof(BranchId))]
        public virtual Branch? Branch { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        [ForeignKey(nameof(CorrespondentId))]
        public virtual Correspondent? Correspondent { get; set; }

        [ForeignKey(nameof(FromCurrencyId))]
        public virtual Currency? FromCurrency { get; set; }

        [ForeignKey(nameof(ToCurrencyId))]
        public virtual Currency? ToCurrency { get; set; }

        [ForeignKey(nameof(CommissionCurrencyId))]
        public virtual Currency? CommissionCurrency { get; set; }

        [ForeignKey(nameof(AgentCommissionCurrencyId))]
        public virtual Currency? AgentCommissionCurrency { get; set; }

        [ForeignKey(nameof(CreatedBy))]
        public virtual User? CreatedByUser { get; set; }

        [ForeignKey(nameof(PaidBy))]
        public virtual User? PaidByUser { get; set; }

        [ForeignKey(nameof(CancelledBy))]
        public virtual User? CancelledByUser { get; set; }
    }
}