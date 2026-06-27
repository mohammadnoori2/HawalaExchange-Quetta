using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class AccountBadehkarLimitDto
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string AccountName { get; set; }
        public long CurrencyId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal BadehkarLimit { get; set; }
        public bool IsActive { get; set; }
        public long? CreatedBy { get; set; }
        public string CreatedByName { get; set; }
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
}
