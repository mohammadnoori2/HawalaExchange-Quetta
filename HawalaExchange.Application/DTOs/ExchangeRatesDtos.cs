using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class ExchangeRatesDtos
    {
        public long Id { get; set; }
        public long FromCurrencyId { get; set; }
        public long ToCurrencyId { get; set; }
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
        public DateTime EffectiveDate { get; set; }
        public long CreatedBy { get; set; }

    }

    public class CreateExchangeRatesRequest
    {
        public long FromCurrencyId { get; set; }
        public long ToCurrencyId { get; set; }
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
        public DateTime EffectiveDate { get; set; }
        public long CreatedBy { get; set; }
    }

    public class UpdateExchangeRatesRequest
    {
        public long FromCurrencyId { get; set; }
        public long ToCurrencyId { get; set; }
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
        public DateTime EffectiveDate { get; set; }
        public long CreatedBy { get; set; }
    }
}
