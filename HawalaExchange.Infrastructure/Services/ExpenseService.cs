using AutoMapper;
using AutoMapper.QueryableExtensions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class ExpenseService : IExpenseService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public ExpenseService(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<ExpenseDto>> GetAllAsync()
        {
            return await _context.Expenses
                .AsNoTracking()
                .Include(e => e.Currency)
                .Include(e => e.ExpenseAccount)
                .Include(e => e.PaidFromAccount)
                .Where(e => !e.IsDeleted)
                .OrderByDescending(e => e.ExpenseDate)
                .ProjectTo<ExpenseDto>(_mapper.ConfigurationProvider)
                .ToListAsync();
        }

        public async Task<ExpenseDto?> GetByIdAsync(long id)
        {
            return await _context.Expenses
                .AsNoTracking()
                .Include(e => e.Currency)
                .Include(e => e.ExpenseAccount)
                .Include(e => e.PaidFromAccount)
                .Where(e => e.Id == id && !e.IsDeleted)
                .ProjectTo<ExpenseDto>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync();
        }

        public async Task<ExpenseDto> CreateExpenseAsync(CreateExpenseDto createDto)
        {
            ValidateCreateDto(createDto);

            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await ValidateAccountsAsync(
                    createDto.ExpenseAccountId,
                    createDto.PaidFromAccountId,
                    createDto.CurrencyId);

                var expense = _mapper.Map<Expense>(createDto);

                expense.Title = createDto.Title.Trim();
                expense.Description = string.IsNullOrWhiteSpace(createDto.Description)
                    ? createDto.Title.Trim()
                    : createDto.Description.Trim();

                expense.ExpenseDate = createDto.ExpenseDate == DateTime.MinValue
                    ? DateTime.UtcNow
                    : createDto.ExpenseDate;

                expense.CreatedAt = DateTime.UtcNow;
                expense.CreatedBy = GetCurrentUserId();
                expense.IsDeleted = false;

                await _context.Expenses.AddAsync(expense);
                await _context.SaveChangesAsync();

                await CreateLedgerEntriesAsync(expense);

                await _context.SaveChangesAsync();

                await dbTransaction.CommitAsync();

                var result = await GetByIdAsync(expense.Id);

                if (result == null)
                    throw new InvalidOperationException("مصرف ثبت شد، اما در بارگذاری دوباره یافت نشد.");

                return result;
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        public async Task<ExpenseDto> UpdateExpenseAsync(long id, UpdateExpenseDto updateDto)
        {
            ValidateUpdateDto(updateDto);

            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var expense = await _context.Expenses
                    .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

                if (expense == null)
                    throw new KeyNotFoundException($"مصرف با شناسه {id} یافت نشد.");

                await ValidateAccountsAsync(
                    updateDto.ExpenseAccountId,
                    updateDto.PaidFromAccountId,
                    updateDto.CurrencyId);

                await DeleteLedgerEntriesAsync(expense.Id);

                _mapper.Map(updateDto, expense);

                expense.Title = updateDto.Title.Trim();
                expense.Description = string.IsNullOrWhiteSpace(updateDto.Description)
                    ? updateDto.Title.Trim()
                    : updateDto.Description.Trim();

                expense.ExpenseDate = updateDto.ExpenseDate == DateTime.MinValue
                    ? DateTime.UtcNow
                    : updateDto.ExpenseDate;

                expense.ModifiedAt = DateTime.UtcNow;
                expense.ModifiedBy = GetCurrentUserId();

                await CreateLedgerEntriesAsync(expense);

                await _context.SaveChangesAsync();

                await dbTransaction.CommitAsync();

                var result = await GetByIdAsync(expense.Id);

                if (result == null)
                    throw new InvalidOperationException("مصرف ویرایش شد، اما در بارگذاری دوباره یافت نشد.");

                return result;
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        public async Task DeleteExpenseAsync(long id)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var expense = await _context.Expenses
                    .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

                if (expense == null)
                    throw new KeyNotFoundException($"مصرف با شناسه {id} یافت نشد.");

                await DeleteLedgerEntriesAsync(expense.Id);

                expense.IsDeleted = true;
                expense.ModifiedAt = DateTime.UtcNow;
                expense.ModifiedBy = GetCurrentUserId();

                await _context.SaveChangesAsync();

                await dbTransaction.CommitAsync();
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<ExpenseDto>> GetExpensesByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            return await _context.Expenses
                .AsNoTracking()
                .Include(e => e.Currency)
                .Include(e => e.ExpenseAccount)
                .Include(e => e.PaidFromAccount)
                .Where(e =>
                    !e.IsDeleted &&
                    e.ExpenseDate >= fromDate &&
                    e.ExpenseDate <= toDate)
                .OrderByDescending(e => e.ExpenseDate)
                .ProjectTo<ExpenseDto>(_mapper.ConfigurationProvider)
                .ToListAsync();
        }

        public async Task<IEnumerable<ExpenseDto>> GetExpensesByCurrencyAsync(long currencyId)
        {
            return await _context.Expenses
                .AsNoTracking()
                .Include(e => e.Currency)
                .Include(e => e.ExpenseAccount)
                .Include(e => e.PaidFromAccount)
                .Where(e => !e.IsDeleted && e.CurrencyId == currencyId)
                .OrderByDescending(e => e.ExpenseDate)
                .ProjectTo<ExpenseDto>(_mapper.ConfigurationProvider)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalExpensesByCurrencyAsync(
            long currencyId,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var query = _context.Expenses
                .Where(e => !e.IsDeleted && e.CurrencyId == currencyId);

            if (fromDate.HasValue)
                query = query.Where(e => e.ExpenseDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(e => e.ExpenseDate <= toDate.Value);

            return await query.SumAsync(e => e.Amount);
        }

        private async Task CreateLedgerEntriesAsync(Expense expense)
        {
            var description = string.IsNullOrWhiteSpace(expense.Description)
                ? $"ثبت مصرف {expense.Title}"
                : expense.Description;

            var ledgerEntries = new List<LedgerEntry>
            {
                new LedgerEntry
                {
                    ExpenseId = expense.Id,
                    CapitalInvestmentId = null,
                    HawalaId = null,
                    TransactionId = null,

                    AccountId = expense.ExpenseAccountId,
                    CurrencyId = expense.CurrencyId,

                    TalabKar = 0,
                    BadehKar = expense.Amount,

                    Description = $"{description} با شماره {expense.Id}",
                    CreatedAt = expense.ExpenseDate
                },
                new LedgerEntry
                {
                    ExpenseId = expense.Id,
                    CapitalInvestmentId = null,
                    HawalaId = null,
                    TransactionId = null,

                    AccountId = expense.PaidFromAccountId,
                    CurrencyId = expense.CurrencyId,

                    TalabKar = expense.Amount,
                    BadehKar = 0,

                    Description = $"{description} با شماره {expense.Id}",
                    CreatedAt = expense.ExpenseDate
                }
            };

            await _context.LedgerEntries.AddRangeAsync(ledgerEntries);
        }

        private async Task DeleteLedgerEntriesAsync(long expenseId)
        {
            var ledgerEntries = await _context.LedgerEntries
                .Where(x => x.ExpenseId == expenseId)
                .ToListAsync();

            if (ledgerEntries.Any())
            {
                _context.LedgerEntries.RemoveRange(ledgerEntries);
            }
        }

        private async Task ValidateAccountsAsync(
            long expenseAccountId,
            long paidFromAccountId,
            long currencyId)
        {
            var currencyExists = await _context.Currencies
                .AnyAsync(x => x.Id == currencyId && x.IsActive);

            if (!currencyExists)
                throw new InvalidOperationException("ارز انتخاب‌شده معتبر نیست.");

            var expenseAccount = await _context.Accounts
                .FirstOrDefaultAsync(x => x.Id == expenseAccountId && !x.IsArchived);

            if (expenseAccount == null)
                throw new InvalidOperationException("حساب مصرف معتبر نیست.");

            if (expenseAccount.AccountType != "Expense")
                throw new InvalidOperationException("حساب مصرف باید از نوع Expense باشد.");

            var paidFromAccount = await _context.Accounts
                .FirstOrDefaultAsync(x => x.Id == paidFromAccountId && !x.IsArchived);

            if (paidFromAccount == null)
                throw new InvalidOperationException("حساب پرداخت‌کننده معتبر نیست.");

            if (paidFromAccount.AccountType != "Cash" &&
                paidFromAccount.AccountType != "Bank")
            {
                throw new InvalidOperationException("حساب پرداخت‌کننده باید از نوع Cash یا Bank باشد.");
            }
        }

        private static void ValidateCreateDto(CreateExpenseDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new InvalidOperationException("عنوان مصرف الزامی است.");

            if (dto.CurrencyId <= 0)
                throw new InvalidOperationException("انتخاب ارز الزامی است.");

            if (dto.Amount <= 0)
                throw new InvalidOperationException("مبلغ مصرف باید بزرگتر از صفر باشد.");

            if (dto.ExpenseAccountId <= 0)
                throw new InvalidOperationException("انتخاب حساب مصرف الزامی است.");

            if (dto.PaidFromAccountId <= 0)
                throw new InvalidOperationException("انتخاب حساب پرداخت‌کننده الزامی است.");

            if (dto.ExpenseAccountId == dto.PaidFromAccountId)
                throw new InvalidOperationException("حساب مصرف و حساب پرداخت‌کننده نمی‌تواند یکی باشد.");
        }

        private static void ValidateUpdateDto(UpdateExpenseDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new InvalidOperationException("عنوان مصرف الزامی است.");

            if (dto.CurrencyId <= 0)
                throw new InvalidOperationException("انتخاب ارز الزامی است.");

            if (dto.Amount <= 0)
                throw new InvalidOperationException("مبلغ مصرف باید بزرگتر از صفر باشد.");

            if (dto.ExpenseAccountId <= 0)
                throw new InvalidOperationException("انتخاب حساب مصرف الزامی است.");

            if (dto.PaidFromAccountId <= 0)
                throw new InvalidOperationException("انتخاب حساب پرداخت‌کننده الزامی است.");

            if (dto.ExpenseAccountId == dto.PaidFromAccountId)
                throw new InvalidOperationException("حساب مصرف و حساب پرداخت‌کننده نمی‌تواند یکی باشد.");
        }

        private long GetCurrentUserId() => 1;
    }
}