using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IFinancialReportService
{
    Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime asOfDate);

    Task<ProfitLossStatementDto> GetProfitLossStatementAsync(DateTime fromDate, DateTime toDate);

    Task<DashboardSummaryDto> GetDashboardSummaryAsync(
        DateTime fromDate,
        DateTime toDate,
        long reportingCurrencyId);
}
