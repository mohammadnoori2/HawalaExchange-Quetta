namespace HawalaExchange.Application.DTOs;

public sealed class SelectedRecordDto
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Kind { get; set; } = "";
    public long? CorrespondentId { get; set; }
    public string CorrespondentName { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string ReferenceNumber { get; set; } = "";
    public string PaymentLocationName { get; set; } = "";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal? AgentCommission { get; set; }
    public string CommissionCurrencyCode { get; set; } = "";
    public string SenderPhone { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string SenderIdentity { get; set; } = "";
    public string ReceiverIdentity { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long? SourceHawalaId { get; set; }
    public List<SelectedTransactionDetailDto> TransactionDetails { get; set; } = [];
}

public sealed class SelectedTransactionDetailDto
{
    public decimal? FromAmount { get; set; }
    public string FromCurrency { get; set; } = "";
    public decimal? ToAmount { get; set; }
    public string ToCurrency { get; set; } = "";
    public decimal? ExchangeRate { get; set; }
    public decimal? TransferAmount { get; set; }
    public decimal Commission { get; set; }
    public string CommissionCurrency { get; set; } = "";
}

public sealed record SelectedRecordsFileDto(string FileName, string ContentType, byte[] Content);
