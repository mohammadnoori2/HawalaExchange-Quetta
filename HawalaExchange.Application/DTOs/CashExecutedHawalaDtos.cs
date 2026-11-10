namespace HawalaExchange.Application.DTOs;
public sealed class CashExecutedHawalaFilter
{
    public DateTime From { get; set; } = DateTime.Today;
    public DateTime To { get; set; } = DateTime.Today;
    public long? AccountId { get; set; }
    public long? CorrespondentId { get; set; }
    public long? LocationId { get; set; }
    public long? CurrencyId { get; set; }
    public string Mode { get; set; } = "";
    public string State { get; set; } = "Paid";
    public string Search { get; set; } = "";
}
public sealed class CashExecutedHawalaPart
{
    public string EventKey { get; set; } = "";
    public long HawalaId { get; set; }
    public long Number { get; set; }
    public string Reference { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string Sender { get; set; } = "";
    public long? CorrespondentId { get; set; }
    public string Correspondent { get; set; } = "";
    public long? LocationId { get; set; }
    public string Location { get; set; } = "";
    public long AccountId { get; set; }
    public string Account { get; set; } = "";
    public long CurrencyId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Principal { get; set; }
    public decimal Commission { get; set; }
    public DateTime PaidAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ExecutedBy { get; set; }
    public string ExecutedByName { get; set; } = "";
    public string Mode { get; set; } = "";
    public bool IsReversed { get; set; }
    public long? BillId { get; set; }
    public string HawalaStatus { get; set; } = "";
}
public sealed class CashExecutedHawalaRow
{
    public CashExecutedHawalaPart Payment { get; set; } = new();
    public List<CashExecutedHawalaPart> Amounts { get; set; } = [];
}
public sealed class CashExecutedHawalaTotal
{
    public int Count { get; set; }
    public string Currency { get; set; } = "";
    public decimal Principal { get; set; }
    public decimal Commission { get; set; }
    public decimal Total => Principal + Commission;
}
public sealed class CashExecutedHawalaResult
{
    public int HawalaCount { get; set; }
    public int PaymentCount { get; set; }
    public List<CashExecutedHawalaTotal> Totals { get; set; } = [];
    public List<CashExecutedHawalaTotal> ReversedTotals { get; set; } = [];
    public PaginatedResult<CashExecutedHawalaRow> Page { get; set; } = new();
}
public sealed class CashExecutedHawalaLookups
{
    public DateTime Today { get; set; } = DateTime.Today;
    public List<AgentCommissionReportOption> Accounts { get; set; } = [];
    public List<AgentCommissionReportOption> Correspondents { get; set; } = [];
    public List<AgentCommissionReportOption> Locations { get; set; } = [];
    public List<AgentCommissionReportOption> Currencies { get; set; } = [];
}
