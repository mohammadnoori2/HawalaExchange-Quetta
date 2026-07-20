using AutoMapper;
using AutoMapper.QueryableExtensions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class MoneyExchangeOperationService : IMoneyExchangeOperationService
{
    private readonly ApplicationDbContext _context;
    private readonly IMapper _mapper;

    public MoneyExchangeOperationService(
        ApplicationDbContext context,
        IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<IEnumerable<MoneyExchangeOperationDto>> GetAllAsync()
    {
        return await _context.MoneyExchangeOperations
            .AsNoTracking()
            .Include(x => x.FromAccount)
            .Include(x => x.ToAccount)
            .Include(x => x.FromCurrency)
            .Include(x => x.ToCurrency)
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.ExchangeDate)
            .ProjectTo<MoneyExchangeOperationDto>(_mapper.ConfigurationProvider)
            .ToListAsync();
    }

    public async Task<MoneyExchangeOperationDto?> GetByIdAsync(long id)
    {
        return await _context.MoneyExchangeOperations
            .AsNoTracking()
            .Include(x => x.FromAccount)
            .Include(x => x.ToAccount)
            .Include(x => x.FromCurrency)
            .Include(x => x.ToCurrency)
            .Where(x => x.Id == id && !x.IsDeleted)
            .ProjectTo<MoneyExchangeOperationDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
    }

    public async Task<MoneyExchangeOperationDto> CreateAsync(CreateMoneyExchangeOperationDto dto)
    {
        ValidateCreateDto(dto);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await ValidateAccountsAndCurrenciesAsync(dto.FromAccountId, dto.ToAccountId, dto.FromCurrencyId, dto.ToCurrencyId);

            var exchange = _mapper.Map<MoneyExchangeOperation>(dto);

            exchange.ExchangeDate = dto.ExchangeDate == DateTime.MinValue
                ? DateTime.UtcNow
                : dto.ExchangeDate;

            exchange.ExchangeRate = dto.ExchangeRate <= 0
                ? dto.ToAmount / dto.FromAmount
                : dto.ExchangeRate;

            exchange.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? "ثبت تبدیل پول"
                : dto.Description.Trim();

            exchange.CreatedAt = DateTime.UtcNow;
            exchange.CreatedBy = GetCurrentUserId();
            exchange.IsDeleted = false;

            await _context.MoneyExchangeOperations.AddAsync(exchange);
            await _context.SaveChangesAsync();

            await CreateLedgerEntriesAsync(exchange);

            await _context.SaveChangesAsync();

            await dbTransaction.CommitAsync();

            var result = await GetByIdAsync(exchange.Id);

            if (result == null)
                throw new InvalidOperationException("تبدیل پول ثبت شد، اما در بارگذاری دوباره یافت نشد.");

            return result;
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    public async Task<MoneyExchangeOperationDto> UpdateAsync(long id, UpdateMoneyExchangeOperationDto dto)
    {
        ValidateUpdateDto(dto);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var exchange = await _context.MoneyExchangeOperations
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (exchange == null)
                throw new KeyNotFoundException($"تبدیل پول با شناسه {id} یافت نشد.");

            await ValidateAccountsAndCurrenciesAsync(dto.FromAccountId, dto.ToAccountId, dto.FromCurrencyId, dto.ToCurrencyId);

            await DeleteLedgerEntriesAsync(exchange.Id);

            _mapper.Map(dto, exchange);

            exchange.ExchangeDate = dto.ExchangeDate == DateTime.MinValue
                ? DateTime.UtcNow
                : dto.ExchangeDate;

            exchange.ExchangeRate = dto.ExchangeRate <= 0
                ? dto.ToAmount / dto.FromAmount
                : dto.ExchangeRate;

            exchange.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? "ثبت تبدیل پول"
                : dto.Description.Trim();

            exchange.ModifiedAt = DateTime.UtcNow;
            exchange.ModifiedBy = GetCurrentUserId();

            await CreateLedgerEntriesAsync(exchange);

            await _context.SaveChangesAsync();

            await dbTransaction.CommitAsync();

            var result = await GetByIdAsync(exchange.Id);

            if (result == null)
                throw new InvalidOperationException("تبدیل پول ویرایش شد، اما در بارگذاری دوباره یافت نشد.");

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
            var exchange = await _context.MoneyExchangeOperations
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (exchange == null)
                throw new KeyNotFoundException($"تبدیل پول با شناسه {id} یافت نشد.");

            await DeleteLedgerEntriesAsync(exchange.Id);

            exchange.IsDeleted = true;
            exchange.ModifiedAt = DateTime.UtcNow;
            exchange.ModifiedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    private async Task CreateLedgerEntriesAsync(MoneyExchangeOperation exchange)
    {
        var description = $"{exchange.Description} با شماره {exchange.Id}";

        var ledgerEntries = new List<LedgerEntry>
        {
            new LedgerEntry
            {
                MoneyExchangeOperationId = exchange.Id,
                CapitalInvestmentId = null,
                ExpenseId = null,
                HawalaId = null,
                TransactionId = null,

                AccountId = exchange.ToAccountId,
                CurrencyId = exchange.ToCurrencyId,

                TalabKar = exchange.ToAmount,
                BadehKar = 0,

                Description = description,
                CreatedAt = exchange.ExchangeDate
            },
            new LedgerEntry
            {
                MoneyExchangeOperationId = exchange.Id,
                CapitalInvestmentId = null,
                ExpenseId = null,
                HawalaId = null,
                TransactionId = null,

                AccountId = exchange.FromAccountId,
                CurrencyId = exchange.FromCurrencyId,

                TalabKar = 0,
                BadehKar = exchange.FromAmount,

                Description = description,
                CreatedAt = exchange.ExchangeDate
            }
        };

        await _context.LedgerEntries.AddRangeAsync(ledgerEntries);
    }

    private async Task DeleteLedgerEntriesAsync(long moneyExchangeOperationId)
    {
        var ledgerEntries = await _context.LedgerEntries
            .Where(x => x.MoneyExchangeOperationId == moneyExchangeOperationId)
            .ToListAsync();

        if (ledgerEntries.Any())
        {
            _context.LedgerEntries.RemoveRange(ledgerEntries);
        }
    }

    private async Task ValidateAccountsAndCurrenciesAsync(
        long fromAccountId,
        long toAccountId,
        long fromCurrencyId,
        long toCurrencyId)
    {
        var fromAccountExists = await _context.Accounts
            .AnyAsync(x => x.Id == fromAccountId && !x.IsArchived);

        if (!fromAccountExists)
            throw new InvalidOperationException("حساب پرداخت‌کننده معتبر نیست.");

        var toAccountExists = await _context.Accounts
            .AnyAsync(x => x.Id == toAccountId && !x.IsArchived);

        if (!toAccountExists)
            throw new InvalidOperationException("حساب دریافت‌کننده معتبر نیست.");

        var fromCurrencyExists = await _context.Currencies
            .AnyAsync(x => x.Id == fromCurrencyId && x.IsActive);

        if (!fromCurrencyExists)
            throw new InvalidOperationException("ارز پرداختی معتبر نیست.");

        var toCurrencyExists = await _context.Currencies
            .AnyAsync(x => x.Id == toCurrencyId && x.IsActive);

        if (!toCurrencyExists)
            throw new InvalidOperationException("ارز دریافتی معتبر نیست.");

        if (fromCurrencyId == toCurrencyId)
            throw new InvalidOperationException("ارز پرداختی و ارز دریافتی نباید یکی باشد.");
    }

    private static void ValidateCreateDto(CreateMoneyExchangeOperationDto dto)
    {
        if (dto.FromAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب پرداخت‌کننده الزامی است.");

        if (dto.ToAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب دریافت‌کننده الزامی است.");

        if (dto.FromCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز پرداختی الزامی است.");

        if (dto.ToCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز دریافتی الزامی است.");

        if (dto.FromAmount <= 0)
            throw new InvalidOperationException("مبلغ پرداختی باید بزرگتر از صفر باشد.");

        if (dto.ToAmount <= 0)
            throw new InvalidOperationException("مبلغ دریافتی باید بزرگتر از صفر باشد.");

        if (dto.ExchangeRate <= 0)
            throw new InvalidOperationException("نرخ تبدیل باید بزرگتر از صفر باشد.");
    }

    private static void ValidateUpdateDto(UpdateMoneyExchangeOperationDto dto)
    {
        if (dto.FromAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب پرداخت‌کننده الزامی است.");

        if (dto.ToAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب دریافت‌کننده الزامی است.");

        if (dto.FromCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز پرداختی الزامی است.");

        if (dto.ToCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز دریافتی الزامی است.");

        if (dto.FromAmount <= 0)
            throw new InvalidOperationException("مبلغ پرداختی باید بزرگتر از صفر باشد.");

        if (dto.ToAmount <= 0)
            throw new InvalidOperationException("مبلغ دریافتی باید بزرگتر از صفر باشد.");

        if (dto.ExchangeRate <= 0)
            throw new InvalidOperationException("نرخ تبدیل باید بزرگتر از صفر باشد.");
    }

    private long GetCurrentUserId() => 1;
}