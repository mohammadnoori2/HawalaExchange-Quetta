using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
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

        [Column(TypeName = "decimal(18,8)")]
        public decimal BuyRate { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal SellRate { get; set; }

        public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

        [Required]
        public long CreatedBy { get; set; }

        // Navigation Properties
        [ForeignKey("FromCurrencyId")]
        public virtual Currency FromCurrency { get; set; }

        [ForeignKey("ToCurrencyId")]
        public virtual Currency ToCurrency { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; }
    }
}