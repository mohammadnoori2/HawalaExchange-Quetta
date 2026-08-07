namespace HawalaExchange.Domain.Entities;

public class AccountMoneyOperation : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }

    // Deposit / Withdraw
    public string OperationType { get; set; } = string.Empty;

    public DateTime OperationDate { get; set; }

    public long AccountId { get; set; }

    public long CashOrBankAccountId { get; set; }

    public long CurrencyId { get; set; }

    public decimal Amount { get; set; }

    public string? Description { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public Account Account { get; set; } = null!;

    public Account CashOrBankAccount { get; set; } = null!;

    public Currency Currency { get; set; } = null!;

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}
