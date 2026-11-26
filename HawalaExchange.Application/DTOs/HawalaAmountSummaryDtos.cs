namespace HawalaExchange.Application.DTOs;

public sealed class HawalaBrowserAmountRow
{
    public long Id { get; set; }
    public long? CorrespondentId { get; set; }
    public string Correspondent { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Original { get; set; } = "0";
    public string? SettlementCurrency { get; set; }
    public string? Converted { get; set; }
    public bool Cancelled { get; set; }
    public bool HasConversion { get; set; }
    public bool HasIndependentExchange { get; set; }
    public string? IndependentCurrency { get; set; }
    public string? IndependentConverted { get; set; }
}

public sealed class HawalaAmountSummaryRequest
{
    public HawalaFilterDto Filter { get; set; } = new();
    public long[] SelectedIds { get; set; } = [];
    public long? ScopeCorrespondentId { get; set; }
    public string? ScopeHawalaType { get; set; }
}
public sealed class HawalaAmountSummaryDto
{
    public int Count { get; set; }
    public bool SelectedOnly { get; set; }
    public int WithoutAgreementCount { get; set; }
    public List<HawalaOriginalAmountTotal> Originals { get; set; } = [];
    public List<HawalaSettlementAmountTotal> Settlements { get; set; } = [];
}
public sealed class HawalaOriginalAmountTotal
{
    public string Currency { get; set; } = "";
    public bool Cancelled { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
}
public sealed class HawalaSettlementAmountTotal
{
    public int Count { get; set; }
    public long? CorrespondentId { get; set; }
    public string Correspondent { get; set; } = "";
    public string Currency { get; set; } = "";
    public bool Cancelled { get; set; }
    public decimal NativeAmount { get; set; }
    public decimal ConvertedAmount { get; set; }
    public int UnconvertedCount { get; set; }
    public int IncompleteCount { get; set; }
    public decimal Total => NativeAmount + ConvertedAmount;
}
