namespace HawalaExchange.Application.DTOs;

public sealed class AccountStatementFilter
{
    public long AccountId { get; set; }
    public DateTime From { get; set; } = DateTime.Today.AddMonths(-1);
    public DateTime To { get; set; } = DateTime.Today;
    public string Currency { get; set; } = "";
    public string Search { get; set; } = "";
    public string Direction { get; set; } = "";
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
}
public sealed class AccountStatementEntry
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string Document { get; set; } = "";
    public string Description { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public bool Pending { get; set; }
    public decimal Balance { get; set; }
}
public sealed class AccountStatementSummary
{
    public string Currency { get; set; } = "";
    public decimal Opening { get; set; }
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public decimal Closing => Opening + Credit - Debit;
    public decimal PendingCredit { get; set; }
    public decimal PendingDebit { get; set; }
}
public sealed class AccountStatementResult
{
    public string AccountName { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public List<AccountStatementEntry> Entries { get; set; } = [];
    public List<AccountStatementSummary> Summaries { get; set; } = [];
}
