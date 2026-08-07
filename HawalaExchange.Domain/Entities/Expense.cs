namespace HawalaExchange.Domain.Entities;

public class Expense : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }

    public DateTime ExpenseDate { get; set; }

    public string Title { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public decimal Amount { get; set; }

    public long ExpenseAccountId { get; set; }

    public long PaidFromAccountId { get; set; }

    public string? Description { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public Currency Currency { get; set; } = null!;

    public Account ExpenseAccount { get; set; } = null!;

    public Account PaidFromAccount { get; set; } = null!;

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}
