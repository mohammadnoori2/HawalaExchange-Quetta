namespace HawalaExchange.Domain.Entities;

public class MoneyExchangeOperation
{
    public long Id { get; set; }

    public DateTime ExchangeDate { get; set; }

    public long FromAccountId { get; set; }

    public long FromCurrencyId { get; set; }

    public decimal FromAmount { get; set; }

    public long ToAccountId { get; set; }

    public long ToCurrencyId { get; set; }

    public decimal ToAmount { get; set; }

    public decimal ExchangeRate { get; set; }

    public string? Description { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public Account FromAccount { get; set; } = null!;

    public Account ToAccount { get; set; } = null!;

    public Currency FromCurrency { get; set; } = null!;

    public Currency ToCurrency { get; set; } = null!;

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}