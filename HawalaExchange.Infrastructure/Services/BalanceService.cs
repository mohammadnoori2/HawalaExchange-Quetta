using System.Data;
using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HawalaExchange.Application.Services
{
    public class BalanceService : IBalanceService
    {
        private readonly ApplicationDbContext _context;

        // Keep the established constructor contract so existing registrations and callers do not change.
        public BalanceService(ApplicationDbContext context, ILedgerService ledgerService, IMapper mapper)
        {
            _context = context;
        }

        public async Task<IEnumerable<CustomerBalanceDto>> GetAllCustomerBalancesAsync()
        {
            var customers = await _context.Customers.AsNoTracking()
                .Where(c => !c.IsArchived)
                .OrderBy(c => c.FullName)
                .Select(c => new { c.Id, c.FullName })
                .ToListAsync();
            var rows = await ReadBalancesAsync(ownerType: "Customer");
            var byCustomer = rows.Where(x => x.CustomerId.HasValue)
                .GroupBy(x => x.CustomerId!.Value)
                .ToDictionary(x => x.Key, BuildBalances);

            return customers.Select(customer => new CustomerBalanceDto
            {
                CustomerId = customer.Id,
                CustomerName = customer.FullName,
                Balances = byCustomer.GetValueOrDefault(customer.Id) ?? []
            }).ToList();
        }

        public async Task<CustomerBalanceDto?> GetCustomerBalanceAsync(long customerId)
        {
            var customer = await _context.Customers.AsNoTracking()
                .Where(x => x.Id == customerId)
                .Select(x => new { x.Id, x.FullName })
                .SingleOrDefaultAsync();
            if (customer == null)
                return null;

            return new CustomerBalanceDto
            {
                CustomerId = customer.Id,
                CustomerName = customer.FullName,
                Balances = BuildBalances(await ReadBalancesAsync(customerId: customerId))
            };
        }

        public async Task<IEnumerable<CorrespondentBalanceDto>> GetAllCorrespondentBalancesAsync()
        {
            var correspondents = await _context.Correspondents.AsNoTracking()
                .Where(c => !c.IsArchived)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            var rows = await ReadBalancesAsync(ownerType: "Correspondent");
            var byCorrespondent = rows.Where(x => x.CorrespondentId.HasValue)
                .GroupBy(x => x.CorrespondentId!.Value)
                .ToDictionary(x => x.Key, BuildBalances);

            return correspondents.Select(correspondent => new CorrespondentBalanceDto
            {
                CorrespondentId = correspondent.Id,
                CorrespondentName = correspondent.Name,
                Balances = byCorrespondent.GetValueOrDefault(correspondent.Id) ?? []
            }).ToList();
        }

        public async Task<CorrespondentBalanceDto?> GetCorrespondentBalanceAsync(long correspondentId)
        {
            var correspondent = await _context.Correspondents.AsNoTracking()
                .Where(x => x.Id == correspondentId)
                .Select(x => new { x.Id, x.Name })
                .SingleOrDefaultAsync();
            if (correspondent == null)
                return null;

            return new CorrespondentBalanceDto
            {
                CorrespondentId = correspondent.Id,
                CorrespondentName = correspondent.Name,
                Balances = BuildBalances(await ReadBalancesAsync(correspondentId: correspondentId))
            };
        }

        public async Task<IEnumerable<CashBalanceDto>> GetAllCashBalancesAsync(long branchId)
        {
            var accounts = await _context.Accounts.AsNoTracking()
                .Where(a => a.AccountType == "Cash")
                .OrderBy(a => a.AccountCode)
                .Select(a => new { a.Id, a.AccountName })
                .ToListAsync();
            var rows = await ReadBalancesAsync(accountType: "Cash");
            var byAccount = rows.GroupBy(x => x.AccountId).ToDictionary(x => x.Key, BuildBalances);

            return accounts.Select(account => new CashBalanceDto
            {
                AccountId = account.Id,
                AccountName = account.AccountName,
                Balances = byAccount.GetValueOrDefault(account.Id) ?? []
            }).ToList();
        }

        public async Task<CashBalanceDto?> GetCashBalanceAsync(long accountId)
        {
            var account = await _context.Accounts.AsNoTracking()
                .Where(x => x.Id == accountId)
                .Select(x => new { x.Id, x.AccountName })
                .SingleOrDefaultAsync();
            if (account == null)
                return null;

            return new CashBalanceDto
            {
                AccountId = account.Id,
                AccountName = account.AccountName,
                Balances = BuildBalances(await ReadBalancesAsync(accountId: accountId))
            };
        }

        public async Task<BranchBalanceDto?> GetBranchBalanceAsync(long branchId)
        {
            var branchName = await _context.Branches.AsNoTracking()
                .Where(x => x.Id == branchId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync();
            var rows = await ReadBalancesAsync();

            return new BranchBalanceDto
            {
                BranchId = branchId,
                BranchName = branchName ?? $"Branch {branchId}",
                CashBalances = ToFlatBalances(rows.Where(x => x.AccountType == "Cash")),
                BankBalances = ToFlatBalances(rows.Where(x => x.AccountType == "Bank")),
                CorrespondentBalances = ToFlatBalances(rows.Where(x => x.AccountType == "Correspondent"))
            };
        }

        public async Task<IEnumerable<BalanceDto>> GetAccountBalanceAsync(long accountId, DateTime? asOfDate = null)
        {
            return BuildBalances(await ReadBalancesAsync(accountId: accountId, asOfDate: asOfDate));
        }

        public async Task<bool> ValidateBadehkarLimitAsync(long accountId, long currencyId, decimal amount)
        {
            var limit = await _context.AccountBadehkarLimits.AsNoTracking()
                .Where(l => l.AccountId == accountId && l.CurrencyId == currencyId && l.IsActive)
                .Select(l => (decimal?)l.BadehkarLimit)
                .SingleOrDefaultAsync();
            if (!limit.HasValue)
                return true;

            var currentBalance = (await ReadBalancesAsync(accountId: accountId))
                .Where(x => x.CurrencyId == currencyId)
                .Select(x => x.Balance)
                .SingleOrDefault();
            return currentBalance - amount >= -limit.Value;
        }

        public async Task<IEnumerable<AccountBalanceDto>> GetAccountsWithLimitsAsync()
        {
            var limits = await _context.AccountBadehkarLimits.AsNoTracking()
                .Where(l => l.IsActive)
                .Select(l => new
                {
                    l.AccountId,
                    AccountName = l.Account != null ? l.Account.AccountName : "Unknown",
                    l.CurrencyId,
                    l.BadehkarLimit
                })
                .ToListAsync();
            var balances = (await ReadBalancesAsync())
                .ToDictionary(x => (x.AccountId, x.CurrencyId), x => x.Balance);

            return limits.Select(limit =>
            {
                var balance = balances.GetValueOrDefault((limit.AccountId, limit.CurrencyId));
                return new AccountBalanceDto
                {
                    AccountId = limit.AccountId,
                    AccountName = limit.AccountName,
                    CurrencyId = limit.CurrencyId,
                    Balance = balance,
                    BadehkarLimit = limit.BadehkarLimit,
                    AvailableBalance = balance + limit.BadehkarLimit,
                    IsOverLimit = balance < -limit.BadehkarLimit
                };
            }).ToList();
        }

        private async Task<List<AccountBalanceRow>> ReadBalancesAsync(
            long? accountId = null,
            long? customerId = null,
            long? correspondentId = null,
            string? ownerType = null,
            string? accountType = null,
            DateTime? asOfDate = null)
        {
            var connection = (SqlConnection)_context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                var transaction = _context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
                await using var command = new SqlCommand("[dbo].[usp_GetAccountBalances_v1]", connection, transaction)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };
                command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
                AddNullable(command, "@AccountId", SqlDbType.BigInt, accountId);
                AddNullable(command, "@CustomerId", SqlDbType.BigInt, customerId);
                AddNullable(command, "@CorrespondentId", SqlDbType.BigInt, correspondentId);
                AddNullable(command, "@OwnerType", SqlDbType.NVarChar, ownerType, 20);
                AddNullable(command, "@AccountType", SqlDbType.NVarChar, accountType, 50);
                AddNullable(command, "@AsOfDate", SqlDbType.DateTime2, asOfDate);

                var rows = new List<AccountBalanceRow>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add(new AccountBalanceRow(
                        reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetInt64(3),
                        reader.IsDBNull(4) ? null : reader.GetInt64(4),
                        reader.GetInt64(5), reader.GetString(6), reader.GetDecimal(7)));
                }
                return rows;
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private static void AddNullable(SqlCommand command, string name, SqlDbType type, object? value, int? size = null)
        {
            var parameter = size.HasValue
                ? command.Parameters.Add(name, type, size.Value)
                : command.Parameters.Add(name, type);
            parameter.Value = value ?? DBNull.Value;
        }

        private static List<BalanceDto> BuildBalances(IEnumerable<AccountBalanceRow> rows) => rows
            .GroupBy(x => new { x.CurrencyId, x.CurrencyCode })
            .Select(x => new BalanceDto
            {
                CurrencyId = x.Key.CurrencyId,
                CurrencyCode = x.Key.CurrencyCode,
                Balance = x.Sum(row => row.Balance)
            })
            .Where(x => x.Balance != 0)
            .OrderBy(x => x.CurrencyCode)
            .ToList();

        private static List<BalanceDto> ToFlatBalances(IEnumerable<AccountBalanceRow> rows) => rows
            .Select(x => new BalanceDto
            {
                CurrencyId = x.CurrencyId,
                CurrencyCode = x.CurrencyCode,
                Balance = x.Balance
            })
            .OrderBy(x => x.CurrencyCode)
            .ToList();

        private sealed record AccountBalanceRow(
            long AccountId, string AccountName, string AccountType,
            long? CustomerId, long? CorrespondentId,
            long CurrencyId, string CurrencyCode, decimal Balance);
    }
}
