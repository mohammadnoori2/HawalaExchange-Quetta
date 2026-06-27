using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class LedgerEntryDto
    {
        public long Id { get; set; }
        public long TransactionId { get; set; }
        public long AccountId { get; set; }
        public string AccountName { get; set; }
        public string AccountCode { get; set; }
        public long CurrencyId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal TalabKar { get; set; } // Debit
        public decimal BadehKar { get; set; } // Credit
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateLedgerEntryDto
    {
        public long AccountId { get; set; }
        public long CurrencyId { get; set; }
        public decimal TalabKar { get; set; }
        public decimal BadehKar { get; set; }
        public string? Description { get; set; }
    }
}
