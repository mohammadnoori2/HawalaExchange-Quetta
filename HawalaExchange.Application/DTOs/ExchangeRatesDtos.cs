using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class DashboardExchangeRatesDto
    {
        public DateTime LocalNow { get; set; }
        public DateTime Today => LocalNow.Date;
        public List<ExchangeRateDto> Rates { get; set; } = new();
    }
    public class ExchangeRateDto
    {
        public long Id { get; set; }
        public long FromCurrencyId { get; set; }
        public string FromCurrencyCode { get; set; }
        public long ToCurrencyId { get; set; }
        public string ToCurrencyCode { get; set; }
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
        public DateTime EffectiveDate { get; set; }
        public long CreatedBy { get; set; }
        public string CreatedByName { get; set; }
    }

    public class CreateExchangeRateDto
    {
        public long FromCurrencyId { get; set; }
        public long ToCurrencyId { get; set; }
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
        public DateTime EffectiveDate { get; set; }
    }

    public class UpdateExchangeRateDto
    {
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
        public DateTime EffectiveDate { get; set; }
    }
}
