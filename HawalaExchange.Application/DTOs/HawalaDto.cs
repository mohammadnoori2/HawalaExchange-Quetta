using System;
using System.Collections.Generic;

namespace HawalaExchange.Application.DTOs
{
    public class HawalaDto
    {
        public long Id { get; set; }
        public long TransactionId { get; set; }
        public string TransactionNo { get; set; }
        public string HawalaType { get; set; }
        public string HawalaTypeName => GetHawalaTypeName(HawalaType);
        public long BranchId { get; set; }
        public string BranchName { get; set; }
        public long? CustomerId { get; set; }
        public string? CustomerFullName { get; set; }
        public long? CorrespondentId { get; set; }
        public string? CorrespondentName { get; set; }
        public string? SenderName { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderTazkiraNumber { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public long FromCurrencyId { get; set; }
        public string FromCurrencyCode { get; set; }
        public decimal FromAmount { get; set; }
        public long ToCurrencyId { get; set; }
        public string ToCurrencyCode { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal? ExchangeRate { get; set; }
        public decimal? CommissionAmount { get; set; }
        public long? CommissionCurrencyId { get; set; }
        public string? CommissionCurrencyCode { get; set; }
        public decimal? AgentCommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public string? AgentCommissionCurrencyCode { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? SenderAddress { get; set; }
        public string? ReceiverAddress { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; }
        public string StatusName => GetStatusName(Status);
        public DateTime CreatedAt { get; set; }
        public long CreatedBy { get; set; }
        public string CreatedByName { get; set; }
        public DateTime? PaidAt { get; set; }
        public long? PaidBy { get; set; }
        public string? PaidByName { get; set; }
        public DateTime? CancelledAt { get; set; }
        public long? CancelledBy { get; set; }
        public string? CancelledByName { get; set; }
        public string? CancelReason { get; set; }
        public long? ReversedTransactionId { get; set; }

        private string GetHawalaTypeName(string type)
        {
            return type switch
            {
                "HawalaSend" => "حواله ارسال",
                "HawalaReceive" => "حواله دریافت",
                "HawalaOther" => "حواله متفرقه",
                _ => type ?? "نامشخص"
            };
        }

        private string GetStatusName(string status)
        {
            return status switch
            {
                "Pending" => "در انتظار",
                "Paid" => "پرداخت شده",
                "Cancel" => "لغو شده",
                _ => status ?? "نامشخص"
            };
        }
    }

    public class CreateHawalaDto
    {
        public long TransactionId { get; set; }
        public string HawalaType { get; set; }
        public long BranchId { get; set; }
        public long? CustomerId { get; set; }
        public long? CorrespondentId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderTazkiraNumber { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public long FromCurrencyId { get; set; }
        public decimal FromAmount { get; set; }
        public long ToCurrencyId { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal? ExchangeRate { get; set; }
        public decimal? CommissionAmount { get; set; }
        public long? CommissionCurrencyId { get; set; }
        public decimal? AgentCommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? SenderAddress { get; set; }
        public string? ReceiverAddress { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = "Pending";
    }

    public class UpdateHawalaDto
    {
        public string? SenderName { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderTazkiraNumber { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal? ExchangeRate { get; set; }
        public decimal? CommissionAmount { get; set; }
        public long? CommissionCurrencyId { get; set; }
        public decimal? AgentCommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? SenderAddress { get; set; }
        public string? ReceiverAddress { get; set; }
        public string? Notes { get; set; }
    }

    public class HawalaFilterDto
    {
        public string? SearchTerm { get; set; }
        public string? HawalaType { get; set; }
        public string? Status { get; set; }
        public long? BranchId { get; set; }
        public long? CustomerId { get; set; }
        public long? CorrespondentId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SortColumn { get; set; } = "CreatedAt";
        public string SortDirection { get; set; } = "desc";
    }

    public class HawalaStatisticsDto
    {
        public int TotalHawalas { get; set; }
        public int HawalaSendCount { get; set; }
        public int HawalaReceiveCount { get; set; }
        public int HawalaOtherCount { get; set; }
        public int PendingCount { get; set; }
        public int PaidCount { get; set; }
        public int CancelCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalCommission { get; set; }
        public Dictionary<string, int> DailyStats { get; set; } = new();
    }

    
}