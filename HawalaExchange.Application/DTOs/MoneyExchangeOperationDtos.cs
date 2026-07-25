namespace HawalaExchange.Application.DTOs;

public class MoneyExchangeOperationDto
{
    public long Id { get; set; }

    public DateTime ExchangeDate { get; set; }

    public long FromAccountId { get; set; }

    public string FromAccountName { get; set; } = string.Empty;

    public string FromAccountCode { get; set; } = string.Empty;

    public long FromCurrencyId { get; set; }

    public string FromCurrencyCode { get; set; } = string.Empty;

    public decimal FromAmount { get; set; }

    public long ToAccountId { get; set; }

    public string ToAccountName { get; set; } = string.Empty;

    public string ToAccountCode { get; set; } = string.Empty;

    public long ToCurrencyId { get; set; }

    public string ToCurrencyCode { get; set; } = string.Empty;

    public decimal ToAmount { get; set; }

    public decimal ExchangeRate { get; set; }
    public long? RateBaseCurrencyId { get; set; }
    public string RateBaseCurrencyCode { get; set; } = string.Empty;
    public long? RateQuoteCurrencyId { get; set; }
    public string RateQuoteCurrencyCode { get; set; } = string.Empty;

    public string OperationType { get; set; } = "Treasury";
    public long? ProfitCurrencyId { get; set; }
    public string ProfitCurrencyCode { get; set; } = string.Empty;
    public decimal CommissionAmount { get; set; }
    public decimal ExternalFeeAmount { get; set; }
    public decimal CostAmount { get; set; }
    public decimal RealizedProfit { get; set; }
    public decimal ExchangeProfitAmount { get; set; }
    public decimal DeferredAmount { get; set; }
    public string ProfitStatus { get; set; } = "NotCalculated";

    public string? Description { get; set; }
}

public class CreateMoneyExchangeOperationDto
{
    public DateTime ExchangeDate { get; set; } = DateTime.UtcNow;

    public long FromAccountId { get; set; }

    public long FromCurrencyId { get; set; }

    public decimal FromAmount { get; set; }

    public long ToAccountId { get; set; }

    public long ToCurrencyId { get; set; }

    public decimal ToAmount { get; set; }

    public decimal ExchangeRate { get; set; }

    public string OperationType { get; set; } = "Treasury";
    public long ProfitCurrencyId { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal ExternalFeeAmount { get; set; }

    public string? Description { get; set; }
}

public class UpdateMoneyExchangeOperationDto
{
    public DateTime ExchangeDate { get; set; }

    public long FromAccountId { get; set; }

    public long FromCurrencyId { get; set; }

    public decimal FromAmount { get; set; }

    public long ToAccountId { get; set; }

    public long ToCurrencyId { get; set; }

    public decimal ToAmount { get; set; }

    public decimal ExchangeRate { get; set; }

    public string OperationType { get; set; } = "Treasury";
    public long ProfitCurrencyId { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal ExternalFeeAmount { get; set; }

    public string? Description { get; set; }
}

public class CurrencyCostPositionDto
{
    public long CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public long ProfitCurrencyId { get; set; }
    public string ProfitCurrencyCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal CarryingAmount { get; set; }
    public decimal AverageCost => Quantity > 0 ? CarryingAmount / Quantity : 0;
    public decimal DeferredProceeds { get; set; }
    public bool IsShort => Quantity < 0;
}

public class MoneyExchangeProfitSummaryDto
{
    public decimal CustomerRealizedProfit { get; set; }
    public decimal TreasuryRealizedProfit { get; set; }
    public decimal TotalRealizedProfit => CustomerRealizedProfit + TreasuryRealizedProfit;
    public int DeferredOperationCount { get; set; }
}
