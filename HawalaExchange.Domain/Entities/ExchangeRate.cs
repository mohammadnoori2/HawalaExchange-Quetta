using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace HawalaExchange.Domain.Entities
{
    [Table("ExchangeRates")]
    public class ExchangeRate
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long FromCurrencyId { get; set; }

        [Required]
        public long ToCurrencyId { get; set; }

        [Required]
        public decimal BuyRate { get; set; }

        [Required]
        public decimal SellRate { get; set; }

        public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

        [Required]
        public long CreatedBy { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(FromCurrencyId))]
        public virtual Currency? FromCurrency { get; set; }

        [ForeignKey(nameof(ToCurrencyId))]
        public virtual Currency? ToCurrency { get; set; }

        [ForeignKey(nameof(CreatedBy))]
        public virtual User? CreatedByUser { get; set; }
    }
}