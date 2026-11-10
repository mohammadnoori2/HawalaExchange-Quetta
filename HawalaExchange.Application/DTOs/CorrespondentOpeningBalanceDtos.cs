namespace HawalaExchange.Application.DTOs;

public sealed class CorrespondentOpeningBalanceRequestDto
{
    public long CurrencyId { get; set; }
    public decimal Amount { get; set; }
    public string Direction { get; set; } = "Debit";
    public DateTime OpeningDate { get; set; } = DateTime.Today;
    public string? Description { get; set; }
}

public sealed class CorrespondentOpeningBalanceDto
{
    public long Id { get; set; }
    public long TransactionId { get; set; }
    public string TransactionNo { get; set; } = "";
    public long CurrencyId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public string Direction { get; set; } = "Debit";
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string OppositeAccount { get; set; } = "";
    public bool HasLegacyCounterparty { get; set; }
    public string State { get; set; } = "Active";
    public bool IsClosedPeriod { get; set; }
    public long? ReversalTransactionId { get; set; }
    public long? ReplacementTransactionId { get; set; }
}
