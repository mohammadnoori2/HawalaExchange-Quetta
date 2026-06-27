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

        public async Task<ExpenseDto> CreateExpenseAsync(CreateExpenseDto createDto)
        {
            var expense = _mapper.Map<Expense>(createDto);
            expense.ExpenseDate = expense.ExpenseDate == DateTime.MinValue ? DateTime.UtcNow : expense.ExpenseDate;

            await _context.Expenses.AddAsync(expense);
            await _context.SaveChangesAsync();

            var expenseAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.AccountType == "Expense" && a.AccountName == "General Expenses");

            if (expenseAccount == null)
            {
                expenseAccount = new Account
                {
                    AccountCode = "4001",
                    AccountName = "General Expenses",
                    AccountType = "Expense",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.Accounts.AddAsync(expenseAccount);
                await _context.SaveChangesAsync();
            }

            await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
            {
                AccountId = expenseAccount.Id,
                CurrencyId = createDto.CurrencyId,
                TalabKar = 0,
                BadehKar = createDto.Amount,
                Description = $"Expense: {createDto.Title}"
            });

            return _mapper.Map<ExpenseDto>(expense);
        }

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

        public async Task<ExpenseDto?> GetExpenseByIdAsync(long id)
        {
            var expense = await _context.Expenses
                .Include(e => e.Currency)
                .FirstOrDefaultAsync(e => e.Id == id);
            return expense == null ? null : _mapper.Map<ExpenseDto>(expense);
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