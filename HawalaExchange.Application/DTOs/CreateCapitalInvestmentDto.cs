using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class CreateCapitalInvestmentDto
    {
        public long CurrencyId { get; set; }

        public decimal Amount { get; set; }

        public long ReceivingAccountId { get; set; }

        public long CapitalAccountId { get; set; }

        public DateTime InvestmentDate { get; set; } = DateTime.UtcNow;

        public string? Description { get; set; }
    }
}
