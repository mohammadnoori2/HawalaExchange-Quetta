using System;
using System.Collections.Generic;

namespace HawalaExchange.Application.DTOs
{
    public class HawalaDto
    {
        public long Id { get; set; }
        public long Number { get; set; }
        public string HawalaType { get; set; } = string.Empty;
        public string HawalaTypeName => HawalaType switch
        {
            "HawalaSend" => "حواله ارسال",
            "HawalaReceive" => "حواله دریافت",
            "HawalaOther" => "حواله متفرقه",
            _ => "نامشخص"
        };
        public long? FromAccountId { get; set; }
        public string? FromAccountName { get; set; }
        public long? CorrespondentId { get; set; }
        // در CreateHawalaDto
        public long? PaymentLocationId { get; set; }

        // در HawalaDto
        public string? PaymentLocationName { get; set; }
        public string? PaymentLocationAddress { get; set; }
        public string? CorrespondentName { get; set; }
        public string? SenderName { get; set; }
        public string? SenderFatherName { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderTazkiraNumber { get; set; }
        public string? SenderTazkiraImagePath { get; set; }
        public string? SenderAddress { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverFatherName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public string? ReceiverTazkiraImagePath { get; set; }
        public string? ReceiverAddress { get; set; }
        public long FromCurrencyId { get; set; }
        public string FromCurrencyCode { get; set; } = string.Empty;
        public decimal FromAmount { get; set; }
        public long ToCurrencyId { get; set; }
        public string ToCurrencyCode { get; set; } = string.Empty;
        public decimal? ToAmount { get; set; }
        public decimal? ExchangeRate { get; set; }
        public decimal? CommissionAmount { get; set; }
        public long? CommissionCurrencyId { get; set; }
        public string? CommissionCurrencyCode { get; set; }
        public decimal? AgentCommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public string? AgentCommissionCurrencyCode { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = "Pending";
        public string StatusName => Status switch
        {
            "Pending" => "در انتظار",
            "Paid" => "پرداخت شده",
            "Cancel" => "لغو شده",
            _ => "نامشخص"
        };
        public DateTime CreatedAt { get; set; }
        public long CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime? PaidAt { get; set; }
        public long? PaidBy { get; set; }
        public string? PaymentLocation { get; set; }
        public DateTime? CancelledAt { get; set; }
        public long? CancelledBy { get; set; }
        public string? CancelReason { get; set; }
    }

    public class CreateHawalaDto
    {
        public long Number { get; set; }
        public string HawalaType { get; set; } = string.Empty;
        public long? CorrespondentId { get; set; }
        public long? PaymentLocationId { get; set; }
        public long? FromAccountId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderFatherName { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderTazkiraNumber { get; set; }
        public string? SenderTazkiraImagePath { get; set; }
        public string? SenderAddress { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverFatherName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public string? ReceiverTazkiraImagePath { get; set; }
        public string? ReceiverAddress { get; set; }
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
        public string? Notes { get; set; }
        public string Status { get; set; } = "Pending";
        public string? PaymentLocation { get; set; }
    }

    public class UpdateHawalaDto
    {
        public long? PaymentLocationId { get; set; }
        public long? FromAccountId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderFatherName { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderTazkiraNumber { get; set; }
        public string? SenderAddress { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverFatherName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public string? ReceiverTazkiraImagePath { get; set; }
        public string? ReceiverAddress { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal? ExchangeRate { get; set; }
        public decimal? CommissionAmount { get; set; }
        public long? CommissionCurrencyId { get; set; }
        public decimal? AgentCommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
        public string? PaymentLocation { get; set; }
       
    }

    public class HawalaFilterDto
    {
        public long Number { get; set; }
        public string? SearchTerm { get; set; }
        public string? HawalaType { get; set; }
        public string? Status { get; set; }
        public long? CorrespondentId { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string SortColumn { get; set; } = "CreatedAt";
        public string SortDirection { get; set; } = "desc";
    }

    public class HawalaListResultDto
    {
        public List<HawalaDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
    }

    public class HawalaStatisticsDto
    {
        public int HawalaSendCount { get; set; }
        public int HawalaReceiveCount { get; set; }
        public int HawalaOtherCount { get; set; }
        public int PendingCount { get; set; }
        public int PaidCount { get; set; }
    }


}