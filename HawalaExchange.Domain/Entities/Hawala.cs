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
        public long Number { get; set; } // Unique Hawala number
        [Required]
        [MaxLength(50)]
        public string HawalaType { get; set; } // HawalaSend, HawalaReceive, HawalaOther
        public long? CorrespondentId { get; set; }
        [MaxLength(200)]
        // در Hawala.cs
        public long? PaymentLocationId { get; set; }

        [ForeignKey(nameof(PaymentLocationId))]
        public virtual PaymentLocation? PaymentLocation { get; set; }

        [MaxLength(200)]
        public string? SenderName { get; set; }
        [MaxLength(200)]
        public string? SenderFatherName { get; set; }

        [MaxLength(500)]
        public string? TazkiraImagePath { get; set; }

        [MaxLength(50)]
        public string? SenderPhone { get; set; }

        [MaxLength(50)]
        public string? SenderTazkiraNumber { get; set; }

        [MaxLength(200)]
        public string? ReceiverName { get; set; }
        
        [MaxLength(200)]
        public string? ReceiverFatherName { get; set; }
        
        
        [MaxLength(500)]
        public string? ReceiverTazkiraImagePath { get; set; }


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
        public virtual ApplicationUser? CreatedByUser { get; set; }

        [ForeignKey(nameof(PaidBy))]
        public virtual ApplicationUser? PaidByUser { get; set; }

        [ForeignKey(nameof(CancelledBy))]
        public virtual ApplicationUser? CancelledByUser { get; set; }
    }
}