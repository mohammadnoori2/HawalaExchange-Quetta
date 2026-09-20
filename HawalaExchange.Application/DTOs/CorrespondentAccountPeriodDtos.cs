namespace HawalaExchange.Application.DTOs;

public sealed class CloseCorrespondentPeriodDto { public string? Note { get; set; } }
public sealed class CorrespondentPeriodClosePreviewDto
{
    public string CorrespondentName { get; set; } = "";
    public int PeriodNumber { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public int HawalaCount { get; set; }
    public int ReceiveCount { get; set; }
    public int SendCount { get; set; }
    public int PendingCount { get; set; }
    public int PaidCount { get; set; }
    public int CancelledCount { get; set; }
    public List<CorrespondentPeriodBalanceDto> OpeningBalances { get; set; } = [];
    public List<CorrespondentPeriodBalanceDto> ClosingBalances { get; set; } = [];
}
public sealed class CorrespondentPeriodBalanceDto
{
    public string CurrencyCode { get; set; } = "";
    public decimal TalabKar { get; set; }
    public decimal BadehKar { get; set; }
    public decimal Net => TalabKar - BadehKar;
}
public sealed class CorrespondentPeriodDto
{
    public long Id { get; set; }
    public long CorrespondentId { get; set; }
    public string CorrespondentName { get; set; } = "";
    public int PeriodNumber { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public DateTime ClosedAt { get; set; }
    public string? Note { get; set; }
    public int HawalaCount { get; set; }
    public List<CorrespondentPeriodBalanceDto> OpeningBalances { get; set; } = [];
    public List<CorrespondentPeriodBalanceDto> Balances { get; set; } = [];
    public List<HawalaDto> Hawalas { get; set; } = [];
}
