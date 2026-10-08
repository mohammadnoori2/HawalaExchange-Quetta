namespace HawalaExchange.Application.DTOs;

public sealed class BulkHawalaPaymentRequestDto
{
    public long PaidFromAccountId { get; set; }
    public List<BulkHawalaPaymentItemDto> Items { get; set; } = [];
}

public sealed class BulkHawalaPaymentItemDto
{
    public long Id { get; set; }
    public long Number { get; set; }
    public string ReceiverName { get; set; } = "";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class BulkHawalaPaymentCurrencyDto
{
    public long CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal Principal { get; set; }
    public decimal AgentCommission { get; set; }
    public decimal Total => Principal + AgentCommission;
    // Ledger balance is credit minus debit, as in the rest of the application.
    public decimal AccountBalance { get; set; }
    public decimal AccountBalanceAfter => AccountBalance + Total;
}

public sealed class BulkHawalaPaymentPreviewDto
{
    public long PaidFromAccountId { get; set; }
    public string AccountName { get; set; } = "";
    public bool IsCashOrBank { get; set; }
    public bool IsCorrespondent { get; set; }
    public List<BulkHawalaPaymentItemDto> Items { get; set; } = [];
    public List<BulkHawalaPaymentCurrencyDto> Totals { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public bool CanExecute => PaidFromAccountId > 0 && Items.Count > 0 && Errors.Count == 0 && Items.All(x => x.Error == null);
}

public sealed class HawalaPaymentBatchDto
{
    public long Id { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string AccountName { get; set; } = "";
    public string ExecutedByName { get; set; } = "";
    public int Count { get; set; }
    public List<HawalaPaymentBatchItemDto> Items { get; set; } = [];
    public List<BulkHawalaPaymentCurrencyDto> Totals { get; set; } = [];
}

public sealed class HawalaPaymentBatchItemDto
{
    public long HawalaId { get; set; }
    public long Number { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string CorrespondentName { get; set; } = "";
    public string PaymentLocation { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
    public decimal Amount { get; set; }
    public long CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal AgentCommission { get; set; }
    public long? CommissionCurrencyId { get; set; }
    public string CommissionCurrencyCode { get; set; } = "";
}
