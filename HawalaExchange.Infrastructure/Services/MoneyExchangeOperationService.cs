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
    public async Task<MoneyExchangeOperationDto?> GetByLinkedHawalaAsync(long hawalaId) {
        var id=await _context.MoneyExchangeOperations.Where(x=>x.LinkedHawalaId==hawalaId && !x.IsDeleted).Select(x=>(long?)x.Id).SingleOrDefaultAsync();
        return id.HasValue?await GetByIdAsync(id.Value):null;
    }
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
        if(dto.LinkedHawalaId.HasValue && !dto.ConfirmNoPreviousManualConversion)
            throw new InvalidOperationException("ابتدا بررسی و تأیید کنید که مبلغ حواله قبلاً به‌صورت دستی تبدیل نشده است.");
        dto.ProfitCurrencyId = await ResolveDefaultProfitCurrencyIdAsync();
        ValidateCreateDto(dto);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await LockCorrespondentAccountsAsync(dto.OperationType, dto.FromAccountId, dto.ToAccountId);
            await ValidateAccountsAndCurrenciesAsync(dto.FromAccountId, dto.ToAccountId, dto.FromCurrencyId, dto.ToCurrencyId, dto.OperationType);
            await ValidateProfitCurrencyAsync(dto.ProfitCurrencyId);

            await ValidateLinkedHawalaAsync(dto.LinkedHawalaId,dto.FromAccountId,dto.ToAccountId,dto.FromCurrencyId,dto.FromAmount,dto.OperationType);

            var exchange = _mapper.Map<MoneyExchangeOperation>(dto);
            if(dto.LinkedHawalaId.HasValue) {
                exchange.ToAmount=AmountValueHelper.RoundConvertedAmount(dto.FromAmount*dto.ExchangeRate);
                if(exchange.ToAmount<=0) throw new InvalidOperationException("مبلغ تبدیل‌شده باید بیشتر از صفر باشد.");
                var pair=await _context.Currencies.Where(x=>x.Id==dto.FromCurrencyId || x.Id==dto.ToCurrencyId).ToDictionaryAsync(x=>x.Id);
                var a=pair[dto.FromCurrencyId]; var b=pair[dto.ToCurrencyId];
                exchange.ExchangeRate=CurrencyQuotationCalculator.Calculate(a.Id,a.Code,a.QuotationPriority,exchange.FromAmount,b.Id,b.Code,b.QuotationPriority,exchange.ToAmount).Rate;
            }

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
            await ValidateCorrespondentPeriodAsync(exchange);

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
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<MoneyExchangeOperationDto> UpdateAsync(long id, UpdateMoneyExchangeOperationDto dto)
    {
        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var exchange = await _context.MoneyExchangeOperations
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (exchange == null)
                throw new KeyNotFoundException($"تبدیل پول با شناسه {id} یافت نشد.");

            // ارز مفاد هر سند تاریخی ثابت می‌ماند. فقط سندهای قدیمی فاقد این مقدار،
            // هنگام ویرایش ارز اصلی فعلی تنظیمات را دریافت می‌کنند.
            dto.ProfitCurrencyId = exchange.ProfitCurrencyId
                ?? await ResolveDefaultProfitCurrencyIdAsync();
            ValidateUpdateDto(dto);

            await LockCorrespondentAccountsAsync(MoneyExchangeMode.IsCorrespondent(exchange.OperationType) ? exchange.OperationType : dto.OperationType,
                exchange.FromAccountId, exchange.ToAccountId, dto.FromAccountId, dto.ToAccountId);
            await ValidateCorrespondentPeriodAsync(exchange);

            await ValidateLinkedHawalaAsync(exchange.LinkedHawalaId,dto.FromAccountId,dto.ToAccountId,dto.FromCurrencyId,dto.FromAmount,dto.OperationType,exchange.Id);
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
            await ValidateCorrespondentPeriodAsync(exchange);
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
            _context.ChangeTracker.Clear();
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

            await LockCorrespondentAccountsAsync(exchange.OperationType, exchange.FromAccountId, exchange.ToAccountId);
            await ValidateCorrespondentPeriodAsync(exchange);
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
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    internal static async Task CreateLedgerEntriesAsync(
        ApplicationDbContext context, MoneyExchangeOperation exchange)
    {
        var description = await BuildPrimaryLedgerDescriptionAsync(context, exchange);

        var isCustomerExchange = MoneyExchangeMode.UsesPartyLedger(exchange.OperationType);
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
                .Where(x =>
                            x.AccountCode == ApplicationDbContext.CurrencyInventoryAccountCode ||
                            x.AccountCode == ApplicationDbContext.CurrencySaleLiabilityAccountCode ||
                            x.AccountCode == "3002" || x.AccountCode == "3001" ||
                            x.AccountCode == "4001")
                .ToDictionaryAsync(x => x.AccountCode);
            var inventoryAccount = RequireSystemAccount(
                systemAccounts,
                ApplicationDbContext.CurrencyInventoryAccountCode);
            var shortLiabilityAccount = RequireSystemAccount(
                systemAccounts,
                ApplicationDbContext.CurrencySaleLiabilityAccountCode);
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

        if (!MoneyExchangeMode.UsesPartyLedger(exchange.OperationType) && !MoneyExchangeMode.IsCorrespondent(exchange.OperationType))
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

        var formattedRate = AmountValueHelper.Format(quotation.Rate, 8);

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
        if (MoneyExchangeMode.IsCorrespondent(operationType) &&
            (fromAccount.AccountType != "Correspondent" || toAccount.AccountType != "Correspondent"))
            throw new InvalidOperationException("برای تبدیل مانده نمایندگی، حساب فروش و دریافت باید حساب نمایندگی فعال باشد.");
        if (MoneyExchangeMode.IsCorrespondent(operationType) &&
            !await _context.Correspondents.AnyAsync(x => x.Id==fromAccount.CorrespondentId && !x.IsArchived))
            throw new InvalidOperationException("نمایندگی مبدأ معتبر و فعال نیست.");
        if (MoneyExchangeMode.IsCorrespondent(operationType) &&
            !await _context.Correspondents.AnyAsync(x => x.Id==toAccount.CorrespondentId && !x.IsArchived))
            throw new InvalidOperationException("نمایندگی مقصد معتبر و فعال نیست.");

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

    private async Task ValidateLinkedHawalaAsync(long? id,long fromAccount,long toAccount,long currency,decimal amount,string mode,long? exchangeId=null)
    {
        if(!id.HasValue) return;
        var h=await (_context.Database.IsNpgsql()
            ? _context.Hawalas.FromSqlInterpolated($"SELECT * FROM public.\"Hawalas\" WHERE \"TenantId\"={_context.CurrentTenantId} AND \"Id\"={id} FOR UPDATE")
            : _context.Hawalas.FromSqlInterpolated($"SELECT * FROM dbo.Hawalas WITH(UPDLOCK,HOLDLOCK) WHERE TenantId={_context.CurrentTenantId} AND Id={id}"))
            .AsNoTracking().SingleOrDefaultAsync(x=>!x.IsDeleted && x.Status!="Cancel");
        if(h==null || h.HawalaType is not ("HawalaReceive" or "HawalaSend") || !h.CorrespondentId.HasValue || !MoneyExchangeMode.IsCorrespondent(mode)) throw new InvalidOperationException("حواله دریافتی یا ارسالی معتبر نمایندگی انتخاب کنید.");
        var expectedCurrency=h.HawalaType=="HawalaSend"?h.ToCurrencyId:h.FromCurrencyId;
        var expectedAmount=h.HawalaType=="HawalaSend"?h.ToAmount??h.FromAmount:h.FromAmount;
        var expectedMode=h.HawalaType=="HawalaSend"?MoneyExchangeMode.CorrespondentCredit:MoneyExchangeMode.CorrespondentDebt;
        if(currency!=expectedCurrency || amount!=expectedAmount || mode!=expectedMode || await _context.Accounts.CountAsync(x=>(x.Id==fromAccount || x.Id==toAccount) && x.CorrespondentId==h.CorrespondentId && !x.IsArchived)!=(fromAccount==toAccount?1:2))
            throw new InvalidOperationException("ارز، مبلغ، جهت و حساب‌های تبدیل باید مطابق حواله انتخاب‌شده باشند.");
        if(await _context.MoneyExchangeOperations.AnyAsync(x=>x.LinkedHawalaId==id && !x.IsDeleted && x.Id!=exchangeId)) throw new InvalidOperationException("این حواله قبلاً تبدیل شده؛ ابتدا تبدیل قبلی را حذف کنید.");
        if(await _context.CorrespondentSettlementConversionHawalas.AnyAsync(x=>x.HawalaId==id)) throw new InvalidOperationException("این حواله به ارز توافقی تبدیل شده؛ ابتدا آن تبدیل را برگشت دهید تا حسابداری تکراری ثبت نشود.");
        if(await _context.AgentPaymentBillItems.AnyAsync(x=>x.HawalaId==id && x.IsActive)) throw new InvalidOperationException("حواله در بیل پرداخت عامل قرار دارد؛ ابتدا بیل پرداخت‌نشده را لغو کنید.");
        // Invalidate any in-flight editor's old rowversion without changing the remittance.
        if(_context.Database.IsNpgsql())await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Hawalas\" SET \"Status\"=\"Status\" WHERE \"TenantId\"={_context.CurrentTenantId} AND \"Id\"={id}");
        else await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Hawalas SET Status=Status WHERE TenantId={_context.CurrentTenantId} AND Id={id}");
        var tracked=_context.ChangeTracker.Entries<Hawala>().FirstOrDefault(x=>x.Entity.Id==id && x.State==EntityState.Unchanged);
        if(tracked!=null) await tracked.ReloadAsync();
    }

    private async Task LockCorrespondentAccountsAsync(string mode, params long[] accountIds)
    {
        if (!MoneyExchangeMode.IsCorrespondent(mode)) return;
        var ids=accountIds.Distinct().OrderBy(x=>x).ToArray();
        var owners=await _context.Accounts.AsNoTracking().Where(x=>ids.Contains(x.Id) && x.CorrespondentId.HasValue)
            .Select(x=>x.CorrespondentId!.Value).Distinct().OrderBy(x=>x).ToListAsync();
        foreach(var owner in owners) await HawalaExchange.Infrastructure.Services.CorrespondentCommissionPeriodLock.AcquireAsync(_context,owner);
        foreach(var id in ids) await (_context.Database.IsNpgsql()
            ? _context.Accounts.FromSqlInterpolated($"SELECT * FROM public.\"Accounts\" WHERE \"TenantId\"={_context.CurrentTenantId} AND \"Id\"={id} FOR UPDATE")
            : _context.Accounts.FromSqlRaw("SELECT * FROM dbo.Accounts WITH(UPDLOCK,HOLDLOCK)"))
            .AsNoTracking().Where(x=>x.Id==id).Select(x=>x.Id).FirstOrDefaultAsync();
    }

    private async Task ValidateCorrespondentPeriodAsync(MoneyExchangeOperation exchange)
    {
        if (!MoneyExchangeMode.IsCorrespondent(exchange.OperationType)) return;
        var owners=_context.Accounts.Where(x=>x.Id==exchange.FromAccountId || x.Id==exchange.ToAccountId)
            .Where(x=>x.CorrespondentId.HasValue).Select(x=>x.CorrespondentId!.Value);
        if(await _context.CorrespondentAccountPeriods.AnyAsync(x=>owners.Contains(x.CorrespondentId) && x.PeriodTo>=exchange.ExchangeDate))
            throw new InvalidOperationException("ثبت یا تغییر تبدیل در دوره بسته مجاز نیست؛ تبدیل جدید را در دوره باز ثبت کنید.");
    }

    private async Task ValidateProfitCurrencyAsync(long profitCurrencyId)
    {
        if (profitCurrencyId <= 0 ||
            !await _context.Currencies.AnyAsync(x => x.Id == profitCurrencyId && x.IsActive))
            throw new InvalidOperationException("ارز اصلی محاسبه مفاد و ضرر معتبر نیست.");
    }

    private async Task<long> ResolveDefaultProfitCurrencyIdAsync()
    {
        var configuredCurrencyId = await _context.CompanySettings
            .AsNoTracking()
            .Select(x => x.DefaultProfitCurrencyId)
            .FirstOrDefaultAsync();

        if (configuredCurrencyId.HasValue)
        {
            if (await _context.Currencies.AnyAsync(x =>
                    x.Id == configuredCurrencyId.Value &&
                    x.IsActive))
            {
                return configuredCurrencyId.Value;
            }

            throw new InvalidOperationException(
                "ارز اصلی محاسبه مفاد و ضرر در تنظیمات غیرفعال یا نامعتبر است.");
        }

        var fallbackCurrencyId = await _context.Currencies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Code == "AFN")
            .ThenBy(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync();

        return fallbackCurrencyId
            ?? throw new InvalidOperationException(
                "ابتدا ارز اصلی محاسبه مفاد و ضرر را در تنظیمات شرکت انتخاب کنید.");
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
        if (operationType is not ("Customer" or "Treasury") && !MoneyExchangeMode.IsCorrespondent(operationType))
            throw new InvalidOperationException("نوع تبدیل باید مشتری، نمایندگی یا سرمایه صراف باشد.");
        if (profitCurrencyId <= 0)
            throw new InvalidOperationException("ارز اصلی محاسبه مفاد و ضرر در تنظیمات مشخص نشده است.");
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
            CorrespondentRealizedProfit = await query.Where(x => x.OperationType == MoneyExchangeMode.CorrespondentCredit || x.OperationType == MoneyExchangeMode.CorrespondentDebt)
                .SumAsync(x => x.RealizedProfit),
            DeferredOperationCount = await query.CountAsync(x =>
                x.ProfitStatus == "Deferred" || x.ProfitStatus == "PartiallyDeferred")
        };
    }

    private long GetCurrentUserId() => _context.RequireCurrentUserId();
}
