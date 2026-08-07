using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{
    [Table("TransactionDetails")]
    public class TransactionDetail : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TenantId { get; set; }

        [Required]
        public long TransactionId { get; set; }

        public long? FromCurrencyId { get; set; }

        public decimal? FromAmount { get; set; }

        public long? ToCurrencyId { get; set; }

        public decimal? ToAmount { get; set; }

        public decimal? ExchangeRate { get; set; }

        public decimal? TransferAmount { get; set; }

        public long? CommissionCurrencyId { get; set; }

        public decimal CommissionAmount { get; set; } = 0;

        public long? AgentCommissionCurrencyId { get; set; }

        public decimal AgentCommissionAmount { get; set; } = 0;

        public long? CorrespondentId { get; set; }

        [MaxLength(200)]
        public string? SenderName { get; set; }

        [MaxLength(50)]
        public string? SenderPhone { get; set; }

        [MaxLength(100)]
        public string? SenderTazkiraNumber { get; set; }

        [MaxLength(200)]
        public string? ReceiverName { get; set; }

        [MaxLength(50)]
        public string? ReceiverPhone { get; set; }

        [MaxLength(100)]
        public string? ReceiverTazkiraNumber { get; set; }

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(TransactionId))]
        public virtual Transaction? Transaction { get; set; }

        [ForeignKey(nameof(FromCurrencyId))]
        public virtual Currency? FromCurrency { get; set; }

        [ForeignKey(nameof(ToCurrencyId))]
        public virtual Currency? ToCurrency { get; set; }

        [ForeignKey(nameof(CommissionCurrencyId))]
        public virtual Currency? CommissionCurrency { get; set; }

        [ForeignKey(nameof(AgentCommissionCurrencyId))]
        public virtual Currency? AgentCommissionCurrency { get; set; }

        [ForeignKey(nameof(CorrespondentId))]
        public virtual Correspondent? Correspondent { get; set; }
    }
}
