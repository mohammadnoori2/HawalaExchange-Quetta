using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class AccountBadehkarLimitDto
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public long CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal BadehkarLimit { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal CurrentDebt { get; set; }
        public decimal AvailableDebt { get; set; }
        public bool IsOverLimit { get; set; }
        public bool IsActive { get; set; }
        public long? CreatedBy { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class CreateAccountBadehkarLimitDto
    {
        public long AccountId { get; set; }
        public long CurrencyId { get; set; }
        public decimal BadehkarLimit { get; set; }
    }

    public class UpdateAccountBadehkarLimitDto
    {
        public decimal BadehkarLimit { get; set; }
        public bool IsActive { get; set; }
    }

    public class DebtLimitInputDto
    {
        public long CurrencyId { get; set; }
        public decimal BadehkarLimit { get; set; }
        public bool IsEnabled { get; set; }
    }
}
