using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
{
    [Table("TransactionDetails")]
    public class TransactionDetail
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long TransactionId { get; set; }

        public long? FromCurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? FromAmount { get; set; }

        public long? ToCurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? ToAmount { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal? ExchangeRate { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? TransferAmount { get; set; }

        public long? CommissionCurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CommissionAmount { get; set; } = 0;

        public long? AgentCommissionCurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal AgentCommissionAmount { get; set; } = 0;

        public long? CorrespondentId { get; set; }

        [MaxLength(200)]
        public string SenderName { get; set; }

        [MaxLength(50)]
        public string SenderPhone { get; set; }

        [MaxLength(100)]
        public string SenderTazkiraNumber { get; set; }

        [MaxLength(200)]
        public string ReceiverName { get; set; }

        [MaxLength(50)]
        public string ReceiverPhone { get; set; }

        [MaxLength(100)]
        public string ReceiverTazkiraNumber { get; set; }

        [MaxLength(100)]
        public string ReferenceNumber { get; set; }

        [MaxLength(1000)]
        public string Notes { get; set; }

        // Navigation Properties
        [ForeignKey("TransactionId")]
        public virtual Transaction Transaction { get; set; }

        [ForeignKey("FromCurrencyId")]
        public virtual Currency FromCurrency { get; set; }

        [ForeignKey("ToCurrencyId")]
        public virtual Currency ToCurrency { get; set; }

        [ForeignKey("CommissionCurrencyId")]
        public virtual Currency CommissionCurrency { get; set; }

        [ForeignKey("AgentCommissionCurrencyId")]
        public virtual Currency AgentCommissionCurrency { get; set; }

        [ForeignKey("CorrespondentId")]
        public virtual Correspondent Correspondent { get; set; }
    }
}