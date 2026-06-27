using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class BalanceDto
    {
        public long CurrencyId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal Balance { get; set; }
    }

    public class CustomerBalanceDto
    {
        public long CustomerId { get; set; }
        public string CustomerName { get; set; }
        public List<BalanceDto> Balances { get; set; }
    }

    public class CorrespondentBalanceDto
    {
        public long CorrespondentId { get; set; }
        public string CorrespondentName { get; set; }
        public List<BalanceDto> Balances { get; set; }
    }

    public class CashBalanceDto
    {
        public long AccountId { get; set; }
        public string AccountName { get; set; }
        public List<BalanceDto> Balances { get; set; }
    }

    public class BranchBalanceDto
    {
        public long BranchId { get; set; }
        public string BranchName { get; set; }
        public List<BalanceDto> CashBalances { get; set; }
        public List<BalanceDto> BankBalances { get; set; }
        public List<BalanceDto> CorrespondentBalances { get; set; }
    }
}
