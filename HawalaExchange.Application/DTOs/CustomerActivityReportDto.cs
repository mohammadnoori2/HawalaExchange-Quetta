namespace HawalaExchange.Application.DTOs;

public sealed class CustomerActivityReportDto
{
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerCode { get; set; } = "";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public List<CustomerActivityRowDto> Rows { get; set; } = [];
    public List<CustomerActivityBalanceDto> Balances { get; set; } = [];
}

public sealed class CustomerActivityBalanceDto
{
    public string Currency { get; set; } = "";
    public decimal Opening { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Closing => Opening + Credit - Debit;
}

public sealed class CustomerActivityRowDto
{
    public DateTime Date { get; init; }
    public string Type { get; init; } = "";
    public string? Reference { get; init; }
    public decimal TalabKar { get; init; }
    public decimal BadehKar { get; init; }
    public string Currency { get; init; } = "";
    public string? AccountFrom { get; init; }
    public string? AccountTo { get; init; }
    public string? Description { get; init; }
    public string DisplayDescription { get; set; } = "";
    public decimal RunningBalance { get; set; }
}
