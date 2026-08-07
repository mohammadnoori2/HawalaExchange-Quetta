namespace HawalaExchange.Domain.Entities;

public class MoneyExchangeOperation : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }

    public DateTime ExchangeDate { get; set; }

    public long FromAccountId { get; set; }

    public long FromCurrencyId { get; set; }

    public decimal FromAmount { get; set; }

    public long ToAccountId { get; set; }

    public long ToCurrencyId { get; set; }

    public decimal ToAmount { get; set; }

    public decimal ExchangeRate { get; set; }

    public long? RateBaseCurrencyId { get; set; }

    public long? RateQuoteCurrencyId { get; set; }

    /// <summary>Customer: exchange performed for a customer. Treasury: exchange of the exchange office's own funds.</summary>
    public string OperationType { get; set; } = "Treasury";

    /// <summary>The reporting currency in which cost and profit are measured.</summary>
    public long? ProfitCurrencyId { get; set; }

    public decimal CommissionAmount { get; set; }

    public decimal ExternalFeeAmount { get; set; }

    public decimal CostAmount { get; set; }

    public decimal RealizedProfit { get; set; }

    public decimal ExchangeProfitAmount { get; set; }

    public decimal InventoryCostIncrease { get; set; }

    public decimal InventoryCostDecrease { get; set; }

    public decimal ShortLiabilityIncrease { get; set; }

    public decimal ShortLiabilityDecrease { get; set; }

    public decimal DeferredAmount { get; set; }

    public string ProfitStatus { get; set; } = "NotCalculated";

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

    public Currency? ProfitCurrency { get; set; }

    public Currency? RateBaseCurrency { get; set; }

    public Currency? RateQuoteCurrency { get; set; }

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}
