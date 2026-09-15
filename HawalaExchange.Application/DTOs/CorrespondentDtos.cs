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

    public sealed class CorrespondentDetailsPageDto
    {
        public CorrespondentDto Correspondent { get; set; } = new();
        public long? AccountId { get; set; }
    }

    public sealed class CorrespondentStatusPageDto
    {
        public CorrespondentDto Correspondent { get; set; } = new();
        public long? AccountId { get; set; }
        public List<BalanceDto> Balances { get; set; } = [];
        public AccountOperationsPageDto Operations { get; set; } = new();
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
