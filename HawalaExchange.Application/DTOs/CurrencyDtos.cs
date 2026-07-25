using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class CurrencyDto
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string? Symbol { get; set; }
        public int DecimalPlaces { get; set; }
        public int QuotationPriority { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateCurrencyDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string? Symbol { get; set; }
        public int DecimalPlaces { get; set; }
        public int QuotationPriority { get; set; } = 1000;
    }

    public class UpdateCurrencyDto
    {
        public string Name { get; set; }
        public string? Symbol { get; set; }
        public int DecimalPlaces { get; set; }
        public int QuotationPriority { get; set; }
        public bool IsActive { get; set; }
    }
}
