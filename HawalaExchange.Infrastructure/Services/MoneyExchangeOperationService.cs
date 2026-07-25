using AutoMapper;
using AutoMapper.QueryableExtensions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace HawalaExchange.Application.Services;

public class MoneyExchangeOperationService : IMoneyExchangeOperationService
{
    private readonly ApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly ICurrencyCostService _currencyCostService;

    public MoneyExchangeOperationService(
        ApplicationDbContext context,
        IMapper mapper,
        ICurrencyCostService currencyCostService)
    {
        _context = context;
        _mapper = mapper;
        _currencyCostService = currencyCostService;
    }

    public async Task<IEnumerable<MoneyExchangeOperationDto>> GetAllAsync()
    {
        return await _context.MoneyExchangeOperations
            .AsNoTracking()
            .Include(x => x.FromAccount)
            .Include(x => x.ToAccount)
            .Include(x => x.FromCurrency)
            .Include(x => x.ToCurrency)
            .Include(x => x.ProfitCurrency)
            .Include(x => x.RateBaseCurrency)
            .Include(x => x.RateQuoteCurrency)
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
            .Include(x => x.ProfitCurrency)
            .Include(x => x.RateBaseCurrency)
            .Include(x => x.RateQuoteCurrency)
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
            await ValidateAccountsAndCurrenciesAsync(dto.FromAccountId, dto.ToAccountId, dto.FromCurrencyId, dto.ToCurrencyId, dto.OperationType);
            await ValidateProfitCurrencyAsync(dto.ProfitCurrencyId);

            var exchange = _mapper.Map<MoneyExchangeOperation>(dto);

            exchange.ExchangeDate = dto.ExchangeDate == DateTime.MinValue
                ? DateTime.UtcNow
                : dto.ExchangeDate;

            await ApplyCanonicalRateAsync(exchange);

            exchange.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? "ثبت تبدیل پول"
                : dto.Description.Trim();

            exchange.CreatedAt = DateTime.UtcNow;
            exchange.CreatedBy = GetCurrentUserId();
            exchange.IsDeleted = false;

            await _context.MoneyExchangeOperations.AddAsync(exchange);
            await _context.SaveChangesAsync();

            await _currencyCostService.RebuildAsync();

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

            await ValidateAccountsAndCurrenciesAsync(dto.FromAccountId, dto.ToAccountId, dto.FromCurrencyId, dto.ToCurrencyId, dto.OperationType);
            await ValidateProfitCurrencyAsync(dto.ProfitCurrencyId);

            await DeleteLedgerEntriesAsync(exchange.Id);

            _mapper.Map(dto, exchange);

            exchange.ExchangeDate = dto.ExchangeDate == DateTime.MinValue
                ? DateTime.UtcNow
                : dto.ExchangeDate;

            await ApplyCanonicalRateAsync(exchange);

            exchange.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? "ثبت تبدیل پول"
                : dto.Description.Trim();

            exchange.ModifiedAt = DateTime.UtcNow;
            exchange.ModifiedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();
            await _currencyCostService.RebuildAsync();

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
            await _currencyCostService.RebuildAsync();

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    internal static async Task CreateLedgerEntriesAsync(
        ApplicationDbContext context, MoneyExchangeOperation exchange)
    {
        var description = await BuildPrimaryLedgerDescriptionAsync(context, exchange);

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

        if (exchange.OperationType == "Treasury" && exchange.ProfitCurrencyId.HasValue)
        {
            var systemAccounts = await context.Accounts
                .Where(x => x.AccountCode == "1201" || x.AccountCode == "2101" ||
                            x.AccountCode == "3002" || x.AccountCode == "3001" ||
                            x.AccountCode == "4001")
                .ToDictionaryAsync(x => x.AccountCode);
            var inventoryAccount = RequireSystemAccount(systemAccounts, "1201");
            var shortLiabilityAccount = RequireSystemAccount(systemAccounts, "2101");
            var profitAccount = RequireSystemAccount(systemAccounts, "3002");
            var profitCurrencyId = exchange.ProfitCurrencyId.Value;

            AddDebit(ledgerEntries, exchange, inventoryAccount, profitCurrencyId,
                exchange.InventoryCostIncrease, "افزایش موجودی ارز به بهای تمام‌شده");
            AddCredit(ledgerEntries, exchange, inventoryAccount, profitCurrencyId,
                exchange.InventoryCostDecrease, "بهای تمام‌شده ارز فروش‌رفته");
            AddCredit(ledgerEntries, exchange, shortLiabilityAccount, profitCurrencyId,
                exchange.ShortLiabilityIncrease, "افزایش تعهد فروش ارز");
            AddDebit(ledgerEntries, exchange, shortLiabilityAccount, profitCurrencyId,
                exchange.ShortLiabilityDecrease, "تسویه تعهد فروش ارز");

            if (exchange.ExchangeProfitAmount >= 0)
                AddDebit(ledgerEntries, exchange, profitAccount, profitCurrencyId,
                    exchange.ExchangeProfitAmount, "مفاد تحقق‌یافته تبدیل پول");
            else
                AddCredit(ledgerEntries, exchange, profitAccount, profitCurrencyId,
                    -exchange.ExchangeProfitAmount, "زیان تحقق‌یافته تبدیل پول");
        }

        if (exchange.ProfitCurrencyId.HasValue && exchange.CommissionAmount > 0)
        {
            var commissionAccount = await context.Accounts
                .FirstOrDefaultAsync(x => x.AccountCode == "3001")
                ?? throw new InvalidOperationException("حساب درآمد کمیسیون با کد 3001 یافت نشد.");
            AddDebit(ledgerEntries, exchange, exchange.ToAccountId, exchange.ProfitCurrencyId.Value,
                exchange.CommissionAmount, "کمیسیون تبدیل پول");
            AddCredit(ledgerEntries, exchange, commissionAccount.Id, exchange.ProfitCurrencyId.Value,
                exchange.CommissionAmount, "درآمد کمیسیون تبدیل پول");
        }

        if (exchange.ProfitCurrencyId.HasValue && exchange.ExternalFeeAmount > 0)
        {
            if (exchange.FromCurrencyId == exchange.ProfitCurrencyId)
            {
                AddCredit(ledgerEntries, exchange, exchange.FromAccountId, exchange.ProfitCurrencyId.Value,
                    exchange.ExternalFeeAmount, "هزینه مستقیم خرید ارز");
            }
            else
            {
                var expenseAccount = await context.Accounts
                    .FirstOrDefaultAsync(x => x.AccountCode == "4001")
                    ?? throw new InvalidOperationException("حساب هزینه تبدیل پول یافت نشد.");
                AddDebit(ledgerEntries, exchange, expenseAccount.Id, exchange.ProfitCurrencyId.Value,
                    exchange.ExternalFeeAmount, "هزینه مستقیم فروش ارز");
                AddCredit(ledgerEntries, exchange, exchange.ToAccountId, exchange.ProfitCurrencyId.Value,
                    exchange.ExternalFeeAmount, "پرداخت هزینه مستقیم فروش ارز");
            }
        }

        await context.LedgerEntries.AddRangeAsync(ledgerEntries);
    }

    private static async Task<string> BuildPrimaryLedgerDescriptionAsync(
        ApplicationDbContext context,
        MoneyExchangeOperation exchange)
    {
        var description = $"{exchange.Description} با شماره {exchange.Id}";

        if (exchange.OperationType != "Customer")
            return description;

        CurrencyQuotationResult quotation;

        if (exchange.FromCurrency != null && exchange.ToCurrency != null)
        {
            quotation = CurrencyQuotationCalculator.Calculate(
                exchange.FromCurrency.Id,
                exchange.FromCurrency.Code,
                exchange.FromCurrency.QuotationPriority,
                exchange.FromAmount,
                exchange.ToCurrency.Id,
                exchange.ToCurrency.Code,
                exchange.ToCurrency.QuotationPriority,
                exchange.ToAmount);
        }
        else
        {
            var currencies = await context.Currencies
                .AsNoTracking()
                .Where(currency =>
                    currency.Id == exchange.FromCurrencyId ||
                    currency.Id == exchange.ToCurrencyId)
                .ToDictionaryAsync(currency => currency.Id);

            if (!currencies.TryGetValue(exchange.FromCurrencyId, out var fromCurrency) ||
                !currencies.TryGetValue(exchange.ToCurrencyId, out var toCurrency))
            {
                return description;
            }

            quotation = CurrencyQuotationCalculator.Calculate(
                fromCurrency.Id,
                fromCurrency.Code,
                fromCurrency.QuotationPriority,
                exchange.FromAmount,
                toCurrency.Id,
                toCurrency.Code,
                toCurrency.QuotationPriority,
                exchange.ToAmount);
        }

        var formattedRate = quotation.Rate.ToString(
            "0.00######",
            CultureInfo.InvariantCulture);

        return $"{description} - نرخ تبدیل: 1 {quotation.BaseCurrencyCode} = {formattedRate} {quotation.QuoteCurrencyCode}";
    }

    private static long RequireSystemAccount(IReadOnlyDictionary<string, Account> accounts, string code) =>
        accounts.TryGetValue(code, out var account)
            ? account.Id
            : throw new InvalidOperationException($"حساب سیستمی با کد {code} یافت نشد.");

    private static void AddDebit(
        ICollection<LedgerEntry> entries, MoneyExchangeOperation exchange,
        long accountId, long currencyId, decimal amount, string description)
    {
        if (amount <= 0) return;
        entries.Add(NewLedgerEntry(exchange, accountId, currencyId, amount, 0, description));
    }

    private static void AddCredit(
        ICollection<LedgerEntry> entries, MoneyExchangeOperation exchange,
        long accountId, long currencyId, decimal amount, string description)
    {
        if (amount <= 0) return;
        entries.Add(NewLedgerEntry(exchange, accountId, currencyId, 0, amount, description));
    }

    private static LedgerEntry NewLedgerEntry(
        MoneyExchangeOperation exchange, long accountId, long currencyId,
        decimal debit, decimal credit, string description) => new()
    {
        MoneyExchangeOperationId = exchange.Id,
        AccountId = accountId,
        CurrencyId = currencyId,
        TalabKar = debit,
        BadehKar = credit,
        Description = $"{description} - تبدیل شماره {exchange.Id}",
        CreatedAt = exchange.ExchangeDate
    };

    private async Task ApplyCanonicalRateAsync(MoneyExchangeOperation exchange)
    {
        var currencies = await _context.Currencies
            .Where(x => x.Id == exchange.FromCurrencyId || x.Id == exchange.ToCurrencyId)
            .ToDictionaryAsync(x => x.Id);
        var from = currencies[exchange.FromCurrencyId];
        var to = currencies[exchange.ToCurrencyId];
        var quotation = CurrencyQuotationCalculator.Calculate(
            from.Id, from.Code, from.QuotationPriority, exchange.FromAmount,
            to.Id, to.Code, to.QuotationPriority, exchange.ToAmount);
        exchange.RateBaseCurrencyId = quotation.BaseCurrencyId;
        exchange.RateQuoteCurrencyId = quotation.QuoteCurrencyId;
        exchange.ExchangeRate = decimal.Round(quotation.Rate, 8);
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
        long toCurrencyId,
        string operationType)
    {
        var fromAccount = await _context.Accounts
            .FirstOrDefaultAsync(x => x.Id == fromAccountId && !x.IsArchived);

        if (fromAccount == null)
            throw new InvalidOperationException("حساب پرداخت‌کننده معتبر نیست.");

        var toAccount = await _context.Accounts
            .FirstOrDefaultAsync(x => x.Id == toAccountId && !x.IsArchived);

        if (toAccount == null)
            throw new InvalidOperationException("حساب دریافت‌کننده معتبر نیست.");

        if (operationType == "Customer" &&
            (fromAccount.AccountType != "Customer" || toAccount.AccountType != "Customer" ||
             fromAccountId != toAccountId))
            throw new InvalidOperationException("برای تبدیل پول مشتری، هر دو طرف باید همان حساب مشتری باشد.");

        if (operationType == "Treasury" &&
            (!IsTreasuryAccount(fromAccount.AccountType) || !IsTreasuryAccount(toAccount.AccountType)))
            throw new InvalidOperationException("برای تبدیل سرمایه صراف فقط حساب نقدی یا بانکی قابل انتخاب است.");

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

    private static bool IsTreasuryAccount(string accountType) =>
        accountType is "Cash" or "Bank";

    private async Task ValidateProfitCurrencyAsync(long profitCurrencyId)
    {
        if (profitCurrencyId <= 0 ||
            !await _context.Currencies.AnyAsync(x => x.Id == profitCurrencyId && x.IsActive))
            throw new InvalidOperationException("ارز محاسبه سود معتبر نیست.");
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

        ValidateProfitFields(dto.OperationType, dto.ProfitCurrencyId, dto.CommissionAmount, dto.ExternalFeeAmount);
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

        ValidateProfitFields(dto.OperationType, dto.ProfitCurrencyId, dto.CommissionAmount, dto.ExternalFeeAmount);
    }

    private static void ValidateProfitFields(
        string operationType, long profitCurrencyId, decimal commission, decimal externalFee)
    {
        if (operationType is not ("Customer" or "Treasury"))
            throw new InvalidOperationException("نوع تبدیل باید مشتری یا سرمایه صراف باشد.");
        if (profitCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز محاسبه سود الزامی است.");
        if (commission < 0 || externalFee < 0)
            throw new InvalidOperationException("کمیسیون و هزینه خارجی نمی‌تواند منفی باشد.");
    }

    public Task<IReadOnlyList<CurrencyCostPositionDto>> GetCostPositionsAsync() =>
        _currencyCostService.GetPositionsAsync();

    public async Task<MoneyExchangeProfitSummaryDto> GetProfitSummaryAsync(long? profitCurrencyId = null)
    {
        var query = _context.MoneyExchangeOperations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ProfitCurrencyId != null);
        if (profitCurrencyId.HasValue)
            query = query.Where(x => x.ProfitCurrencyId == profitCurrencyId.Value);

        return new MoneyExchangeProfitSummaryDto
        {
            CustomerRealizedProfit = await query.Where(x => x.OperationType == "Customer")
                .SumAsync(x => x.RealizedProfit),
            TreasuryRealizedProfit = await query.Where(x => x.OperationType == "Treasury")
                .SumAsync(x => x.RealizedProfit),
            DeferredOperationCount = await query.CountAsync(x =>
                x.ProfitStatus == "Deferred" || x.ProfitStatus == "PartiallyDeferred")
        };
    }

    private long GetCurrentUserId() => 1;
}
