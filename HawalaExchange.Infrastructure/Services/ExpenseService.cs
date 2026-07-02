using AutoMapper;
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
        private readonly ILedgerService _ledgerService;
        private readonly IMapper _mapper;

        public ExpenseService(ApplicationDbContext context, ILedgerService ledgerService, IMapper mapper)
        {
            _context = context;
            _ledgerService = ledgerService;
            _mapper = mapper;
        }

        // ===== متدهای جدید =====
        public async Task<IEnumerable<ExpenseDto>> GetAllAsync()
        {
            var expenses = await _context.Expenses
                .Include(e => e.Currency)
                .OrderByDescending(e => e.Id)
                .ToListAsync();

            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }

        public async Task<ExpenseDto?> GetByIdAsync(long id)
        {
            var expense = await _context.Expenses
                .Include(e => e.Currency)
                .FirstOrDefaultAsync(e => e.Id == id);

            return expense == null ? null : _mapper.Map<ExpenseDto>(expense);
        }
        public async Task<ExpenseDto> CreateExpenseAsync(CreateExpenseDto createDto)
        {
            // اعتبارسنجی اولیه
            if (createDto.TransactionId == 0)
                throw new InvalidOperationException("شناسه تراکنش نمی‌تواند صفر باشد. لطفاً یک تراکنش معتبر انتخاب کنید.");

            if (createDto.Amount <= 0)
                throw new InvalidOperationException("مبلغ باید بزرگتر از صفر باشد.");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. ثبت هزینه
                var expense = _mapper.Map<Expense>(createDto);
                expense.ExpenseDate = expense.ExpenseDate == DateTime.MinValue ? DateTime.UtcNow : expense.ExpenseDate;

                await _context.Expenses.AddAsync(expense);
                await _context.SaveChangesAsync();

                // 2. دریافت یا ایجاد حساب هزینه بر اساس کد یکتا
                var expenseAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountCode == "4001" && a.AccountType == "Expense");

                if (expenseAccount == null)
                {
                    expenseAccount = new Account
                    {
                        AccountCode = "4001",
                        AccountName = "General Expenses",
                        AccountType = "Expense",
                        IsArchived = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _context.Accounts.AddAsync(expenseAccount);
                    await _context.SaveChangesAsync();
                }

                // 3. ثبت ورودی دفتر کل
                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                {
                    TransactionId = createDto.TransactionId,
                    AccountId = expenseAccount.Id,
                    CurrencyId = createDto.CurrencyId,
                    TalabKar = 0,
                    BadehKar = createDto.Amount,
                    Description = $"Expense: {createDto.Title}"
                });

                await transaction.CommitAsync();

                return _mapper.Map<ExpenseDto>(expense);
            }
            catch (DbUpdateException dbEx)
            {
                await transaction.RollbackAsync();
                var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;
                throw new InvalidOperationException($"خطای دیتابیس: {innerMessage}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"خطا: {ex.Message}");
            }
        }

        public async Task<ExpenseDto> UpdateExpenseAsync(long id, CreateExpenseDto updateDto)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) throw new KeyNotFoundException($"Expense with ID {id} not found.");
            _mapper.Map(updateDto, expense);
            await _context.SaveChangesAsync();
            return _mapper.Map<ExpenseDto>(expense);
        }

        public async Task DeleteExpenseAsync(long id)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) throw new KeyNotFoundException($"Expense with ID {id} not found.");
            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();
        }

        // ===== متدهای کوئری خاص =====
        public async Task<IEnumerable<ExpenseDto>> GetExpensesByTransactionAsync(long transactionId)
        {
            var expenses = await _context.Expenses
                .Where(e => e.TransactionId == transactionId)
                .Include(e => e.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }

        public async Task<IEnumerable<ExpenseDto>> GetExpensesByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            var expenses = await _context.Expenses
                .Where(e => e.ExpenseDate >= fromDate && e.ExpenseDate <= toDate)
                .Include(e => e.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }

        public async Task<IEnumerable<ExpenseDto>> GetExpensesByCurrencyAsync(long currencyId)
        {
            var expenses = await _context.Expenses
                .Where(e => e.CurrencyId == currencyId)
                .Include(e => e.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }

        public async Task<decimal> GetTotalExpensesByCurrencyAsync(long currencyId, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.Expenses.Where(e => e.CurrencyId == currencyId);
            if (fromDate.HasValue) query = query.Where(e => e.ExpenseDate >= fromDate.Value);
            if (toDate.HasValue) query = query.Where(e => e.ExpenseDate <= toDate.Value);
            return await query.SumAsync(e => e.Amount);
        }

        public async Task<IEnumerable<ExpenseDto>> GetExpensesByBranchAsync(long branchId)
        {
            var expenses = await _context.Expenses
                .Include(e => e.Transaction)
                .Where(e => e.Transaction != null && e.Transaction.BranchId == branchId)
                .Include(e => e.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }
    }
}