using AutoMapper;
using AutoMapper.QueryableExtensions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class AccountMoneyOperationService : IAccountMoneyOperationService
{
    private readonly ApplicationDbContext _context;
    private readonly IMapper _mapper;

    public AccountMoneyOperationService(
        ApplicationDbContext context,
        IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<IEnumerable<AccountMoneyOperationDto>> GetAllAsync()
    {
        return await _context.AccountMoneyOperations
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.CashOrBankAccount)
            .Include(x => x.Currency)
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.OperationDate)
            .ProjectTo<AccountMoneyOperationDto>(_mapper.ConfigurationProvider)
            .ToListAsync();
    }

    public async Task<AccountMoneyOperationDto?> GetByIdAsync(long id)
    {
        return await _context.AccountMoneyOperations
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.CashOrBankAccount)
            .Include(x => x.Currency)
            .Where(x => x.Id == id && !x.IsDeleted)
            .ProjectTo<AccountMoneyOperationDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
    }

    public async Task<AccountMoneyOperationDto> CreateAsync(CreateAccountMoneyOperationDto dto)
    {
        ValidateCreateDto(dto);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await ValidateAccountsAsync(
                dto.AccountId,
                dto.CashOrBankAccountId,
                dto.CurrencyId);

            var operation = _mapper.Map<AccountMoneyOperation>(dto);

            operation.OperationDate = dto.OperationDate == DateTime.MinValue
                ? DateTime.UtcNow
                : dto.OperationDate;

            operation.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? GetDefaultDescription(dto.OperationType)
                : dto.Description.Trim();

            operation.CreatedAt = DateTime.UtcNow;
            operation.CreatedBy = GetCurrentUserId();
            operation.IsDeleted = false;

            await _context.AccountMoneyOperations.AddAsync(operation);
            await _context.SaveChangesAsync();

            await CreateLedgerEntriesAsync(operation);

            await _context.SaveChangesAsync();

            await dbTransaction.CommitAsync();

            var result = await GetByIdAsync(operation.Id);

            if (result == null)
                throw new InvalidOperationException("عملیات ثبت شد، اما در بارگذاری دوباره یافت نشد.");

            return result;
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    public async Task<AccountMoneyOperationDto> UpdateAsync(long id, UpdateAccountMoneyOperationDto dto)
    {
        ValidateUpdateDto(dto);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var operation = await _context.AccountMoneyOperations
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (operation == null)
                throw new KeyNotFoundException($"عملیات با شناسه {id} یافت نشد.");

            await ValidateAccountsAsync(
                dto.AccountId,
                dto.CashOrBankAccountId,
                dto.CurrencyId);

            await DeleteLedgerEntriesAsync(operation.Id);

            _mapper.Map(dto, operation);

            operation.OperationDate = dto.OperationDate == DateTime.MinValue
                ? DateTime.UtcNow
                : dto.OperationDate;

            operation.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? GetDefaultDescription(dto.OperationType)
                : dto.Description.Trim();

            operation.ModifiedAt = DateTime.UtcNow;
            operation.ModifiedBy = GetCurrentUserId();

            await CreateLedgerEntriesAsync(operation);

            await _context.SaveChangesAsync();

            await dbTransaction.CommitAsync();

            var result = await GetByIdAsync(operation.Id);

            if (result == null)
                throw new InvalidOperationException("عملیات ویرایش شد، اما در بارگذاری دوباره یافت نشد.");

            return result;
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAsync(long id)
    {
        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var operation = await _context.AccountMoneyOperations
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (operation == null)
                throw new KeyNotFoundException($"عملیات با شناسه {id} یافت نشد.");

            await DeleteLedgerEntriesAsync(operation.Id);

            operation.IsDeleted = true;
            operation.ModifiedAt = DateTime.UtcNow;
            operation.ModifiedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    private async Task CreateLedgerEntriesAsync(AccountMoneyOperation operation)
    {
        var description = $"{operation.Description} با شماره {operation.Id}";

        var ledgerEntries = new List<LedgerEntry>();

        if (operation.OperationType == "Deposit")
        {
            ledgerEntries.Add(new LedgerEntry
            {
                AccountMoneyOperationId = operation.Id,
                AccountId = operation.CashOrBankAccountId,
                CurrencyId = operation.CurrencyId,
                TalabKar = 0,
                BadehKar = operation.Amount,
                Description = description,
                CreatedAt = operation.OperationDate
            });

            ledgerEntries.Add(new LedgerEntry
            {
                AccountMoneyOperationId = operation.Id,
                AccountId = operation.AccountId,
                CurrencyId = operation.CurrencyId,
                TalabKar = operation.Amount,
                BadehKar = 0,
                Description = description,
                CreatedAt = operation.OperationDate
            });
        }
        else if (operation.OperationType == "Withdraw")
        {
            ledgerEntries.Add(new LedgerEntry
            {
                AccountMoneyOperationId = operation.Id,
                AccountId = operation.AccountId,
                CurrencyId = operation.CurrencyId,
                TalabKar = 0,
                BadehKar = operation.Amount,
                Description = description,
                CreatedAt = operation.OperationDate
            });

            ledgerEntries.Add(new LedgerEntry
            {
                AccountMoneyOperationId = operation.Id,
                AccountId = operation.CashOrBankAccountId,
                CurrencyId = operation.CurrencyId,
                TalabKar = operation.Amount,
                BadehKar = 0,
                Description = description,
                CreatedAt = operation.OperationDate
            });
        }
        else
        {
            throw new InvalidOperationException("نوع عملیات معتبر نیست.");
        }

        await _context.LedgerEntries.AddRangeAsync(ledgerEntries);
    }

    private async Task DeleteLedgerEntriesAsync(long operationId)
    {
        var ledgerEntries = await _context.LedgerEntries
            .Where(x => x.AccountMoneyOperationId == operationId)
            .ToListAsync();

        if (ledgerEntries.Any())
        {
            _context.LedgerEntries.RemoveRange(ledgerEntries);
        }
    }

    private async Task ValidateAccountsAsync(
        long accountId,
        long cashOrBankAccountId,
        long currencyId)
    {
        var currencyExists = await _context.Currencies
            .AnyAsync(x => x.Id == currencyId && x.IsActive);

        if (!currencyExists)
            throw new InvalidOperationException("ارز انتخاب‌شده معتبر نیست.");

        var account = await _context.Accounts
            .FirstOrDefaultAsync(x => x.Id == accountId && !x.IsArchived);

        if (account == null)
            throw new InvalidOperationException("حساب انتخاب‌شده معتبر نیست.");

        var cashOrBankAccount = await _context.Accounts
            .FirstOrDefaultAsync(x => x.Id == cashOrBankAccountId && !x.IsArchived);

        if (cashOrBankAccount == null)
            throw new InvalidOperationException("حساب صندوق/بانک معتبر نیست.");

        if (cashOrBankAccount.AccountType != "Cash" &&
            cashOrBankAccount.AccountType != "Bank")
        {
            throw new InvalidOperationException("حساب پرداخت/دریافت باید از نوع Cash یا Bank باشد.");
        }

        if (accountId == cashOrBankAccountId)
            throw new InvalidOperationException("حساب انتخاب‌شده و حساب صندوق/بانک نمی‌تواند یکی باشد.");
    }

    private static void ValidateCreateDto(CreateAccountMoneyOperationDto dto)
    {
        if (dto.OperationType != "Deposit" && dto.OperationType != "Withdraw")
            throw new InvalidOperationException("نوع عملیات باید واریز یا برداشت باشد.");

        if (dto.AccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب الزامی است.");

        if (dto.CashOrBankAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب صندوق/بانک الزامی است.");

        if (dto.CurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز الزامی است.");

        if (dto.Amount <= 0)
            throw new InvalidOperationException("مبلغ باید بزرگتر از صفر باشد.");
    }

    private static void ValidateUpdateDto(UpdateAccountMoneyOperationDto dto)
    {
        if (dto.OperationType != "Deposit" && dto.OperationType != "Withdraw")
            throw new InvalidOperationException("نوع عملیات باید واریز یا برداشت باشد.");

        if (dto.AccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب الزامی است.");

        if (dto.CashOrBankAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب صندوق/بانک الزامی است.");

        if (dto.CurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز الزامی است.");

        if (dto.Amount <= 0)
            throw new InvalidOperationException("مبلغ باید بزرگتر از صفر باشد.");
    }

    private static string GetDefaultDescription(string operationType)
    {
        return operationType == "Deposit"
            ? "ثبت واریز"
            : "ثبت برداشت";
    }

    private long GetCurrentUserId() => 1;
}