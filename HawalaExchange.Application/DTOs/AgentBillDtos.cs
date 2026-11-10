namespace HawalaExchange.Application.DTOs;
public sealed class AgentBillCreateDto
{
    public long CorrespondentId { get; set; }
    public List<long> HawalaIds { get; set; } = [];
    public string RecipientName { get; set; } = "";
    public string? RecipientPhone { get; set; }
    public string? Notes { get; set; }
    public bool ConfirmUnsettled { get; set; }
    public bool HistoricalReceipt { get; set; }
    public bool ConfirmPreviouslySettled { get; set; }
    public string? PreviousSettlementReference { get; set; }
    public string? PreviewToken { get; set; }
}
public sealed class AgentBillPaymentAccountDto
{
    public long CurrencyId { get; set; }
    public long AccountId { get; set; }
}
public sealed class AgentBillPayDto
{
    public long BillId { get; set; }
    public bool ConfirmExecution { get; set; }
    public List<AgentBillPaymentAccountDto> Accounts { get; set; } = [];
}
public sealed class AgentBillExecuteDto
{
    public AgentBillCreateDto Bill { get; set; } = new();
    public List<AgentBillPaymentAccountDto> Accounts { get; set; } = [];
    public Guid RequestId { get; set; }
}
public sealed class AgentBillHistoricalDto
{
    public AgentBillCreateDto Bill { get; set; } = new();
    public Guid RequestId { get; set; }
}
public sealed class AgentBillDto
{
    public bool IsHistoricalReceipt { get; set; }
    public string? PreviousSettlementReference { get; set; }
    public Guid? ExecutionRequestId { get; set; }
    public string? ExecutionFingerprint { get; set; }
    public List<long> PaymentLedgerEntryIds { get; set; } = [];
    public string ConfirmationToken { get; set; } = "";
    public long Id { get; set; }
    public long CorrespondentId { get; set; }
    public string CorrespondentName { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string StatusLabel => Status switch { "Historical" => "رسید تسویه قبلی", "Paid" => "پرداخت‌شده", "Cancelled" => "لغوشده", _ => "در انتظار پرداخت" };
    public string RecipientName { get; set; } = "";
    public string? RecipientPhone { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public List<AgentBillPaymentAccountDto> PaymentAccounts { get; set; } = [];
    public List<HawalaPaymentBatchItemDto> Items { get; set; } = [];
    public List<BulkHawalaPaymentCurrencyDto> Totals { get; set; } = [];
}
