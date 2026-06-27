using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IReportService
    {
        Task<DailyReportDto> GetDailyReportAsync(DateTime date, long branchId);
        Task<IEnumerable<DailyReportDto>> GetDailyReportRangeAsync(DateTime fromDate, DateTime toDate, long? branchId = null);
        Task<IEnumerable<TransactionReportDto>> GetTransactionReportAsync(DateTime fromDate, DateTime toDate, long? branchId = null, string? status = null);
        Task<IEnumerable<CommissionReportDto>> GetCommissionReportAsync(DateTime fromDate, DateTime toDate, long? branchId = null);
        Task<IEnumerable<TrialBalanceDto>> GetTrialBalanceAsync(DateTime asOfDate, long? branchId = null);
        Task<IEnumerable<TransactionReportDto>> GetCustomerTransactionReportAsync(long customerId, DateTime fromDate, DateTime toDate);
        Task<IEnumerable<TransactionReportDto>> GetCorrespondentTransactionReportAsync(long correspondentId, DateTime fromDate, DateTime toDate);
        Task<decimal> GetTotalCommissionAsync(DateTime fromDate, DateTime toDate, long? branchId = null);
        Task<decimal> GetTotalExpensesAsync(DateTime fromDate, DateTime toDate, long? branchId = null);
        Task<decimal> GetNetProfitAsync(DateTime fromDate, DateTime toDate, long? branchId = null);
    }
}