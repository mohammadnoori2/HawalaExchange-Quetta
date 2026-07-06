using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class TransactionDetailDto
    {
        public long Id { get; set; }
        public long TransactionId { get; set; }
        public long? FromCurrencyId { get; set; }
        public string? FromCurrencyCode { get; set; }
        public decimal? FromAmount { get; set; }
        public long? ToCurrencyId { get; set; }
        public string? ToCurrencyCode { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal? ExchangeRate { get; set; }
        
        public long? CommissionCurrencyId { get; set; }
        public string? CommissionCurrencyCode { get; set; }
        public decimal CommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public string? AgentCommissionCurrencyCode { get; set; }
        public decimal AgentCommissionAmount { get; set; }
        public long? CorrespondentId { get; set; }
        public string? CorrespondentName { get; set; }
        public string? SenderName { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderFatherName { get; set; }
        public string? SenderTazkiraImagePath { get; set; } = null;
        public string? SenderTazkiraNumber { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverFatherName { get; set; }
        public string? ReceiverTazkiraImagePath { get; set; } = null;
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
    }

    public class CreateTransactionDetailDto
    {
        public long? FromCurrencyId { get; set; }
        public decimal? FromAmount { get; set; }
        public long? ToCurrencyId { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal? ExchangeRate { get; set; }
        
        public long? CommissionCurrencyId { get; set; }
        public decimal CommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public decimal AgentCommissionAmount { get; set; }
        public long? CorrespondentId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderFatherName { get; set; }
        public string? SenderTazkiraImagePath { get; set; }
        public string? SenderPhone { get; set; }
        public string? SenderTazkiraNumber { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverFatherName { get; set; }
        public string? ReceiverTazkiraImagePath { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverTazkiraNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
    }
}
