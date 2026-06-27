using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class AccountDto
    {
        public long Id { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public string AccountType { get; set; }
        public string? ReferenceType { get; set; }
        public long? ReferenceId { get; set; }
        public string? ReferenceName { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateAccountDto
    {
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public string AccountType { get; set; }
        public string? ReferenceType { get; set; }
        public long? ReferenceId { get; set; }
    }

    public class UpdateAccountDto
    {
        public string AccountName { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }

        public string? ReferenceType { get; set; }
        public long? ReferenceId { get; set; }
    }

    public class AccountBalanceDto
    {
        public long AccountId { get; set; }
        public string AccountName { get; set; }
        public long CurrencyId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal Balance { get; set; }
        public decimal? BadehkarLimit { get; set; }
        public decimal AvailableBalance { get; set; }
        public bool IsOverLimit { get; set; }
    }
}
