using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class AccountDtos
    {
        public long Id { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string ReferenceType { get; set; } = string.Empty;
        public long ? ReferenceId { get; set; }
        public bool IsArchived { get; set; }
        public DateTime DateTime { get; set; }
    }

    public class CreateAccountRequest
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string ReferenceType { get; set; } = string.Empty;
        public long ? ReferenceId { get; set; }
    }

    public class UpdateAccountRequest
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string ReferenceType { get; set; } = string.Empty;
        public long? ReferenceId { get; set; }
        public bool IsArrived { get; set; }
    }
}
