using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class BalanceService : IBalanceService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILedgerService _ledgerService;
        private readonly IMapper _mapper;

        public BalanceService(ApplicationDbContext context, ILedgerService ledgerService, IMapper mapper)
        {
            _context = context;
            _ledgerService = ledgerService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<CustomerBalanceDto>> GetAllCustomerBalancesAsync()
        {
            var customers = await _context.Customers.Where(c => !c.IsArchived).ToListAsync();
            var result = new List<CustomerBalanceDto>();
            foreach (var c in customers)
            {
                var balance = await GetCustomerBalanceAsync(c.Id);
                if (balance != null) result.Add(balance);
            }
            return result;
        }

        public async Task<CustomerBalanceDto?> GetCustomerBalanceAsync(long customerId)
        {
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return null;

            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Customer" && a.ReferenceId == customerId);
            if (account == null)
                return new CustomerBalanceDto
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.FullName,
                    Balances = new List<BalanceDto>()
                };

            // ✅ Convert to List<BalanceDto>
            var balances = (await _ledgerService.GetAccountBalancesAsync(account.Id)).ToList();

            return new CustomerBalanceDto
            {
                CustomerId = customer.Id,
                CustomerName = customer.FullName,
                Balances = balances
            };
        }

        public async Task<IEnumerable<CorrespondentBalanceDto>> GetAllCorrespondentBalancesAsync()
        {
            var correspondents = await _context.Correspondents.Where(c => !c.IsArchived).ToListAsync();
            var result = new List<CorrespondentBalanceDto>();
            foreach (var c in correspondents)
            {
                var balance = await GetCorrespondentBalanceAsync(c.Id);
                if (balance != null) result.Add(balance);
            }
            return result;
        }

        public async Task<CorrespondentBalanceDto?> GetCorrespondentBalanceAsync(long correspondentId)
        {
            var correspondent = await _context.Correspondents.FindAsync(correspondentId);
            if (correspondent == null) return null;

            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == correspondentId);
            if (account == null)
                return new CorrespondentBalanceDto
                {
                    CorrespondentId = correspondent.Id,
                    CorrespondentName = correspondent.Name,
                    Balances = new List<BalanceDto>()
                };

            // ✅ Convert to List<BalanceDto>
            var balances = (await _ledgerService.GetAccountBalancesAsync(account.Id)).ToList();

            return new CorrespondentBalanceDto
            {
                CorrespondentId = correspondent.Id,
                CorrespondentName = correspondent.Name,
                Balances = balances
            };
        }

        public async Task<IEnumerable<CashBalanceDto>> GetAllCashBalancesAsync(long branchId)
        {
            var accounts = await _context.Accounts.Where(a => a.AccountType == "Cash").ToListAsync();
            var result = new List<CashBalanceDto>();
            foreach (var account in accounts)
            {
                var balance = await GetCashBalanceAsync(account.Id);
                if (balance != null) result.Add(balance);
            }
            return result;
        }

        public async Task<CashBalanceDto?> GetCashBalanceAsync(long accountId)
        {
            var account = await _context.Accounts.FindAsync(accountId);
            if (account == null) return null;

            // ✅ Convert to List<BalanceDto>
            var balances = (await _ledgerService.GetAccountBalancesAsync(accountId)).ToList();

            return new CashBalanceDto
            {
                AccountId = account.Id,
                AccountName = account.AccountName,
                Balances = balances
            };
        }

        public async Task<BranchBalanceDto?> GetBranchBalanceAsync(long branchId)
        {
            var branch = await _context.Branches.FindAsync(branchId);

            var cashBalances = new List<BalanceDto>();
            var bankBalances = new List<BalanceDto>();
            var correspondentBalances = new List<BalanceDto>();

            // Cash accounts
            var cashAccounts = await _context.Accounts.Where(a => a.AccountType == "Cash").ToListAsync();
            foreach (var a in cashAccounts)
            {
                var b = await _ledgerService.GetAccountBalancesAsync(a.Id);
                cashBalances.AddRange(b); // AddRange works with IEnumerable
            }

            // Bank accounts
            var bankAccounts = await _context.Accounts.Where(a => a.AccountType == "Bank").ToListAsync();
            foreach (var a in bankAccounts)
            {
                var b = await _ledgerService.GetAccountBalancesAsync(a.Id);
                bankBalances.AddRange(b);
            }

            // Correspondent accounts
            var corrAccounts = await _context.Accounts.Where(a => a.AccountType == "Correspondent").ToListAsync();
            foreach (var a in corrAccounts)
            {
                var b = await _ledgerService.GetAccountBalancesAsync(a.Id);
                correspondentBalances.AddRange(b);
            }

            return new BranchBalanceDto
            {
                BranchId = branchId,
                BranchName = branch?.Name ?? $"Branch {branchId}",
                CashBalances = cashBalances,          // Already List<BalanceDto>
                BankBalances = bankBalances,          // Already List<BalanceDto>
                CorrespondentBalances = correspondentBalances // Already List<BalanceDto>
            };
        }

        public async Task<IEnumerable<BalanceDto>> GetAccountBalanceAsync(long accountId)
        {
            return await _ledgerService.GetAccountBalancesAsync(accountId);
        }

        public async Task<bool> ValidateBadehkarLimitAsync(long accountId, long currencyId, decimal amount)
        {
            var limit = await _context.AccountBadehkarLimits
                .FirstOrDefaultAsync(l => l.AccountId == accountId && l.CurrencyId == currencyId && l.IsActive);
            if (limit == null) return true;

            var currentBalance = await _ledgerService.GetAccountBalanceAsync(accountId, currencyId);
            var newBalance = currentBalance - amount;
            return newBalance >= -limit.BadehkarLimit;
        }

        public async Task<IEnumerable<AccountBalanceDto>> GetAccountsWithLimitsAsync()
        {
            var limits = await _context.AccountBadehkarLimits
                .Where(l => l.IsActive)
                .Include(l => l.Account)
                .Include(l => l.Currency)
                .ToListAsync();

            var result = new List<AccountBalanceDto>();
            foreach (var limit in limits)
            {
                var balance = await _ledgerService.GetAccountBalanceAsync(limit.AccountId, limit.CurrencyId);
                result.Add(new AccountBalanceDto
                {
                    AccountId = limit.AccountId,
                    AccountName = limit.Account?.AccountName ?? "Unknown",
                    CurrencyId = limit.CurrencyId,
                    Balance = balance,
                    BadehkarLimit = limit.BadehkarLimit,
                    AvailableBalance = balance + limit.BadehkarLimit,
                    IsOverLimit = balance < -limit.BadehkarLimit
                });
            }
            return result;
        }
    }
}