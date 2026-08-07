using AutoMapper;
using Azure.Core;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
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
            // ۱. تولید کد اگر خالی باشد
            if (string.IsNullOrWhiteSpace(createDto.AccountCode))
            {
                createDto.AccountCode = await GenerateAccountCodeAsync(createDto.AccountType);
            }

            // ۲. ایجاد یک حساب (فقط یک بار)
            var entity = _mapper.Map<Account>(createDto);
            _context.PrepareTenantEntity(entity);
            entity.CreatedAt = DateTime.UtcNow;

            await _dbSet.AddAsync(entity); // ✅ فقط یک بار
            await _context.SaveChangesAsync();

            // ۳. ثبت موجودی‌های اولیه (همگی به همین حساب متصل می‌شوند)
            if (createDto.HasInitialBalance && createDto.InitialBalances != null && createDto.InitialBalances.Any())
            {
                // ایجاد یک تراکنش از نوع OpeningBalance
                var openingTransaction = new Transaction
                {
                    TransactionNo = await GenerateOpeningTransactionNumberAsync(),
                    TransactionType = "OpeningBalance",
                    BranchId = await _context.GetDefaultBranchIdAsync(),
                    Status = "Paid",
                    Remarks = $"موجودی اولیه برای حساب {entity.AccountName} (کد: {entity.AccountCode})",
                    CreatedBy = _context.RequireCurrentUserId(),
                    CreatedAt = DateTime.UtcNow
                };

                await _context.Transactions.AddAsync(openingTransaction);
                await _context.SaveChangesAsync();

                // ثبت هر موجودی به‌عنوان یک LedgerEntry
                foreach (var initialBalance in createDto.InitialBalances)
                {
                    decimal talabKar = 0, badehKar = 0;
                    if (initialBalance.Direction == "Debit")
                        badehKar = initialBalance.Amount;
                    else if (initialBalance.Direction == "Credit")
                        talabKar = initialBalance.Amount;

                    var ledgerEntryDto = new CreateLedgerEntryDto
                    {
                        TransactionId = openingTransaction.Id,
                        AccountId = entity.Id, // ✅ همه به همین حساب متصل می‌شوند
                        CurrencyId = initialBalance.CurrencyId,
                        TalabKar = talabKar,
                        BadehKar = badehKar,
                        Description = $"موجودی اولیه: {initialBalance.Description ?? "بدون توضیح"}"
                    };

                    await _ledgerService.CreateLedgerEntryAsync(ledgerEntryDto);
                }

                // ثبت در AuditLog
                await _auditLogService.LogAsync("CREATE", "Transactions", openingTransaction.Id, null,
                    $"تراکنش موجودی اولیه برای حساب {entity.AccountName} ایجاد شد", _context.RequireCurrentUserId());
            }

            // ثبت در AuditLog برای حساب
            await _auditLogService.LogAsync("CREATE", "Accounts", entity.Id, null,
                $"حساب {entity.AccountName} با کد {entity.AccountCode} ایجاد شد", _context.RequireCurrentUserId());

            return _mapper.Map<AccountDto>(entity);
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
