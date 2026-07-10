using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITransactionService _transactionService;
        private readonly ILedgerService _ledgerService;
        private readonly IExpenseService _expenseService;
        private readonly IMapper _mapper;

        public ReportService(
            ApplicationDbContext context,
            ITransactionService transactionService,
            ILedgerService ledgerService,
            IExpenseService expenseService,
            IMapper mapper)
        {
            _context = context;
            _transactionService = transactionService;
            _ledgerService = ledgerService;
            _expenseService = expenseService;
            _mapper = mapper;
        }

        public async Task<DailyReportDto> GetDailyReportAsync(DateTime date, long branchId)
        {
            var start = date.Date;
            var end = start.AddDays(1);
            var transactions = await _transactionService.GetByDateRangeAsync(start, end);
            var branchTxns = transactions.Where(t => t.BranchId == branchId);

            var totalSend = branchTxns
                .Where(t => t.TransactionType == "HawalaSend" && t.Status != "Cancel")
                .SelectMany(t => t.TransactionDetails ?? new List<TransactionDetailDto>())
                .Where(d => d.FromAmount.HasValue)
                .Sum(d => d.FromAmount ?? 0);

            var totalReceive = branchTxns
                .Where(t => t.TransactionType == "HawalaReceive" && t.Status != "Cancel")
                .SelectMany(t => t.TransactionDetails ?? new List<TransactionDetailDto>())
                .Where(d => d.ToAmount.HasValue)
                .Sum(d => d.ToAmount ?? 0);

            var totalCommission = branchTxns
                .Where(t => t.Status != "Cancel")
                .SelectMany(t => t.TransactionDetails ?? new List<TransactionDetailDto>())
                .Sum(d => d.CommissionAmount);

            var expenses = await _expenseService.GetExpensesByDateRangeAsync(start, end);
            var expensesTotal = expenses.Sum(e => e.Amount);

            var branch = await _context.Branches.FindAsync(branchId);

            return new DailyReportDto
            {
                Date = date,
                BranchId = branchId,
                BranchName = branch?.Name ?? $"Branch {branchId}",
                TotalTransactions = branchTxns.Count(),
                TotalSendAmount = totalSend,
                TotalReceiveAmount = totalReceive,
                TotalCommission = totalCommission,
                TotalExpenses = expensesTotal,
                NetIncome = totalCommission - expensesTotal
            };
        }

        public async Task<IEnumerable<DailyReportDto>> GetDailyReportRangeAsync(DateTime fromDate, DateTime toDate, long? branchId = null)
        {
            var result = new List<DailyReportDto>();
            for (var d = fromDate.Date; d <= toDate.Date; d = d.AddDays(1))
                result.Add(await GetDailyReportAsync(d, branchId ?? 1));
            return result;
        }

        public async Task<IEnumerable<TransactionReportDto>> GetTransactionReportAsync(DateTime fromDate, DateTime toDate, long? branchId = null, string? status = null)
        {
            var txns = await _transactionService.GetByDateRangeAsync(fromDate, toDate);
            if (branchId.HasValue) txns = txns.Where(t => t.BranchId == branchId.Value);
            if (!string.IsNullOrEmpty(status)) txns = txns.Where(t => t.Status == status);

            var result = new List<TransactionReportDto>();
            foreach (var t in txns)
            {
                var d = t.TransactionDetails?.FirstOrDefault();
                result.Add(new TransactionReportDto
                {
                    TransactionNo = t.TransactionNo,
                    TransactionType = t.TransactionType,
                    CreatedAt = t.CreatedAt,
                    CustomerName = t.CustomerFullName ?? "N/A",
                    SenderName = d?.SenderName ?? "N/A",
                    ReceiverName = d?.ReceiverName ?? "N/A",
                    FromCurrency = d?.FromCurrencyCode ?? "N/A",
                    FromAmount = d?.FromAmount ?? 0,
                    ToCurrency = d?.ToCurrencyCode ?? "N/A",
                    ToAmount = d?.ToAmount ?? 0,
                    Commission = d?.CommissionAmount ?? 0,
                    Status = t.Status
                });
            }
            return result;
        }

        public async Task<IEnumerable<CommissionReportDto>> GetCommissionReportAsync(DateTime fromDate, DateTime toDate, long? branchId = null)
        {
            var txns = await _transactionService.GetByDateRangeAsync(fromDate, toDate);
            if (branchId.HasValue) txns = txns.Where(t => t.BranchId == branchId.Value);

            var result = new List<CommissionReportDto>();
            for (var d = fromDate.Date; d <= toDate.Date; d = d.AddDays(1))
            {
                var dayTxns = txns.Where(t => t.CreatedAt.Date == d);
                result.Add(new CommissionReportDto
                {
                    Date = d,
                    BranchId = branchId ?? 1,
                    BranchName = (await _context.Branches.FindAsync(branchId ?? 1))?.Name ?? $"Branch {branchId ?? 1}",
                    TotalCommission = dayTxns.SelectMany(t => t.TransactionDetails ?? new List<TransactionDetailDto>()).Sum(x => x.CommissionAmount),
                    TotalAgentCommission = dayTxns.SelectMany(t => t.TransactionDetails ?? new List<TransactionDetailDto>()).Sum(x => x.AgentCommissionAmount),
                    NetCommission = dayTxns.SelectMany(t => t.TransactionDetails ?? new List<TransactionDetailDto>()).Sum(x => x.CommissionAmount - x.AgentCommissionAmount)
                });
            }
            return result;
        }

        public async Task<IEnumerable<TrialBalanceDto>> GetTrialBalanceAsync(DateTime asOfDate, long? branchId = null)
        {
            var entries = await _ledgerService.GetEntriesByDateRangeAsync(DateTime.MinValue, asOfDate);
            // If branchId is provided, filter accounts? For now, we return all.
            var grouped = entries
                .GroupBy(e => e.AccountId)
                .Select(g => new TrialBalanceDto
                {
                    AccountId = g.Key,
                    AccountCode = g.First().AccountCode ?? "N/A",
                    AccountName = g.First().AccountName ?? "N/A",
                    TotalDebit = g.Sum(e => e.TalabKar),
                    TotalCredit = g.Sum(e => e.BadehKar),
                    Balance = g.Sum(e => e.TalabKar - e.BadehKar),
                    BalanceType = g.Sum(e => e.TalabKar - e.BadehKar) >= 0 ? "Debit" : "Credit"
                })
                .OrderBy(g => g.AccountCode);
            return grouped;
        }

        public async Task<IEnumerable<TransactionReportDto>> GetCustomerTransactionReportAsync(long customerId, DateTime fromDate, DateTime toDate)
        {
            // Reuse transaction report with customer filter
            var all = await GetTransactionReportAsync(fromDate, toDate);
            // Note: the report doesn't have customerId, we need to get transactions by customer from service
            var txns = await _transactionService.GetByCustomerAsync(customerId);
            var filtered = txns.Where(t => t.CreatedAt >= fromDate && t.CreatedAt <= toDate);
            var result = new List<TransactionReportDto>();
            foreach (var t in filtered)
            {
                var d = t.TransactionDetails?.FirstOrDefault();
                result.Add(new TransactionReportDto
                {
                    TransactionNo = t.TransactionNo,
                    TransactionType = t.TransactionType,
                    CreatedAt = t.CreatedAt,
                    CustomerName = t.CustomerFullName ?? "N/A",
                    SenderName = d?.SenderName ?? "N/A",
                    ReceiverName = d?.ReceiverName ?? "N/A",
                    FromCurrency = d?.FromCurrencyCode ?? "N/A",
                    FromAmount = d?.FromAmount ?? 0,
                    ToCurrency = d?.ToCurrencyCode ?? "N/A",
                    ToAmount = d?.ToAmount ?? 0,
                    Commission = d?.CommissionAmount ?? 0,
                    Status = t.Status
                });
            }
            return result;
        }

        public async Task<IEnumerable<TransactionReportDto>> GetCorrespondentTransactionReportAsync(long correspondentId, DateTime fromDate, DateTime toDate)
        {
            // Similar, need to filter by correspondent in details – we can query transaction details.
            var details = await _context.TransactionDetails
                .Where(d => d.CorrespondentId == correspondentId)
                .Include(d => d.Transaction)
                .Where(d => d.Transaction != null && d.Transaction.CreatedAt >= fromDate && d.Transaction.CreatedAt <= toDate)
                .ToListAsync();

            var result = new List<TransactionReportDto>();
            foreach (var d in details)
            {
                var t = d.Transaction;
                if (t == null) continue;
                result.Add(new TransactionReportDto
                {
                    TransactionNo = t.TransactionNo,
                    TransactionType = t.TransactionType,
                    CreatedAt = t.CreatedAt,
                    CustomerName = t.CustomerFullName ?? "N/A",
                    SenderName = d.SenderName ?? "N/A",
                    ReceiverName = d.ReceiverName ?? "N/A",
                    FromCurrency = d.FromCurrency?.Code ?? "N/A",
                    FromAmount = d.FromAmount ?? 0,
                    ToCurrency = d.ToCurrency?.Code ?? "N/A",
                    ToAmount = d.ToAmount ?? 0,
                    Commission = d.CommissionAmount,
                    Status = t.Status
                });
            }
            return result;
        }

        public async Task<decimal> GetTotalCommissionAsync(DateTime fromDate, DateTime toDate, long? branchId = null)
        {
            var report = await GetCommissionReportAsync(fromDate, toDate, branchId);
            return report.Sum(r => r.TotalCommission);
        }

        public async Task<decimal> GetTotalExpensesAsync(DateTime fromDate, DateTime toDate, long? branchId = null)
        {
            var expenses = await _expenseService.GetExpensesByDateRangeAsync(fromDate, toDate);
            // if branchId, filter by branch – but expense service doesn't have branch filter directly, we can filter via transaction
            if (branchId.HasValue)
            {
                expenses = expenses
                    .Where(e => _context.Transactions.Any(t =>  t.BranchId == branchId));
            }
            return expenses.Sum(e => e.Amount);
        }

        public async Task<decimal> GetNetProfitAsync(DateTime fromDate, DateTime toDate, long? branchId = null)
        {
            var totalCommission = await GetTotalCommissionAsync(fromDate, toDate, branchId);
            var totalExpenses = await GetTotalExpensesAsync(fromDate, toDate, branchId);
            return totalCommission - totalExpenses;
        }
    }
}