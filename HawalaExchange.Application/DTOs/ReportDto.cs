using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class DailyReportDto
    {
        public DateTime Date { get; set; }
        public long BranchId { get; set; }
        public string BranchName { get; set; }
        public int TotalTransactions { get; set; }
        public decimal TotalSendAmount { get; set; }
        public decimal TotalReceiveAmount { get; set; }
        public decimal TotalCommission { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetIncome { get; set; }
    }

    public class TransactionReportDto
    {
        public string TransactionNo { get; set; }
        public string TransactionType { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CustomerName { get; set; }
        public string SenderName { get; set; }
        public string ReceiverName { get; set; }
        public string FromCurrency { get; set; }
        public decimal FromAmount { get; set; }
        public string ToCurrency { get; set; }
        public decimal ToAmount { get; set; }
        public decimal Commission { get; set; }
        public string Status { get; set; }
    }

    public class CommissionReportDto
    {
        public DateTime Date { get; set; }
        public long BranchId { get; set; }
        public string BranchName { get; set; }
        public decimal TotalCommission { get; set; }
        public decimal TotalAgentCommission { get; set; }
        public decimal NetCommission { get; set; }
    }

    public class TrialBalanceDto
    {
        public long AccountId { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Balance { get; set; }
        public string BalanceType { get; set; } // Debit or Credit
    }
}
