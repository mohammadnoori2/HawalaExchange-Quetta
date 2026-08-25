using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class AccountService : BaseService<Account, AccountDto, CreateAccountDto, UpdateAccountDto>, IAccountService
    {
        private readonly ILedgerService _ledgerService;
        private readonly IAuditLogService _auditLogService;

        public AccountService(ApplicationDbContext context, IMapper mapper, ILedgerService ledgerService, IAuditLogService auditLogService)
            : base(context, mapper)
        {
            _ledgerService = ledgerService;
            _auditLogService = auditLogService;
        }

        // در AccountService.cs

        public override async Task<AccountDto> CreateAsync(CreateAccountDto createDto)
        {
            await using var transaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync()
                : null;

            try
            {
                if (string.IsNullOrWhiteSpace(createDto.AccountCode))
                    createDto.AccountCode = await GenerateAccountCodeAsync(createDto.AccountType);

                var entity = _mapper.Map<Account>(createDto);
                _context.PrepareTenantEntity(entity);
                entity.CreatedAt = DateTime.UtcNow;
                await ValidateCreateAsync(entity, createDto);

                await _dbSet.AddAsync(entity);
                await _context.SaveChangesAsync();

                if (createDto.HasInitialBalance && createDto.InitialBalances?.Count > 0)
                {
                    var openingTransaction = new Transaction
                    {
                        TransactionNo = await GenerateOpeningTransactionNumberAsync(),
                        TransactionType = "OpeningBalance",
                        BranchId = await _context.GetDefaultBranchIdAsync(),
                        Status = "Paid",
                        Remarks = $"موجودی اولیه حساب {entity.AccountName}",
                        CreatedBy = _context.RequireCurrentUserId(),
                        CreatedAt = DateTime.UtcNow
                    };

                    await _context.Transactions.AddAsync(openingTransaction);
                    await _context.SaveChangesAsync();

                    var ledgerEntries = createDto.InitialBalances.Select(initialBalance =>
                    {
                        var entry = new LedgerEntry
                        {
                            TransactionId = openingTransaction.Id,
                            AccountId = entity.Id,
                            CurrencyId = initialBalance.CurrencyId,
                            TalabKar = initialBalance.Direction == "Credit" ? initialBalance.Amount : 0,
                            BadehKar = initialBalance.Direction == "Debit" ? initialBalance.Amount : 0,
                            Description = string.IsNullOrWhiteSpace(initialBalance.Description)
                                ? "موجودی اولیه"
                                : $"موجودی اولیه: {initialBalance.Description}",
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.PrepareTenantEntity(entry);
                        return entry;
                    }).ToList();

                    await _context.LedgerEntries.AddRangeAsync(ledgerEntries);
                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync("CREATE", "Transactions", openingTransaction.Id, null,
                        $"موجودی اولیه حساب {entity.AccountName} ثبت شد", _context.RequireCurrentUserId());
                }

                await _auditLogService.LogAsync("CREATE", "Accounts", entity.Id, null,
                    $"حساب {entity.AccountName} ایجاد شد", _context.RequireCurrentUserId());

                if (transaction != null)
                    await transaction.CommitAsync();

                return _mapper.Map<AccountDto>(entity);
            }
            catch (DbUpdateException ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();

                DetachAddedEntities();

                if (FindSqlException(ex) is { Number: 2601 or 2627 } sqlException)
                {
                    if (sqlException.Message.Contains("IX_Accounts_TenantId_CustomerId", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "این مشتری قبلاً حساب دارد. اگر حساب او بایگانی شده است، آن را از بخش بایگانی‌ها دوباره فعال کنید.", ex);

                    if (sqlException.Message.Contains("IX_Accounts_TenantId_CorrespondentId", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "این نمایندگی قبلاً حساب دارد. اگر حساب آن بایگانی شده است، آن را از بخش بایگانی‌ها دوباره فعال کنید.", ex);
                }

                throw new InvalidOperationException(
                    "حساب ذخیره نشد. لطفاً اطلاعات حساب را بررسی کرده و دوباره تلاش کنید.", ex);
            }
            catch
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<AccountDto?> GetByAccountCodeAsync(string accountCode)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
            return entity == null ? null : _mapper.Map<AccountDto>(entity);
        }

        public async Task<IEnumerable<AccountDto>> GetByAccountTypeAsync(string accountType)
        {
            var entities = await _dbSet
                .Where(a => a.AccountType == accountType && !a.IsArchived)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountDto>>(entities);
        }

        public async Task<IEnumerable<AccountDto>> GetByReferenceAsync(string referenceType, long referenceId)
        {
            var entities = await _dbSet
                .Where(a =>
                    ((referenceType == "Customer" && a.CustomerId == referenceId) ||
                     (referenceType == "Correspondent" && a.CorrespondentId == referenceId)) &&
                    !a.IsArchived)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountDto>>(entities);
        }

        public async Task<IEnumerable<AccountDto>> GetActiveAccountsAsync()
        {
            var entities = await _dbSet
                .Where(a => !a.IsArchived)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountDto>>(entities);
        }

        public async Task<AccountDto> ArchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Account with ID {id} not found.");

            entity.IsArchived = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<AccountDto>(entity);
        }

        public async Task<AccountDto> UnarchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Account with ID {id} not found.");

            entity.IsArchived = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<AccountDto>(entity);
        }

        public async Task<decimal> GetAccountBalanceAsync(long accountId, long currencyId)
        {
            return await _ledgerService.GetAccountBalanceAsync(accountId, currencyId);
        }

        public async Task<IEnumerable<BalanceDto>> GetAllAccountBalancesAsync(long accountId)
        {
            return await _ledgerService.GetAccountBalancesAsync(accountId);
        }

        protected override async Task ValidateCreateAsync(Account entity, CreateAccountDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.AccountCode))
            {
                entity.AccountCode = await GenerateAccountCodeAsync(dto.AccountType);
            }
            else
            {
                if (await _dbSet.AnyAsync(a => a.AccountCode == dto.AccountCode))
                    throw new InvalidOperationException($"کد حساب '{dto.AccountCode}' قبلاً وجود دارد.");
            }

            await ValidateUniqueOwnerAsync(entity);
        }

        protected override async Task ValidateUpdateAsync(Account entity, UpdateAccountDto dto)
        {
            await ValidateUniqueOwnerAsync(entity, entity.Id);
        }

        private async Task ValidateUniqueOwnerAsync(Account entity, long? accountIdToExclude = null)
        {
            if (entity.CustomerId.HasValue)
            {
                var existingAccount = await _dbSet.AsNoTracking().FirstOrDefaultAsync(account =>
                    account.CustomerId == entity.CustomerId &&
                    (!accountIdToExclude.HasValue || account.Id != accountIdToExclude.Value));
                if (existingAccount != null)
                {
                    var archiveHint = existingAccount.IsArchived
                        ? " حساب قبلی در بایگانی است؛ آن را از بخش بایگانی‌ها دوباره فعال کنید."
                        : string.Empty;
                    throw new InvalidOperationException($"این مشتری قبلاً حساب «{existingAccount.AccountName}» دارد.{archiveHint}");
                }
            }

            if (entity.CorrespondentId.HasValue)
            {
                var existingAccount = await _dbSet.AsNoTracking().FirstOrDefaultAsync(account =>
                    account.CorrespondentId == entity.CorrespondentId &&
                    (!accountIdToExclude.HasValue || account.Id != accountIdToExclude.Value));
                if (existingAccount != null)
                {
                    var archiveHint = existingAccount.IsArchived
                        ? " حساب قبلی در بایگانی است؛ آن را از بخش بایگانی‌ها دوباره فعال کنید."
                        : string.Empty;
                    throw new InvalidOperationException($"این نمایندگی قبلاً حساب «{existingAccount.AccountName}» دارد.{archiveHint}");
                }
            }
        }

        private static SqlException? FindSqlException(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
                if (current is SqlException sqlException)
                    return sqlException;

            return null;
        }

        private void DetachAddedEntities()
        {
            foreach (var entry in _context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
                entry.State = EntityState.Detached;
        }

        // متد تولید کد خودکار بر اساس نوع حساب
        private async Task<string> GenerateAccountCodeAsync(string accountType)
        {
            string prefix = accountType?.ToLower() switch
            {
                "cash" or "bank" => "1",
                "correspondent" => "2",
                "income" => "3",
                "expense" => "4",
                "equity" => "5",
                "customer" => "6",
                _ => "9"
            };

            var lastAccount = await _dbSet
                .Where(a => a.AccountCode.StartsWith(prefix))
                .OrderByDescending(a => a.AccountCode)
                .FirstOrDefaultAsync();

            int nextNumber;
            if (lastAccount == null)
            {
                nextNumber = int.Parse(prefix + "000");
            }
            else
            {
                if (int.TryParse(lastAccount.AccountCode, out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
                else
                {
                    nextNumber = int.Parse(prefix + "000");
                }
            }

            // حلقه برای جلوگیری از تکراری بودن
            string newCode;
            while (true)
            {
                newCode = nextNumber.ToString();
                var exists = await _dbSet.AnyAsync(a => a.AccountCode == newCode);
                if (!exists)
                    break;
                nextNumber++;
            }

            return newCode;
        }

        // متد جدید برای دریافت کد پیشنهادی (بدون ذخیره‌سازی) – جهت نمایش در UI
        public async Task<string> GetNextAccountCodeAsync(string accountType)
        {
            // از همان منطق GenerateAccountCodeAsync استفاده می‌کنیم
            return await GenerateAccountCodeAsync(accountType);
        }
        private async Task<string> GenerateOpeningTransactionNumberAsync()
        {
            var datePart = DateTime.Now.ToString("yyyyMMdd");
            var lastTransaction = await _context.Transactions
                .Where(t => t.TransactionNo.StartsWith($"OP-{datePart}"))
                .OrderByDescending(t => t.TransactionNo)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastTransaction != null)
            {
                var parts = lastTransaction.TransactionNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
            }

            return $"OP-{datePart}-{nextNumber:D4}";
        }

        
    }
}
