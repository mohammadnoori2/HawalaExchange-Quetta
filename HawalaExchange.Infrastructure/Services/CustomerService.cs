using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.Application.Services
{
    public class CustomerService : BaseService<Customer, CustomerDto, CreateCustomerDto, UpdateCustomerDto>, ICustomerService
    {
        private readonly IAccountService _accountService;
        private readonly ILedgerService _ledgerService;
        private readonly IAuditLogService _auditLogService;
        private readonly ILogger<CustomerService> _logger;

        public CustomerService(
            ApplicationDbContext context,
            IMapper mapper,
            IAccountService accountService,
            ILedgerService ledgerService,
            IAuditLogService auditLogService,
            ILogger<CustomerService> logger)
            : base(context, mapper)
        {
            _accountService = accountService;
            _ledgerService = ledgerService;
            _auditLogService = auditLogService;
            _logger = logger;
        }

        // ===== متدهای موجود =====

        public async Task<CustomerDto?> GetByCustomerCodeAsync(string customerCode)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(c => c.CustomerCode == customerCode);
            return entity == null ? null : _mapper.Map<CustomerDto>(entity);
        }

        public async Task<IEnumerable<CustomerDto>> SearchAsync(string searchTerm)
        {
            var term = searchTerm.ToLower();
            var customers = await _dbSet
                .Where(c =>
                    c.FullName.ToLower().Contains(term) ||
                    (c.PhoneNumber != null && c.PhoneNumber.Contains(term)) ||
                    (c.TazkiraNumber != null && c.TazkiraNumber.Contains(term)) ||
                    c.CustomerCode.ToLower().Contains(term))
                .ToListAsync();
            return _mapper.Map<IEnumerable<CustomerDto>>(customers);
        }

        public async Task<IEnumerable<CustomerDto>> GetArchivedAsync()
        {
            var entities = await _dbSet.Where(c => c.IsArchived).ToListAsync();
            return _mapper.Map<IEnumerable<CustomerDto>>(entities);
        }

        public async Task<CustomerDto> ArchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Customer with ID {id} not found.");

            entity.IsArchived = true;
            await _context.SaveChangesAsync();

            // ثبت لاگ بایگانی
            await _auditLogService.LogAsync(
                action: "ARCHIVE",
                tableName: "Customers",
                recordId: id,
                oldValue: null,
                newValue: $"مشتری {entity.FullName} بایگانی شد"
            );

            return _mapper.Map<CustomerDto>(entity);
        }

        public async Task<CustomerDto> UnarchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Customer with ID {id} not found.");

            entity.IsArchived = false;
            await _context.SaveChangesAsync();

            // ثبت لاگ خروج از بایگانی
            await _auditLogService.LogAsync(
                action: "UNARCHIVE",
                tableName: "Customers",
                recordId: id,
                oldValue: null,
                newValue: $"مشتری {entity.FullName} از بایگانی خارج شد"
            );

            return _mapper.Map<CustomerDto>(entity);
        }

        // ===== متدهای جدید و بازنویسی‌شده =====

        protected override async Task ValidateCreateAsync(Customer entity, CreateCustomerDto dto)
        {
            // تولید کد مشتری
            entity.CustomerCode = await GenerateCustomerCodeAsync();
        }

        public override async Task<CustomerDto> CreateAsync(CreateCustomerDto createDto)
        {
            // استفاده از تراکنش برای اطمینان از یکپارچگی داده‌ها
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // ۱. ایجاد مشتری با استفاده از متد پایه
                var customerDto = await base.CreateAsync(createDto);

                // ۲. ایجاد حساب مرتبط با مشتری
                var accountDto = await CreateCustomerAccountAsync(customerDto.Id, customerDto.FullName);

                // ۳. ثبت موجودی اولیه (در صورت وجود)
                if (createDto.HasInitialBalance && createDto.InitialBalances != null && createDto.InitialBalances.Any())
                {
                    await CreateInitialBalancesAsync(
                        accountDto.Id,
                        createDto.InitialBalances,
                        customerDto.FullName,
                        customerDto.CustomerCode
                    );
                }

                // ۴. ثبت لاگ ایجاد مشتری (قبلاً در base.CreateAsync لاگ نشده، پس خودمان اضافه می‌کنیم)
                await _auditLogService.LogAsync(
                    action: "CREATE",
                    tableName: "Customers",
                    recordId: customerDto.Id,
                    oldValue: null,
                    newValue: $"مشتری {customerDto.FullName} با کد {customerDto.CustomerCode} ایجاد شد"
                );

                // تأیید تراکنش
                await transaction.CommitAsync();

                return customerDto;
            }
            catch (Exception ex)
            {
                // برگرداندن تراکنش در صورت بروز خطا
                await transaction.RollbackAsync();
                _logger.LogError(ex, "خطا در ایجاد مشتری و حساب مرتبط");
                throw;
            }
        }

        // ===== متدهای کمکی خصوصی =====

        private async Task<string> GenerateCustomerCodeAsync()
        {
            // کد مشتری به صورت CUST-YYYYMMDD-XXXX
            var datePart = DateTime.Now.ToString("yyyyMMdd");
            var lastCustomer = await _dbSet
                .Where(c => c.CustomerCode.StartsWith($"CUST-{datePart}"))
                .OrderByDescending(c => c.CustomerCode)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastCustomer != null)
            {
                var parts = lastCustomer.CustomerCode.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
            }

            return $"CUST-{datePart}-{nextNumber:D4}";
        }

        private async Task<AccountDto> CreateCustomerAccountAsync(long customerId, string customerName)
        {
            var createAccountDto = new CreateAccountDto
            {
                AccountType = "Customer",
                AccountName = $"مشتری: {customerName}",
                ReferenceType = "Customer",
                ReferenceId = customerId,
                HasInitialBalance = false, // موجودی‌ها جدا ثبت می‌شوند
                InitialBalances = null
            };

            var accountDto = await _accountService.CreateAsync(createAccountDto);

            // ثبت لاگ ایجاد حساب
            await _auditLogService.LogAsync(
                action: "CREATE",
                tableName: "Accounts",
                recordId: accountDto.Id,
                oldValue: null,
                newValue: $"حساب مرتبط با مشتری {customerName} (کد: {accountDto.AccountCode}) ایجاد شد"
            );

            return accountDto;
        }

        private async Task CreateInitialBalancesAsync(
    long accountId,                  // حساب مشتری
    List<InitialBalanceDto> initialBalances,
    string customerName,
    string customerCode)
        {
            // ایجاد یک تراکنش از نوع OpeningBalance
            var openingTransaction = new Transaction
            {
                TransactionNo = await GenerateOpeningTransactionNumberAsync(),
                TransactionType = "OpeningBalance",
                BranchId = 1,
                Status = "Paid",
                Remarks = $"موجودی اولیه برای مشتری {customerName} (کد: {customerCode})",
                CreatedBy = GetCurrentUserId(),
                CreatedAt = DateTime.UtcNow
            };

            await _context.Transactions.AddAsync(openingTransaction);
            await _context.SaveChangesAsync();

            foreach (var initialBalance in initialBalances)
            {
                // اعتبارسنجی: حساب طرف مقابل باید وجود داشته باشد
                var oppositeAccount = await _context.Accounts.FindAsync(initialBalance.OppositeAccountId);
                if (oppositeAccount == null)
                    throw new InvalidOperationException($"حساب طرف مقابل با شناسه {initialBalance.OppositeAccountId} یافت نشد");

                // ✅ اصلاح: تعیین جهت‌ها بر اساس انتخاب کاربر
                // اگر کاربر "بدهکار" انتخاب کند → مشتری بدهکار (TalabKar)
                // اگر کاربر "بستانکار" انتخاب کند → مشتری بستانکار (BadehKar)
                decimal customerTalabKar = 0, customerBadehKar = 0;
                decimal oppositeTalabKar = 0, oppositeBadehKar = 0;

                if (initialBalance.Direction == "Debit") // بدهکار
                {
                    // مشتری بدهکار است → حساب مشتری بدهکار، حساب طرف مقابل بستانکار

                    customerTalabKar = 0;
                    customerBadehKar = initialBalance.Amount;
                    oppositeTalabKar = initialBalance.Amount;
                    oppositeBadehKar = 0;
                }
                else // Credit (بستانکار)
                {
                    // مشتری بستانکار است → حساب مشتری بستانکار، حساب طرف مقابل بدهکار


                    customerTalabKar = initialBalance.Amount;
                    customerBadehKar = 0;
                    oppositeTalabKar = 0;
                    oppositeBadehKar = initialBalance.Amount;
                }

                // ثبت ورودی برای حساب مشتری
                var customerLedgerEntry = new CreateLedgerEntryDto
                {
                    TransactionId = openingTransaction.Id,
                    AccountId = accountId,
                    CurrencyId = initialBalance.CurrencyId,
                    TalabKar = customerTalabKar,    // ✅ بدهکار
                    BadehKar = customerBadehKar,    // ✅ بستانکار
                    Description = $"موجودی اولیه مشتری: {initialBalance.Description ?? "بدون توضیح"}"
                };
                await _ledgerService.CreateLedgerEntryAsync(customerLedgerEntry);

                // ثبت ورودی برای حساب طرف مقابل (برعکس)
                var oppositeLedgerEntry = new CreateLedgerEntryDto
                {
                    TransactionId = openingTransaction.Id,
                    AccountId = initialBalance.OppositeAccountId,
                    CurrencyId = initialBalance.CurrencyId,
                    TalabKar = oppositeTalabKar,
                    BadehKar = oppositeBadehKar,
                    Description = $"طرف مقابل موجودی اولیه مشتری {customerName}"
                };
                await _ledgerService.CreateLedgerEntryAsync(oppositeLedgerEntry);
            }

            // ثبت لاگ
            await _auditLogService.LogAsync(
                action: "CREATE",
                tableName: "Transactions",
                recordId: openingTransaction.Id,
                oldValue: null,
                newValue: $"تراکنش موجودی اولیه برای مشتری {customerName} با {initialBalances.Count} رکورد ایجاد شد"
            );
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

        private long GetCurrentUserId()
        {
            // در اینجا می‌توانید از Claim یا سرویس کاربر جاری استفاده کنید
            // برای نمونه، مقدار ۱ را برمی‌گردانیم (در پروژه واقعی باید از HttpContext یا IUserService دریافت کنید)
            return 1;
        }
    }
}