using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.Application.Services
{
    public partial class CustomerService : BaseService<Customer, CustomerDto, CreateCustomerDto, UpdateCustomerDto>, ICustomerService
    {
        private readonly IAccountService _accountService;
        private readonly ILedgerService _ledgerService;
        private readonly IAuditLogService _auditLogService;
        private readonly IAccountBadehkarLimitService _debtLimitService;
        private readonly ILogger<CustomerService> _logger;

        public CustomerService(
            ApplicationDbContext context,
            IMapper mapper,
            IAccountService accountService,
            ILedgerService ledgerService,
            IAuditLogService auditLogService,
            IAccountBadehkarLimitService debtLimitService,
            ILogger<CustomerService> logger)
            : base(context, mapper)
        {
            _accountService = accountService;
            _ledgerService = ledgerService;
            _auditLogService = auditLogService;
            _debtLimitService = debtLimitService;
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

                AccountDto? accountDto = null;
                if (createDto.OpenAccount)
                {
                    accountDto = await CreateCustomerAccountAsync(customerDto.Id, customerDto.FullName);
                }

                var debtLimits = createDto.DebtLimits
                    .Where(limit => limit.IsEnabled && limit.CurrencyId > 0)
                    .ToList();

                if (debtLimits.Count > 0)
                {
                    if (accountDto == null)
                        throw new InvalidOperationException("برای تعیین سقف بدهکاری، گزینه افتتاح حساب را انتخاب کنید.");
                    await _debtLimitService.SetForAccountAsync(accountDto.Id, debtLimits);
                }

                // ثبت موجودی افتتاحیه در حساب مقابل افتتاحیه
                if (createDto.HasInitialBalance)
                {
                    if (accountDto == null)
                        throw new InvalidOperationException("برای ثبت موجودی افتتاحیه، گزینه افتتاح حساب را انتخاب کنید.");

                    if (createDto.InitialBalances is not { Count: > 0 })
                        throw new InvalidOperationException("حداقل یک موجودی افتتاحیه وارد کنید.");

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
                _context.ChangeTracker.Clear();
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

        private async Task CreateInitialBalancesAsync(long accountId, List<InitialBalanceDto> initialBalances,
            string customerName, string customerCode)
        {
            var account = await _context.Accounts.SingleAsync(x => x.Id == accountId && x.CustomerId != null && !x.IsArchived);
            if (initialBalances.GroupBy(x => x.CurrencyId).Any(x => x.Count() > 1))
                throw new InvalidOperationException("برای هر ارز فقط یک موجودی افتتاحیه ثبت کنید.");
            foreach (var balance in initialBalances)
            {
                var request = new CustomerOpeningBalanceRequestDto { CurrencyId = balance.CurrencyId,
                    Amount = balance.Amount, Direction = balance.Direction, Description = balance.Description };
                ValidateOpening(request);
                await EnsureOpeningCurrencyAvailableAsync(account.Id, request.CurrencyId);
                await PostOpeningAsync(account, request, await OpeningPostingDateAsync(account.CustomerId!.Value, request.OpeningDate));
            }
        }

    }
}
