using System.Globalization;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class FinancialReportService : IFinancialReportService
{
    private static readonly string[] ValuationMemoAccountCodes = ["1201", "2101"];
    private readonly ApplicationDbContext _context;

    public FinancialReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime asOfDate)
    {
        var endExclusive = asOfDate.Date.AddDays(1);
        var reporting = await GetReportingContextAsync(endExclusive);
        var entries = await GetLedgerEntriesAsync(endExclusive);
        return BuildBalanceSheet(entries, reporting, asOfDate.Date);
    }

    public async Task<ProfitLossStatementDto> GetProfitLossStatementAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        ValidateRange(fromDate, toDate);
        var endExclusive = toDate.Date.AddDays(1);
        var reporting = await GetReportingContextAsync(endExclusive);
        var openingReporting = await GetReportingContextAsync(fromDate.Date, reporting.Currency.Id);
        var entries = await GetLedgerEntriesAsync(endExclusive);
        return BuildProfitLoss(
            entries,
            reporting,
            fromDate.Date,
            toDate.Date,
            openingReporting);
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(
        DateTime fromDate,
        DateTime toDate,
        long reportingCurrencyId)
    {
        ValidateRange(fromDate, toDate);
        var start = fromDate.Date;
        var endExclusive = toDate.Date.AddDays(1);
        var reporting = await GetReportingContextAsync(endExclusive, reportingCurrencyId);
        var openingReporting = await GetReportingContextAsync(start, reportingCurrencyId);
        var entries = await GetLedgerEntriesAsync(endExclusive);
        var rangeEntries = entries
            .Where(x => x.CreatedAt >= start && x.CreatedAt < endExclusive)
            .ToList();

        var hawalas = await _context.Hawalas
            .AsNoTracking()
            .Where(x => x.CreatedAt >= start &&
                        x.CreatedAt < endExclusive &&
                        x.Status != "Cancel")
            .Select(x => new { x.CreatedAt })
            .ToListAsync();

        var exchanges = await _context.MoneyExchangeOperations
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.ExchangeDate >= start &&
                        x.ExchangeDate < endExclusive)
            .Select(x => new { x.ExchangeDate })
            .ToListAsync();

        var accountOperations = await _context.AccountMoneyOperations
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.OperationDate >= start &&
                        x.OperationDate < endExclusive)
            .Select(x => new { x.OperationDate })
            .ToListAsync();

        var expenses = await _context.Expenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.ExpenseDate >= start &&
                        x.ExpenseDate < endExclusive)
            .Select(x => new { x.ExpenseDate })
            .ToListAsync();

        var capitalInvestments = await _context.CapitalInvestments
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.InvestmentDate >= start &&
                        x.InvestmentDate < endExclusive)
            .Select(x => new { x.InvestmentDate })
            .ToListAsync();

        var result = new DashboardSummaryDto
        {
            ReportingCurrencyCode = reporting.Currency.Code,
            HawalaCount = hawalas.Count,
            ExchangeCount = exchanges.Count,
            AccountOperationCount = accountOperations.Count,
            ExpenseCount = expenses.Count,
            CapitalInvestmentCount = capitalInvestments.Count
        };

        result.TotalActivityCount =
            result.HawalaCount +
            result.ExchangeCount +
            result.AccountOperationCount +
            result.ExpenseCount +
            result.CapitalInvestmentCount;

        result.LastActivityAt = new DateTime?[]
            {
                hawalas.Select(x => (DateTime?)x.CreatedAt).Max(),
                exchanges.Select(x => (DateTime?)x.ExchangeDate).Max(),
                accountOperations.Select(x => (DateTime?)x.OperationDate).Max(),
                expenses.Select(x => (DateTime?)x.ExpenseDate).Max(),
                capitalInvestments.Select(x => (DateTime?)x.InvestmentDate).Max()
            }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty()
            .Max();

        if (result.LastActivityAt == default)
            result.LastActivityAt = null;

        var profitLoss = BuildProfitLoss(
            entries,
            reporting,
            start,
            toDate.Date,
            openingReporting);
        result.NetProfit = profitLoss.NetProfit;
        result.Warnings.AddRange(profitLoss.Header.Warnings);

        result.Rates = await GetLatestRatesAsync(reporting.Currency.Id, endExclusive);
        BuildDashboardDebtorsAndLiquidity(entries, reporting, result);
        BuildDashboardSeries(
            start,
            toDate.Date,
            hawalas.Select(x => x.CreatedAt),
            exchanges.Select(x => x.ExchangeDate),
            accountOperations.Select(x => x.OperationDate),
            expenses.Select(x => x.ExpenseDate),
            capitalInvestments.Select(x => x.InvestmentDate),
            rangeEntries,
            reporting,
            result);

        result.Warnings = result.Warnings.Distinct().ToList();
        return result;
    }

    private BalanceSheetDto BuildBalanceSheet(
        IReadOnlyCollection<LedgerEntry> entries,
        ReportingContext reporting,
        DateTime asOfDate)
    {
        var result = new BalanceSheetDto
        {
            Header = CreateHeader(reporting, asOfDate, asOfDate)
        };

        var balances = GetConvertedBalances(
            entries.Where(x => !ValuationMemoAccountCodes.Contains(x.Account!.AccountCode)),
            reporting,
            result.Header.Warnings);

        var cash = DebitBalance(balances, "Cash");
        var bank = DebitBalance(balances, "Bank");
        var customerReceivables = DebitBalance(balances, "Customer");
        var correspondentReceivables = DebitBalance(balances, "Correspondent");
        var otherAssets = DebitBalance(balances, "Asset");
        var negativeCashAndBank = CreditBalance(balances, "Cash", "Bank");

        var customerPayables = CreditBalance(balances, "Customer");
        var correspondentPayables = CreditBalance(balances, "Correspondent");
        var otherLiabilities = CreditBalance(balances, "Liability") + negativeCashAndBank;

        var yearStart = new DateTime(asOfDate.Year, 1, 1);
        var retainedProfit = CalculateNetProfit(
            entries.Where(x => x.CreatedAt < yearStart),
            reporting,
            result.Header.Warnings);
        var currentProfit = CalculateNetProfit(
            entries.Where(x => x.CreatedAt >= yearStart),
            reporting,
            result.Header.Warnings);
        var capital = CreditBalance(balances, "Equity") +
                      CalculateHistoricalCapitalAdjustment(reporting, result.Header.Warnings);

        result.TotalAssets =
            cash +
            bank +
            customerReceivables +
            correspondentReceivables +
            otherAssets;
        result.TotalLiabilities =
            customerPayables +
            correspondentPayables +
            otherLiabilities;

        // Translation can produce a difference when accounts are kept in several currencies.
        // This line makes the statement balance while keeping the difference explicit.
        result.UnrealizedExchangeAdjustment =
            result.TotalAssets -
            result.TotalLiabilities -
            capital -
            retainedProfit -
            currentProfit;
        result.TotalEquity =
            capital +
            retainedProfit +
            currentProfit +
            result.UnrealizedExchangeAdjustment;
        result.TotalLiabilitiesAndEquity = result.TotalLiabilities + result.TotalEquity;

        result.AssetLines =
        [
            Section("الف", "دارایی‌ها"),
            Line("1", "پول نقد", cash),
            Line("2", "پول نقد در بانک", bank),
            Line("3", "حسابات قابل حصول از مشتریان", customerReceivables),
            Line("4", "حسابات قابل حصول از نمایندگان", correspondentReceivables),
            Line("5", "پیش‌پرداخت‌ها و سایر دارایی‌ها", otherAssets),
            Total("6", "مجموع دارایی‌های جاری", result.TotalAssets),
            Section("ب", "دارایی‌های ثابت"),
            Line("7", "دارایی‌های ثابت ملموس", 0),
            Line("8", "دارایی‌های غیرملموس", 0),
            Line("9", "استهلاک انباشته", 0),
            GrandTotal("10", "مجموع دارایی‌ها", result.TotalAssets)
        ];

        result.LiabilityAndEquityLines =
        [
            Section("ج", "بدهی‌ها"),
            Line("11", "حسابات قابل پرداخت به مشتریان", customerPayables),
            Line("12", "حسابات قابل پرداخت به نمایندگان", correspondentPayables),
            Line("13", "مالیات قابل پرداخت", 0),
            Line("14", "سایر بدهی‌ها", otherLiabilities),
            Total("15", "مجموع بدهی‌ها", result.TotalLiabilities),
            Section("د", "سرمایه"),
            Line("16", "سرمایه مالک/شرکا", capital),
            Line("17", "مفاد یا ضرر سال‌های قبل", retainedProfit),
            Line("18", "مفاد یا ضرر دوره جاری", currentProfit),
            Line("19", "تعدیلات ارزی تحقق‌نیافته", result.UnrealizedExchangeAdjustment),
            Total("20", "مجموع سرمایه", result.TotalEquity),
            GrandTotal("21", "مجموع بدهی‌ها و سرمایه", result.TotalLiabilitiesAndEquity)
        ];

        return result;
    }

    private ProfitLossStatementDto BuildProfitLoss(
        IReadOnlyCollection<LedgerEntry> entries,
        ReportingContext reporting,
        DateTime fromDate,
        DateTime toDate,
        ReportingContext? openingReporting = null)
    {
        var endExclusive = toDate.Date.AddDays(1);
        var periodEntries = entries
            .Where(x => x.CreatedAt >= fromDate.Date && x.CreatedAt < endExclusive)
            .ToList();
        var warnings = new List<string>();

        decimal Income(params string[] terms) => SumByAccount(
            periodEntries,
            reporting,
            "Income",
            creditNormal: true,
            warnings,
            terms);

        decimal Expense(params string[] terms) => SumByAccount(
            periodEntries,
            reporting,
            "Expense",
            creditNormal: false,
            warnings,
            terms);

        var commissionIncome = SumByCode(
            periodEntries, reporting, "3001", creditNormal: true, warnings);
        var exchangeIncome = SumByCode(
            periodEntries, reporting, "3002", creditNormal: true, warnings);
        var allIncome = Income();
        var otherIncome = allIncome - commissionIncome - exchangeIncome;

        var depreciation = Expense("استهلاک", "depreciation");
        var staff = Expense("معاش", "حقوق", "کارمند", "staff", "salary");
        var communication = Expense("مخابرات", "تیلفون", "انترنت", "phone", "internet");
        var rent = Expense("کرایه", "rent");
        var food = Expense("اعاشه", "غذا", "food");
        var office = Expense("دفتر", "قرطاسیه", "office", "stationery");
        var insurance = Expense("بیمه", "insurance");
        var license = Expense("جواز", "license");
        var tax = Expense("مالیات", "tax");
        var revaluation = Expense("نوسان", "تعدیل ارز", "revaluation");
        var allExpenses = Expense();
        var categorizedExpenses =
            depreciation + staff + communication + rent + food +
            office + insurance + license + tax + revaluation;
        var otherExpenses = allExpenses - categorizedExpenses;

        var result = new ProfitLossStatementDto
        {
            Header = CreateHeader(reporting, fromDate.Date, toDate.Date),
            GrossRevenue = allIncome,
            OperatingExpenses = allExpenses - tax,
            TaxExpense = tax
        };

        result.Header.Warnings.AddRange(warnings);
        result.NetProfit = result.GrossRevenue - result.OperatingExpenses - result.TaxExpense;

        var startSheet = BuildBalanceSheet(
            entries.Where(x => x.CreatedAt < fromDate.Date).ToList(),
            openingReporting ?? reporting,
            fromDate.Date.AddDays(-1));
        var endSheet = BuildBalanceSheet(
            entries.Where(x => x.CreatedAt < endExclusive).ToList(),
            reporting,
            toDate.Date);
        result.UnrealizedExchangeGainLoss =
            endSheet.UnrealizedExchangeAdjustment -
            startSheet.UnrealizedExchangeAdjustment;
        result.ComprehensiveProfit = result.NetProfit + result.UnrealizedExchangeGainLoss;

        result.Header.Warnings.AddRange(startSheet.Header.Warnings);
        result.Header.Warnings.AddRange(endSheet.Header.Warnings);
        result.Header.Warnings = result.Header.Warnings.Distinct().ToList();

        result.Lines =
        [
            Section("الف", "عواید"),
            Line("1", "عواید کمیسیون حواله", commissionIncome),
            Line("2", "عواید تبدیل پول", exchangeIncome),
            Line("3", "سایر عواید", otherIncome),
            Total("4", "مجموع عواید ناخالص", result.GrossRevenue),
            Section("ب", "مصارف عملیاتی"),
            Line("5", "مصارف استهلاک", depreciation),
            Line("6", "مصارف کارمندان و معاشات", staff),
            Line("7", "مصارف مخابرات و انترنت", communication),
            Line("8", "کرایه", rent),
            Line("9", "اعاشه و غذا", food),
            Line("10", "لوازم و مصارف دفتر", office),
            Line("11", "بیمه", insurance),
            Line("12", "جواز فعالیت", license),
            Line("13", "ضرر ناشی از تعدیلات ارزی", revaluation),
            Line("14", "سایر مصارف", otherExpenses),
            Total("15", "مجموع مصارف عملیاتی", result.OperatingExpenses),
            Line("16", "مفاد قبل از مالیات", result.GrossRevenue - result.OperatingExpenses),
            Line("17", "مالیات", result.TaxExpense),
            GrandTotal("18", "مفاد یا ضرر خالص دوره", result.NetProfit),
            Line("19", "مفاد یا ضرر تحقق‌نیافته ناشی از اسعار", result.UnrealizedExchangeGainLoss),
            GrandTotal("20", "مفاد یا ضرر جامع دوره", result.ComprehensiveProfit)
        ];

        return result;
    }

    private void BuildDashboardDebtorsAndLiquidity(
        IReadOnlyCollection<LedgerEntry> entries,
        ReportingContext reporting,
        DashboardSummaryDto result)
    {
        var warnings = new List<string>();
        var balances = GetConvertedBalances(entries, reporting, warnings);
        var counterpartyBalances = balances
            .Where(x => TypeIs(x.AccountType, "Customer") ||
                        TypeIs(x.AccountType, "Correspondent"))
            .ToList();

        result.TopDebtors = counterpartyBalances
            .Where(x => x.DebitBalance > 0)
            .GroupBy(x => new { x.AccountId, x.AccountName, x.AccountType })
            .Select(x => new DashboardDebtorDto
            {
                AccountId = x.Key.AccountId,
                Name = x.Key.AccountName,
                Initial = string.IsNullOrWhiteSpace(x.Key.AccountName)
                    ? "؟"
                    : x.Key.AccountName.Trim()[..1],
                AccountTypeName = TypeIs(x.Key.AccountType, "Correspondent")
                    ? "نمایندگی"
                    : "مشتری",
                Amount = x.Sum(v => v.DebitBalance)
            })
            .OrderByDescending(x => x.Amount)
            .Take(10)
            .ToList();

        result.TopCreditors = counterpartyBalances
            .Where(x => x.CreditBalance > 0)
            .GroupBy(x => new { x.AccountId, x.AccountName, x.AccountType })
            .Select(x => new DashboardDebtorDto
            {
                AccountId = x.Key.AccountId,
                Name = x.Key.AccountName,
                Initial = string.IsNullOrWhiteSpace(x.Key.AccountName)
                    ? "؟"
                    : x.Key.AccountName.Trim()[..1],
                AccountTypeName = TypeIs(x.Key.AccountType, "Correspondent")
                    ? "نمایندگی"
                    : "مشتری",
                Amount = x.Sum(v => v.CreditBalance)
            })
            .OrderByDescending(x => x.Amount)
            .Take(10)
            .ToList();

        result.TotalCustomerReceivables = balances
            .Where(x => TypeIs(x.AccountType, "Customer"))
            .Sum(x => x.DebitBalance);
        result.TotalCounterpartyReceivables = counterpartyBalances.Sum(x => x.DebitBalance);
        result.TotalCounterpartyPayables = counterpartyBalances.Sum(x => x.CreditBalance);

        var liquidity = balances
            .Where(x => (TypeIs(x.AccountType, "Cash") || TypeIs(x.AccountType, "Bank")) &&
                        x.DebitBalance > 0)
            .GroupBy(x => new { x.AccountType, x.AccountName, x.CurrencyCode })
            .Select(x => new DashboardLiquidityDto
            {
                AccountType = x.Key.AccountType,
                AccountName = x.Key.AccountName,
                CurrencyCode = x.Key.CurrencyCode,
                Amount = x.Sum(v => v.DebitBalance)
            })
            .OrderByDescending(x => x.Amount)
            .ToList();
        var liquidityTotal = liquidity.Sum(x => x.Amount);
        foreach (var item in liquidity)
        {
            item.Percentage = liquidityTotal == 0
                ? 0
                : decimal.Round(item.Amount / liquidityTotal * 100m, 1);
        }

        result.Liquidity = liquidity;
        result.Warnings.AddRange(warnings);
    }

    private void BuildDashboardSeries(
        DateTime fromDate,
        DateTime toDate,
        IEnumerable<DateTime> hawalaDates,
        IEnumerable<DateTime> exchangeDates,
        IEnumerable<DateTime> accountOperationDates,
        IEnumerable<DateTime> expenseDates,
        IEnumerable<DateTime> capitalDates,
        IReadOnlyCollection<LedgerEntry> rangeEntries,
        ReportingContext reporting,
        DashboardSummaryDto result)
    {
        var activityDates = hawalaDates
            .Concat(exchangeDates)
            .Concat(accountOperationDates)
            .Concat(expenseDates)
            .Concat(capitalDates)
            .Select(x => x.Date)
            .ToList();

        var visibleStart = toDate.AddDays(-6) > fromDate
            ? toDate.AddDays(-6)
            : fromDate;
        for (var date = visibleStart; date <= toDate; date = date.AddDays(1))
        {
            result.DailyActivities.Add(new DashboardSeriesPointDto
            {
                Date = date,
                Label = ToPersianMonthDay(date),
                Value = activityDates.Count(x => x == date)
            });
        }

        for (var index = 3; index >= 0; index--)
        {
            var weekEnd = toDate.AddDays(-(index * 7));
            var weekStart = weekEnd.AddDays(-6);
            if (weekStart < fromDate)
                weekStart = fromDate;

            var warnings = new List<string>();
            var profit = CalculateNetProfit(
                rangeEntries.Where(x =>
                    x.CreatedAt >= weekStart &&
                    x.CreatedAt < weekEnd.AddDays(1)),
                reporting,
                warnings);
            result.Warnings.AddRange(warnings);
            result.WeeklyProfit.Add(new DashboardSeriesPointDto
            {
                Date = weekEnd,
                Label = $"هفته {4 - index}",
                Value = profit
            });
        }
    }

    private async Task<List<DashboardRateDto>> GetLatestRatesAsync(
        long reportingCurrencyId,
        DateTime endExclusive)
    {
        var rates = await _context.ExchangeRates
            .AsNoTracking()
            .Include(x => x.FromCurrency)
            .Include(x => x.ToCurrency)
            .Where(x =>
                x.EffectiveDate < endExclusive &&
                (x.FromCurrencyId == reportingCurrencyId ||
                 x.ToCurrencyId == reportingCurrencyId))
            .ToListAsync();

        return rates
            .GroupBy(x => new { x.FromCurrencyId, x.ToCurrencyId })
            .Select(x => x.OrderByDescending(v => v.EffectiveDate).First())
            .OrderBy(x => x.FromCurrency!.Code)
            .ThenBy(x => x.ToCurrency!.Code)
            .Select(x => new DashboardRateDto
            {
                Pair = $"{x.FromCurrency!.Code}/{x.ToCurrency!.Code}",
                BuyRate = x.BuyRate,
                SellRate = x.SellRate,
                EffectiveDate = x.EffectiveDate
            })
            .ToList();
    }

    private async Task<ReportingContext> GetReportingContextAsync(
        DateTime endExclusive,
        long? currencyId = null)
    {
        var setting = await _context.CompanySettings
            .AsNoTracking()
            .Include(x => x.DefaultProfitCurrency)
            .FirstOrDefaultAsync();

        var targetCurrencyId = currencyId ?? setting?.DefaultProfitCurrencyId;
        var currency = targetCurrencyId.HasValue
            ? await _context.Currencies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == targetCurrencyId.Value && x.IsActive)
            : null;
        currency ??= await _context.Currencies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Code == "AFN")
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync();

        if (currency == null)
            throw new InvalidOperationException("برای گزارش مالی هیچ ارز فعال تعریف نشده است.");

        var rates = await _context.ExchangeRates
            .AsNoTracking()
            .Where(x => x.EffectiveDate < endExclusive)
            .OrderByDescending(x => x.EffectiveDate)
            .ToListAsync();
        var currencies = await _context.Currencies.AsNoTracking().ToListAsync();
        var operations = await _context.MoneyExchangeOperations
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.ExchangeDate < endExclusive &&
                        x.ExchangeRate > 0)
            .OrderByDescending(x => x.ExchangeDate)
            .ToListAsync();
        var capitalInvestments = await _context.CapitalInvestments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.InvestmentDate < endExclusive)
            .OrderBy(x => x.InvestmentDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        return new ReportingContext(
            setting?.CompanyName ?? "نام شرکت",
            setting?.LogoPath,
            currency,
            currencies,
            rates,
            operations,
            capitalInvestments);
    }

    private async Task<List<LedgerEntry>> GetLedgerEntriesAsync(DateTime endExclusive)
    {
        return await _context.LedgerEntries
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Where(x => x.CreatedAt < endExclusive)
            .ToListAsync();
    }

    private static List<ConvertedBalance> GetConvertedBalances(
        IEnumerable<LedgerEntry> entries,
        ReportingContext reporting,
        ICollection<string> warnings)
    {
        var result = new List<ConvertedBalance>();
        foreach (var group in entries.GroupBy(x => new
                 {
                     x.AccountId,
                     x.CurrencyId,
                     x.Account!.AccountName,
                     x.Account.AccountType,
                     x.Currency!.Code
                 }))
        {
            var debit = group.Sum(x => x.BadehKar);
            var credit = group.Sum(x => x.TalabKar);
            var conversion = reporting.TryConvert(1m, group.Key.CurrencyId);
            if (!conversion.HasValue)
            {
                warnings.Add(
                    $"نرخ واقعی {group.Key.Code}/{reporting.Currency.Code} تا تاریخ گزارش موجود نیست؛ ماندهٔ این ارز در جمع گزارش وارد نشده است.");
                continue;
            }

            var convertedDebit = debit * conversion.Value;
            var convertedCredit = credit * conversion.Value;
            result.Add(new ConvertedBalance(
                group.Key.AccountId,
                group.Key.AccountName,
                group.Key.AccountType,
                group.Key.Code,
                convertedDebit,
                convertedCredit));
        }

        return result;
    }

    private static decimal CalculateNetProfit(
        IEnumerable<LedgerEntry> entries,
        ReportingContext reporting,
        ICollection<string> warnings)
    {
        decimal total = 0;
        foreach (var entry in entries.Where(x =>
                     TypeIs(x.Account!.AccountType, "Income") ||
                     TypeIs(x.Account.AccountType, "Expense")))
        {
            var convertedDebit = reporting.TryConvert(entry.BadehKar, entry.CurrencyId);
            var convertedCredit = reporting.TryConvert(entry.TalabKar, entry.CurrencyId);
            if (!convertedDebit.HasValue || !convertedCredit.HasValue)
            {
                warnings.Add(
                    $"نرخ واقعی {entry.Currency!.Code}/{reporting.Currency.Code} تا تاریخ گزارش موجود نیست؛ مبلغ این ارز در مفاد و ضرر وارد نشده است.");
                continue;
            }

            total += convertedCredit.Value - convertedDebit.Value;
        }

        return total;
    }

    private static decimal CalculateHistoricalCapitalAdjustment(
        ReportingContext reporting,
        ICollection<string> warnings)
    {
        decimal adjustment = 0;
        foreach (var investment in reporting.CapitalInvestments)
        {
            var currentValue = reporting.TryConvert(investment.Amount, investment.CurrencyId);
            var historicalValue = investment.ProfitCurrencyId.HasValue &&
                                  investment.ProfitCurrencyAmount.HasValue
                ? reporting.TryConvertAt(
                    investment.ProfitCurrencyAmount.Value,
                    investment.ProfitCurrencyId.Value,
                    investment.InvestmentDate)
                : null;

            if (!currentValue.HasValue || !historicalValue.HasValue)
            {
                warnings.Add(
                    $"ارزش تاریخی سرمایه شماره {investment.Id} قابل تبدیل به {reporting.Currency.Code} نیست؛ سرمایه با نرخ جاری نمایش داده شد.");
                continue;
            }

            adjustment += historicalValue.Value - currentValue.Value;
        }

        return adjustment;
    }

    private static decimal SumByCode(
        IEnumerable<LedgerEntry> entries,
        ReportingContext reporting,
        string accountCode,
        bool creditNormal,
        ICollection<string> warnings)
    {
        return SumEntries(
            entries.Where(x => x.Account!.AccountCode == accountCode),
            reporting,
            creditNormal,
            warnings);
    }

    private static decimal SumByAccount(
        IEnumerable<LedgerEntry> entries,
        ReportingContext reporting,
        string accountType,
        bool creditNormal,
        ICollection<string> warnings,
        params string[] terms)
    {
        var selected = entries.Where(x => TypeIs(x.Account!.AccountType, accountType));
        if (terms.Length > 0)
        {
            selected = selected.Where(x => terms.Any(term =>
                x.Account!.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return SumEntries(selected, reporting, creditNormal, warnings);
    }

    private static decimal SumEntries(
        IEnumerable<LedgerEntry> entries,
        ReportingContext reporting,
        bool creditNormal,
        ICollection<string> warnings)
    {
        decimal total = 0;
        foreach (var entry in entries)
        {
            var conversion = reporting.TryConvert(1m, entry.CurrencyId);
            if (!conversion.HasValue)
            {
                warnings.Add(
                    $"نرخ واقعی {entry.Currency!.Code}/{reporting.Currency.Code} تا تاریخ گزارش موجود نیست؛ مبلغ این ارز در جمع وارد نشده است.");
                continue;
            }

            var signed = creditNormal
                ? entry.TalabKar - entry.BadehKar
                : entry.BadehKar - entry.TalabKar;
            total += signed * conversion.Value;
        }

        return total;
    }

    private static decimal DebitBalance(
        IEnumerable<ConvertedBalance> balances,
        params string[] accountTypes) =>
        balances
            .Where(x => accountTypes.Any(t => TypeIs(x.AccountType, t)))
            .Sum(x => x.DebitBalance);

    private static decimal CreditBalance(
        IEnumerable<ConvertedBalance> balances,
        params string[] accountTypes) =>
        balances
            .Where(x => accountTypes.Any(t => TypeIs(x.AccountType, t)))
            .Sum(x => x.CreditBalance);

    private static bool TypeIs(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static FinancialReportHeaderDto CreateHeader(
        ReportingContext reporting,
        DateTime fromDate,
        DateTime toDate) => new()
    {
        CompanyName = reporting.CompanyName,
        LogoPath = reporting.LogoPath,
        BranchName = "همه شعبه‌ها",
        CurrencyCode = reporting.Currency.Code,
        CurrencyName = reporting.Currency.Name,
        FromDate = fromDate,
        ToDate = toDate
    };

    private static FinancialStatementLineDto Line(
        string number,
        string description,
        decimal amount) => new()
    {
        Number = number,
        Description = description,
        Amount = amount
    };

    private static FinancialStatementLineDto Section(
        string number,
        string description) => new()
    {
        Number = number,
        Description = description,
        IsSection = true
    };

    private static FinancialStatementLineDto Total(
        string number,
        string description,
        decimal amount) => new()
    {
        Number = number,
        Description = description,
        Amount = amount,
        IsTotal = true
    };

    private static FinancialStatementLineDto GrandTotal(
        string number,
        string description,
        decimal amount) => new()
    {
        Number = number,
        Description = description,
        Amount = amount,
        IsTotal = true,
        IsGrandTotal = true
    };

    private static string ToPersianMonthDay(DateTime date)
    {
        var calendar = new PersianCalendar();
        return $"{calendar.GetMonth(date)}/{calendar.GetDayOfMonth(date)}";
    }

    private static void ValidateRange(DateTime fromDate, DateTime toDate)
    {
        if (fromDate.Date > toDate.Date)
            throw new InvalidOperationException("تاریخ شروع نمی‌تواند بعد از تاریخ ختم باشد.");
    }

    private sealed record ConvertedBalance(
        long AccountId,
        string AccountName,
        string AccountType,
        string CurrencyCode,
        decimal Debit,
        decimal Credit)
    {
        public decimal DebitBalance => Math.Max(Debit - Credit, 0);
        public decimal CreditBalance => Math.Max(Credit - Debit, 0);
    }

    private sealed class ReportingContext
    {
        private readonly IReadOnlyCollection<Currency> _currencies;
        private readonly IReadOnlyCollection<ExchangeRate> _rates;
        private readonly IReadOnlyCollection<MoneyExchangeOperation> _operations;

        public ReportingContext(
            string companyName,
            string? logoPath,
            Currency currency,
            IReadOnlyCollection<Currency> currencies,
            IReadOnlyCollection<ExchangeRate> rates,
            IReadOnlyCollection<MoneyExchangeOperation> operations,
            IReadOnlyCollection<CapitalInvestment> capitalInvestments)
        {
            CompanyName = companyName;
            LogoPath = logoPath;
            Currency = currency;
            _currencies = currencies;
            _rates = rates;
            _operations = operations;
            CapitalInvestments = capitalInvestments;
        }

        public string CompanyName { get; }
        public string? LogoPath { get; }
        public Currency Currency { get; }
        public IReadOnlyCollection<CapitalInvestment> CapitalInvestments { get; }

        public decimal? TryConvert(decimal amount, long sourceCurrencyId)
        {
            return TryConvert(amount, sourceCurrencyId, null);
        }

        public decimal? TryConvertAt(decimal amount, long sourceCurrencyId, DateTime asOf)
        {
            return TryConvert(amount, sourceCurrencyId, asOf);
        }

        private decimal? TryConvert(decimal amount, long sourceCurrencyId, DateTime? asOf)
        {
            if (sourceCurrencyId == Currency.Id)
                return amount;

            var direct = _rates
                .Where(x => x.FromCurrencyId == sourceCurrencyId &&
                             x.ToCurrencyId == Currency.Id &&
                             x.SellRate > 0 &&
                             (!asOf.HasValue || x.EffectiveDate <= asOf.Value))
                .OrderByDescending(x => x.EffectiveDate)
                .FirstOrDefault();
            if (direct != null)
                return amount * direct.SellRate;

            var reverse = _rates
                .Where(x => x.FromCurrencyId == Currency.Id &&
                             x.ToCurrencyId == sourceCurrencyId &&
                             x.BuyRate > 0 &&
                             (!asOf.HasValue || x.EffectiveDate <= asOf.Value))
                .OrderByDescending(x => x.EffectiveDate)
                .FirstOrDefault();
            if (reverse != null)
                return amount / reverse.BuyRate;

            var source = _currencies.FirstOrDefault(x => x.Id == sourceCurrencyId);
            if (source == null)
                return null;

            var operation = _operations.FirstOrDefault(x =>
                (!asOf.HasValue || x.ExchangeDate <= asOf.Value) &&
                ((x.RateBaseCurrencyId == sourceCurrencyId &&
                  x.RateQuoteCurrencyId == Currency.Id) ||
                 (x.RateBaseCurrencyId == Currency.Id &&
                  x.RateQuoteCurrencyId == sourceCurrencyId) ||
                 (x.FromCurrencyId == sourceCurrencyId &&
                  x.ToCurrencyId == Currency.Id) ||
                 (x.FromCurrencyId == Currency.Id &&
                  x.ToCurrencyId == sourceCurrencyId)));
            if (operation == null)
                return null;

            if (operation.RateBaseCurrencyId.HasValue &&
                operation.RateQuoteCurrencyId.HasValue)
            {
                if (operation.RateBaseCurrencyId == sourceCurrencyId &&
                    operation.RateQuoteCurrencyId == Currency.Id)
                    return amount * operation.ExchangeRate;
                if (operation.RateBaseCurrencyId == Currency.Id &&
                    operation.RateQuoteCurrencyId == sourceCurrencyId)
                    return amount / operation.ExchangeRate;
            }

            if (operation.FromCurrencyId == sourceCurrencyId &&
                operation.ToCurrencyId == Currency.Id &&
                operation.FromAmount > 0)
                return amount * operation.ToAmount / operation.FromAmount;
            if (operation.FromCurrencyId == Currency.Id &&
                operation.ToCurrencyId == sourceCurrencyId &&
                operation.ToAmount > 0)
                return amount * operation.FromAmount / operation.ToAmount;

            return null;
        }
    }
}
