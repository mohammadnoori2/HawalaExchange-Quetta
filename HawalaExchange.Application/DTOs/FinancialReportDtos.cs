namespace HawalaExchange.Application.DTOs;

public class FinancialReportHeaderDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public string BranchName { get; set; } = "همه شعبه‌ها";
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencyName { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class FinancialStatementLineDto
{
    public string Number { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsSection { get; set; }
    public bool IsTotal { get; set; }
    public bool IsGrandTotal { get; set; }
}

public class BalanceSheetDto
{
    public FinancialReportHeaderDto Header { get; set; } = new();
    public List<FinancialStatementLineDto> AssetLines { get; set; } = new();
    public List<FinancialStatementLineDto> LiabilityAndEquityLines { get; set; } = new();
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
    public decimal TotalLiabilitiesAndEquity { get; set; }
    public decimal UnrealizedExchangeAdjustment { get; set; }
    public bool IsBalanced => Math.Abs(TotalAssets - TotalLiabilitiesAndEquity) < 0.01m;
}

public class ProfitLossStatementDto
{
    public FinancialReportHeaderDto Header { get; set; } = new();
    public List<FinancialStatementLineDto> Lines { get; set; } = new();
    public decimal GrossRevenue { get; set; }
    public decimal OperatingExpenses { get; set; }
    public decimal TaxExpense { get; set; }
    public decimal NetProfit { get; set; }
    public decimal UnrealizedExchangeGainLoss { get; set; }
    public decimal ComprehensiveProfit { get; set; }
}

public class DashboardSummaryDto
{
    public string ReportingCurrencyCode { get; set; } = string.Empty;
    public DateTime? LastActivityAt { get; set; }
    public int HawalaCount { get; set; }
    public int ExchangeCount { get; set; }
    public int AccountOperationCount { get; set; }
    public int CapitalInvestmentCount { get; set; }
    public int ExpenseCount { get; set; }
    public int TotalActivityCount { get; set; }
    public decimal NetProfit { get; set; }
    public List<DashboardRateDto> Rates { get; set; } = new();
    public List<DashboardDebtorDto> TopDebtors { get; set; } = new();
    public decimal TotalCustomerReceivables { get; set; }
    public List<DashboardLiquidityDto> Liquidity { get; set; } = new();
    public List<DashboardSeriesPointDto> DailyActivities { get; set; } = new();
    public List<DashboardSeriesPointDto> WeeklyProfit { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class DashboardRateDto
{
    public string Pair { get; set; } = string.Empty;
    public decimal BuyRate { get; set; }
    public decimal SellRate { get; set; }
    public DateTime EffectiveDate { get; set; }
}

public class DashboardDebtorDto
{
    public long AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Initial { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class DashboardLiquidityDto
{
    public string AccountType { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
}

public class DashboardSeriesPointDto
{
    public DateTime Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}
