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

    public string? Description { get; set; }
}