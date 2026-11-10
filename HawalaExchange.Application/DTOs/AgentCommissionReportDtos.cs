namespace HawalaExchange.Application.DTOs;

public sealed class AgentCommissionReportFilter
{
    public long CorrespondentId { get; set; }
    public DateTime From { get; set; } = DateTime.Today;
    public DateTime To { get; set; } = DateTime.Today;
    public long? PaymentLocationId { get; set; }
    public long? CurrencyId { get; set; }
    public long? CommissionCurrencyId { get; set; }
    public string Status { get; set; } = "";
    public string Settlement { get; set; } = "";
    public string Search { get; set; } = "";
    public bool IncludeZeroCommission { get; set; }
    public bool IncludeCancelled { get; set; }
    public string DateBasis { get; set; } = "Commission";
}

public sealed class AgentCommissionReportRow
{
    public long HawalaId { get; set; }
    public long Number { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime CommissionAddedAt { get; set; }
    public string Sender { get; set; } = "";
    public string Receiver { get; set; } = "";
    public long? LocationId { get; set; }
    public string Location { get; set; } = "";
    public long CurrencyId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public long CommissionCurrencyId { get; set; }
    public string CommissionCurrency { get; set; } = "";
    public decimal Commission { get; set; }
    public string Status { get; set; } = "";
    public long? BillId { get; set; }
    public string Settlement { get; set; } = "";
}

public sealed class AgentCommissionCurrencySummary
{
    public string Currency { get; set; } = "";
    public decimal Principal { get; set; }
    public decimal CancelledPrincipal { get; set; }
    public decimal Commission { get; set; }
    public decimal CancelledCommission { get; set; }
    public decimal PaidCommission { get; set; }
    public decimal HistoricalCommission { get; set; }
    public decimal BilledCommission { get; set; }
    public decimal UnrecordedCommission { get; set; }
}
public sealed class AgentCommissionLocationSummary
{
    public long? LocationId { get; set; }
    public string Location { get; set; } = "";
    public int Count { get; set; }
    public List<AgentCommissionCurrencySummary> Currencies { get; set; } = [];
}
public sealed class AgentCommissionReportResult
{
    public string CorrespondentName { get; set; } = "";
    public int Count { get; set; }
    public int CancelledCount { get; set; }
    public List<AgentCommissionCurrencySummary> Currencies { get; set; } = [];
    public List<AgentCommissionLocationSummary> Locations { get; set; } = [];
}
public sealed record AgentCommissionReportOption(long Id, string Name);
public sealed class AgentCommissionReportLookups
{
    public string CorrespondentName { get; set; } = "";
    public DateTime Today { get; set; } = DateTime.Today;
    public List<AgentCommissionReportOption> Locations { get; set; } = [];
    public List<AgentCommissionReportOption> Currencies { get; set; } = [];
}
public static class AgentCommissionReportLabels
{
    public static string Settlement(string value) => value switch {
        "Paid" => "پرداخت‌شده از طریق بیل", "Historical" => "دارای رسید تسویه قبلی", "Billed" => "بیل در انتظار پرداخت",
        _ => "بدون سابقه تسویه ثبت‌شده" };
}
