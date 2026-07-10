namespace HawalaExchange.Application.DTOs;

public class ExpenseDto
{
    public long Id { get; set; }

    public DateTime ExpenseDate { get; set; }

    public string Title { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public long ExpenseAccountId { get; set; }

    public string ExpenseAccountName { get; set; } = string.Empty;

    public long PaidFromAccountId { get; set; }

    public string PaidFromAccountName { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public class CreateExpenseDto
{
    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

    public string Title { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public decimal Amount { get; set; }

    public long ExpenseAccountId { get; set; }

    public long PaidFromAccountId { get; set; }

    public string? Description { get; set; }
}
public class UpdateExpenseDto
{
    public DateTime ExpenseDate { get; set; }

    public string Title { get; set; } = string.Empty;

    public long CurrencyId { get; set; }

    public decimal Amount { get; set; }

    public long ExpenseAccountId { get; set; }

    public long PaidFromAccountId { get; set; }

    public string? Description { get; set; }
}
