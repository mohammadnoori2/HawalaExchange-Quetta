using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class CorrespondentDto
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
        public string? Remarks { get; set; }
        public long? SettlementCurrencyId { get; set; }
        public string? SettlementCurrencyCode { get; set; }
        public string CommissionMethod { get; set; } = "PerTransaction";
        public DateTime CreatedAt { get; set; }
    }

    public class CreateCorrespondentDto
    {
        public string Name { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? Remarks { get; set; }
        public bool HasInitialBalance { get; set; } = false;
        public List<InitialBalanceDto>? InitialBalances { get; set; }
        public long? SettlementCurrencyId { get; set; }
        public string CommissionMethod { get; set; } = "PerTransaction";
    }

    public class UpdateCorrespondentDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
        public string? Remarks { get; set; }
        public long? SettlementCurrencyId { get; set; }
        public string CommissionMethod { get; set; } = "PerTransaction";

    }
}
