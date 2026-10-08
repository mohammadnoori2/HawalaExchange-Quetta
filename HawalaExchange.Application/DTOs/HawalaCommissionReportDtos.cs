namespace HawalaExchange.Application.DTOs;

public sealed class HawalaCommissionReportFilter
{
    public DateTime From { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime To { get; set; } = DateTime.Today;
    public List<long> CorrespondentIds { get; set; } = [];
    public string HawalaType { get; set; } = "";
    public string CommissionType { get; set; } = "";
    public long? PaymentLocationId { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public string CommissionStatus { get; set; } = "";
    public string Registration { get; set; } = "";
    public string Search { get; set; } = "";
    public string GroupBy { get; set; } = "Day";
    public bool Estimate { get; set; }
    public decimal IncomingRate { get; set; } = 400;
    public decimal ForwardingRate { get; set; } = 400;
    public decimal DestinationAfnRate { get; set; } = 200;
    public decimal DestinationUsdRate { get; set; } = 100;
    public List<HawalaReportLocationRate> LocationRates { get; set; } = [];
}

public sealed class HawalaReportLocationRate
{
    public long LocationId { get; set; }
    public decimal? ForwardingRate { get; set; }
    public decimal? DestinationAfnRate { get; set; }
    public decimal? DestinationUsdRate { get; set; }
}

public sealed class HawalaCommissionReportRow
{
    public long HawalaId { get; set; }
    public long Number { get; set; }
    public string Reference { get; set; } = "";
    public DateTime Date { get; set; }
    public string HawalaType { get; set; } = "";
    public long? CorrespondentId { get; set; }
    public string Correspondent { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public long? LocationId { get; set; }
    public string Location { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public bool BulkImport { get; set; }
    public string CommissionType { get; set; } = "";
    public string CommissionStatus { get; set; } = "";
    public long? BatchId { get; set; }
    public decimal? Commission { get; set; }
    public string CommissionCurrency { get; set; } = "";
    public decimal? BasisUsd { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? PerLakhRate { get; set; }
    public DateTime? RateDate { get; set; }
    public bool IsEstimate { get; set; }
    public string Note { get; set; } = "";
}

public sealed record HawalaReportAmount(string HawalaType, string Currency, int Count, decimal Amount);
public sealed record HawalaReportSummary(string Currency, bool Estimate, decimal Income, decimal Expense)
{
    public decimal Total => Income + Expense;
    public decimal Net => Income - Expense;
}
public sealed record HawalaReportGroup(string Label, string Type, string Currency, string State, bool Estimate, int Count, decimal? Commission);
public sealed class HawalaCommissionReportResult
{
    public List<HawalaCommissionReportRow> Rows { get; set; } = [];
    public List<HawalaReportAmount> Amounts { get; set; } = [];
    public List<HawalaReportSummary> Summaries { get; set; } = [];
    public List<HawalaReportGroup> Groups { get; set; } = [];
}

public static class HawalaReportLabels
{
    public static string Type(string type) => type switch { "Incoming" => "کمیشن دریافتی", "Destination" => "کمیشن پرداختی به مقصد", "Forwarding" => "کمیشن ارسالی از فرستنده", "Direct" => "کارمزد تک‌حواله", "None" => "بدون کمیشن دوره‌ای", _ => type };
    public static string State(string state) => state switch { "Calculated" => "محاسبه‌شده / منتظر بستن دوره", "Recognized" => "منظورشده در بستن دوره", "Reversed" => "برگشت‌شده", "Pending" => "محاسبه‌نشده", "Direct" => "ثبت تک‌حواله", "None" => "بدون محاسبه دوره‌ای", _ => state };
}
