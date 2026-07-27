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

        var isCustomerExchange = exchange.OperationType == "Customer";
        var ledgerEntries = new List<LedgerEntry>
        {
            NewPrimaryLedgerEntry(
                exchange,
                exchange.ToAccountId,
                exchange.ToCurrencyId,
                talabKar: isCustomerExchange ? exchange.ToAmount : 0,
                badehKar: isCustomerExchange ? 0 : exchange.ToAmount,
                description),
            NewPrimaryLedgerEntry(
                exchange,
                exchange.FromAccountId,
                exchange.FromCurrencyId,
                talabKar: isCustomerExchange ? 0 : exchange.FromAmount,
                badehKar: isCustomerExchange ? exchange.FromAmount : 0,
                description)
        };

        if (exchange.ProfitCurrencyId.HasValue)
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

            AddBadehKar(ledgerEntries, exchange, inventoryAccount, profitCurrencyId,
                exchange.InventoryCostIncrease, "افزایش موجودی ارز به بهای تمام‌شده");
            AddTalabKar(ledgerEntries, exchange, inventoryAccount, profitCurrencyId,
                exchange.InventoryCostDecrease, "بهای تمام‌شده ارز فروش‌رفته");
            AddTalabKar(ledgerEntries, exchange, shortLiabilityAccount, profitCurrencyId,
                exchange.ShortLiabilityIncrease, "افزایش تعهد فروش ارز");
            AddBadehKar(ledgerEntries, exchange, shortLiabilityAccount, profitCurrencyId,
                exchange.ShortLiabilityDecrease, "تسویه تعهد فروش ارز");

            if (exchange.ExchangeProfitAmount >= 0)
                AddTalabKar(ledgerEntries, exchange, profitAccount, profitCurrencyId,
                    exchange.ExchangeProfitAmount, "مفاد تحقق‌یافته تبدیل پول");
            else
                AddBadehKar(ledgerEntries, exchange, profitAccount, profitCurrencyId,
                    -exchange.ExchangeProfitAmount, "زیان تحقق‌یافته تبدیل پول");
        }

        if (exchange.ProfitCurrencyId.HasValue && exchange.CommissionAmount > 0)
        {
            var commissionAccount = await context.Accounts
                .FirstOrDefaultAsync(x => x.AccountCode == "3001")
                ?? throw new InvalidOperationException("حساب درآمد کمیسیون با کد 3001 یافت نشد.");
            AddBadehKar(ledgerEntries, exchange, exchange.ToAccountId, exchange.ProfitCurrencyId.Value,
                exchange.CommissionAmount, "کمیسیون تبدیل پول");
            AddTalabKar(ledgerEntries, exchange, commissionAccount.Id, exchange.ProfitCurrencyId.Value,
                exchange.CommissionAmount, "درآمد کمیسیون تبدیل پول");
        }

        if (exchange.ProfitCurrencyId.HasValue && exchange.ExternalFeeAmount > 0)
        {
            if (exchange.FromCurrencyId == exchange.ProfitCurrencyId)
            {
                AddTalabKar(ledgerEntries, exchange, exchange.FromAccountId, exchange.ProfitCurrencyId.Value,
                    exchange.ExternalFeeAmount, "هزینه مستقیم خرید ارز");
            }
            else
            {
                var expenseAccount = await context.Accounts
                    .FirstOrDefaultAsync(x => x.AccountCode == "4001")
                    ?? throw new InvalidOperationException("حساب هزینه تبدیل پول یافت نشد.");
                AddBadehKar(ledgerEntries, exchange, expenseAccount.Id, exchange.ProfitCurrencyId.Value,
                    exchange.ExternalFeeAmount, "هزینه مستقیم فروش ارز");
                AddTalabKar(ledgerEntries, exchange, exchange.ToAccountId, exchange.ProfitCurrencyId.Value,
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

    private static void AddTalabKar(
        ICollection<LedgerEntry> entries, MoneyExchangeOperation exchange,
        long accountId, long currencyId, decimal amount, string description)
    {
        if (amount <= 0) return;
        entries.Add(NewLedgerEntry(exchange, accountId, currencyId, amount, 0, description));
    }

    private static void AddBadehKar(
        ICollection<LedgerEntry> entries, MoneyExchangeOperation exchange,
        long accountId, long currencyId, decimal amount, string description)
    {
        if (amount <= 0) return;
        entries.Add(NewLedgerEntry(exchange, accountId, currencyId, 0, amount, description));
    }

    private static LedgerEntry NewLedgerEntry(
        MoneyExchangeOperation exchange, long accountId, long currencyId,
        decimal talabKar, decimal badehKar, string description) => new()
    {
        MoneyExchangeOperationId = exchange.Id,
        AccountId = accountId,
        CurrencyId = currencyId,
        TalabKar = talabKar,
        BadehKar = badehKar,
        Description = $"{description} - تبدیل شماره {exchange.Id}",
        CreatedAt = exchange.ExchangeDate
    };

    private static LedgerEntry NewPrimaryLedgerEntry(
        MoneyExchangeOperation exchange,
        long accountId,
        long currencyId,
        decimal talabKar,
        decimal badehKar,
        string description) => new()
    {
        MoneyExchangeOperationId = exchange.Id,
        CapitalInvestmentId = null,
        ExpenseId = null,
        HawalaId = null,
        TransactionId = null,
        AccountId = accountId,
        CurrencyId = currencyId,
        TalabKar = talabKar,
        BadehKar = badehKar,
        Description = description,
        CreatedAt = exchange.ExchangeDate
    };

    private async Task ApplyCanonicalRateAsync(MoneyExchangeOperation exchange)
    {
        var currencies = await _context.Currencies
            .Where(x => x.Id == exchange.FromCurrencyId || x.Id == exchange.ToCurrencyId)
            .ToDictionaryAsync(x => x.Id);
        var from = currencies[exchange.FromCurrencyId];
        var to = currencies[exchange.ToCurrencyId];
        var conversion = CurrencyQuotationCalculator.ConvertFromAmount(
            from.Id, from.Code, from.QuotationPriority, exchange.FromAmount,
            to.Id, to.Code, to.QuotationPriority, exchange.ExchangeRate);
        exchange.RateBaseCurrencyId = conversion.BaseCurrencyId;
        exchange.RateQuoteCurrencyId = conversion.QuoteCurrencyId;
        exchange.ToAmount = decimal.Round(conversion.ToAmount, to.DecimalPlaces);
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
            throw new InvalidOperationException("حساب ارز فروش معتبر نیست.");

        var toAccount = await _context.Accounts
            .FirstOrDefaultAsync(x => x.Id == toAccountId && !x.IsArchived);

        if (toAccount == null)
            throw new InvalidOperationException("حساب ارز خرید معتبر نیست.");

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
            throw new InvalidOperationException("ارز فروش معتبر نیست.");

        var toCurrencyExists = await _context.Currencies
            .AnyAsync(x => x.Id == toCurrencyId && x.IsActive);

        if (!toCurrencyExists)
            throw new InvalidOperationException("ارز خرید معتبر نیست.");

        if (fromCurrencyId == toCurrencyId)
            throw new InvalidOperationException("ارز فروش و ارز خرید نباید یکی باشد.");
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
            throw new InvalidOperationException("انتخاب حساب ارز فروش الزامی است.");

        if (dto.ToAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب ارز خرید الزامی است.");

        if (dto.FromCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز فروش الزامی است.");

        if (dto.ToCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز خرید الزامی است.");

        if (dto.FromAmount <= 0)
            throw new InvalidOperationException("مبلغ ارز فروش باید بزرگتر از صفر باشد.");

        if (dto.ToAmount <= 0)
            throw new InvalidOperationException("مبلغ ارز خرید باید بزرگتر از صفر باشد.");

        ValidateProfitFields(dto.OperationType, dto.ProfitCurrencyId, dto.CommissionAmount, dto.ExternalFeeAmount);
    }

    private static void ValidateUpdateDto(UpdateMoneyExchangeOperationDto dto)
    {
        if (dto.FromAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب ارز فروش الزامی است.");

        if (dto.ToAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب ارز خرید الزامی است.");

        if (dto.FromCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز فروش الزامی است.");

        if (dto.ToCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز خرید الزامی است.");

        if (dto.FromAmount <= 0)
            throw new InvalidOperationException("مبلغ ارز فروش باید بزرگتر از صفر باشد.");

        if (dto.ToAmount <= 0)
            throw new InvalidOperationException("مبلغ ارز خرید باید بزرگتر از صفر باشد.");

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
