namespace HawalaExchange.Application.DTOs;

public class AccountMoneyOperationDto
{
    public long Id { get; set; }

    public string OperationType { get; set; } = string.Empty;

    public string OperationTypeName =>
        OperationType == "Deposit" ? "رسید" :
        OperationType == "Withdraw" ? "برد" :
        OperationType;

    public DateTime OperationDate { get; set; }

    public long AccountId { get; set; }

    public string AccountName { get; set; } = string.Empty;

    public string AccountCode { get; set; } = string.Empty;

    public long CashOrBankAccountId { get; set; }

    public string CashOrBankAccountName { get; set; } = string.Empty;

    public string CashOrBankAccountCode { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Description { get; set; }
}

public class CreateAccountMoneyOperationDto
{
    public string OperationType { get; set; } = string.Empty;

    public DateTime OperationDate { get; set; } = DateTime.UtcNow;

    public long AccountId { get; set; }

    public long CashOrBankAccountId { get; set; }

    public long CurrencyId { get; set; }

    public decimal Amount { get; set; }

    public string? Description { get; set; }
}

public class UpdateAccountMoneyOperationDto
{
    public string OperationType { get; set; } = string.Empty;

    public DateTime OperationDate { get; set; }

    public long AccountId { get; set; }

    public long CashOrBankAccountId { get; set; }

    public long CurrencyId { get; set; }

    public decimal Amount { get; set; }

    public string? Description { get; set; }
}
