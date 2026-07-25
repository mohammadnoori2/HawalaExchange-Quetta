using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
   
    public class CapitalInvestmentDto
    {
        public long Id { get; set; }

        public long CurrencyId { get; set; }

        public string CurrencyCode { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public long? ProfitCurrencyId { get; set; }
        public string ProfitCurrencyCode { get; set; } = string.Empty;
        public decimal? ProfitCurrencyAmount { get; set; }

        public long ReceivingAccountId { get; set; }

        public string ReceivingAccountName { get; set; } = string.Empty;

        public long CapitalAccountId { get; set; }

        public string CapitalAccountName { get; set; } = string.Empty;

        public DateTime InvestmentDate { get; set; }

        public string? Description { get; set; }
    }

    public class CreateCapitalInvestmentDto
    {
        public long CurrencyId { get; set; }

        public decimal Amount { get; set; }

        public long ProfitCurrencyId { get; set; }
        public decimal ProfitCurrencyAmount { get; set; }

        public long ReceivingAccountId { get; set; }

        public long CapitalAccountId { get; set; }

        public DateTime InvestmentDate { get; set; } = DateTime.UtcNow;

        public string? Description { get; set; }
    }

    public class UpdateCapitalInvestmentDto
    {
        public long CurrencyId { get; set; }

        public decimal Amount { get; set; }

        public long ProfitCurrencyId { get; set; }
        public decimal ProfitCurrencyAmount { get; set; }

        public long ReceivingAccountId { get; set; }

        public long CapitalAccountId { get; set; }

        public DateTime InvestmentDate { get; set; }

        public string? Description { get; set; }
    }
}
