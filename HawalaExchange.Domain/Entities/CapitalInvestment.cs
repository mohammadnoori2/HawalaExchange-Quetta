namespace HawalaExchange.Domain.Entities;

public class CapitalInvestment : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }

    public long CurrencyId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Currency selected for measuring the carrying value of this capital.</summary>
    public long? ProfitCurrencyId { get; set; }

    /// <summary>Opening carrying value in ProfitCurrency. No reference/market rate is used.</summary>
    public decimal? ProfitCurrencyAmount { get; set; }

    public long ReceivingAccountId { get; set; }

    public long CapitalAccountId { get; set; }

    public DateTime InvestmentDate { get; set; }

    public string? Description { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public Currency Currency { get; set; } = null!;

    public Currency? ProfitCurrency { get; set; }

    public Account ReceivingAccount { get; set; } = null!;

    public Account CapitalAccount { get; set; } = null!;

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}
