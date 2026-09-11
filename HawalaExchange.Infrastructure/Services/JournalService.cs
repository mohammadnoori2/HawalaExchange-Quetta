using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class JournalService : IJournalService
{
    private readonly ApplicationDbContext _context;

    public JournalService(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<DailyJournalDto> GetDailyJournalAsync(DateTime journalDate) =>
        GetJournalAsync(journalDate, journalDate);

    public async Task<DailyJournalDto> GetJournalAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        if (fromDate.Date > toDate.Date)
            throw new InvalidOperationException("تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.");

        var localStart = fromDate.Date;
        var localEnd = toDate.Date.AddDays(1);
        var utcStart = localStart.ToUniversalTime();
        var utcEnd = localEnd.ToUniversalTime();

        var ledgerEntries = await _context.LedgerEntries
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Where(x => x.CreatedAt >= utcStart && x.CreatedAt < utcEnd)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var entries = ledgerEntries.Select(ToEntryDto).ToList();
        var operations = ledgerEntries
            .GroupBy(GetOperationKey)
            .Select(group => BuildOperation(group.Key, group))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.SourceId)
            .ToList();

        await PopulateSourceDetailsAsync(operations);

        var summaries = entries
            .GroupBy(x => new { x.CurrencyId, x.CurrencyCode })
            .Select(group => new DailyJournalCurrencySummaryDto
            {
                CurrencyId = group.Key.CurrencyId,
                CurrencyCode = group.Key.CurrencyCode,
                EntriesCount = group.Count(),
                TotalTalabKar = group.Sum(x => x.TalabKar),
                TotalBadehKar = group.Sum(x => x.BadehKar)
            })
            .OrderBy(x => x.CurrencyCode)
            .ToList();

        return new DailyJournalDto
        {
            JournalDate = localStart,
            FromDate = localStart,
            ToDate = toDate.Date,
            Operations = operations,
            Entries = entries,
            CurrencySummaries = summaries
        };
    }

    public async Task<IReadOnlyList<JournalOperationDto>> GetAccountOperationsAsync(long accountId)
    {
        var accountEntries = await _context.LedgerEntries
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .ToListAsync();

        if (accountEntries.Count == 0)
            return Array.Empty<JournalOperationDto>();

        var operationKeys = accountEntries
            .Select(GetOperationKey)
            .ToHashSet(StringComparer.Ordinal);
        var hawalaIds = accountEntries
            .Where(x => x.HawalaId.HasValue)
            .Select(x => x.HawalaId!.Value)
            .ToHashSet();
        var capitalIds = accountEntries
            .Where(x => x.CapitalInvestmentId.HasValue)
            .Select(x => x.CapitalInvestmentId!.Value)
            .ToHashSet();
        var expenseIds = accountEntries
            .Where(x => x.ExpenseId.HasValue)
            .Select(x => x.ExpenseId!.Value)
            .ToHashSet();
        var accountOperationIds = accountEntries
            .Where(x => x.AccountMoneyOperationId.HasValue)
            .Select(x => x.AccountMoneyOperationId!.Value)
            .ToHashSet();
        var exchangeIds = accountEntries
            .Where(x => x.MoneyExchangeOperationId.HasValue)
            .Select(x => x.MoneyExchangeOperationId!.Value)
            .ToHashSet();
        var transferIds = accountEntries
            .Where(x => x.TransferId.HasValue)
            .Select(x => x.TransferId!.Value)
            .ToHashSet();
        var transactionIds = accountEntries
            .Where(x => x.TransactionId.HasValue)
            .Select(x => x.TransactionId!.Value)
            .ToHashSet();
        var manualEntryIds = accountEntries
            .Where(x =>
                !x.HawalaId.HasValue &&
                !x.CapitalInvestmentId.HasValue &&
                !x.ExpenseId.HasValue &&
                !x.AccountMoneyOperationId.HasValue &&
                !x.MoneyExchangeOperationId.HasValue &&
                !x.TransferId.HasValue &&
                !x.TransactionId.HasValue)
            .Select(x => x.Id)
            .ToHashSet();

        var relatedEntries = await _context.LedgerEntries
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Where(x =>
                manualEntryIds.Contains(x.Id) ||
                (x.HawalaId.HasValue && hawalaIds.Contains(x.HawalaId.Value)) ||
                (x.CapitalInvestmentId.HasValue && capitalIds.Contains(x.CapitalInvestmentId.Value)) ||
                (x.ExpenseId.HasValue && expenseIds.Contains(x.ExpenseId.Value)) ||
                (x.AccountMoneyOperationId.HasValue && accountOperationIds.Contains(x.AccountMoneyOperationId.Value)) ||
                (x.MoneyExchangeOperationId.HasValue && exchangeIds.Contains(x.MoneyExchangeOperationId.Value)) ||
                (x.TransferId.HasValue && transferIds.Contains(x.TransferId.Value)) ||
                (x.TransactionId.HasValue && transactionIds.Contains(x.TransactionId.Value)))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var operations = relatedEntries
            .Where(x => operationKeys.Contains(GetOperationKey(x)))
            .GroupBy(GetOperationKey)
            .Select(group => BuildOperation(group.Key, group))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.SourceId)
            .ToList();

        await PopulateSourceDetailsAsync(operations);
        return operations;
    }

    public async Task<IReadOnlyList<CashDailyBalanceDto>> GetCashDailyBalancesAsync(
        DateTime journalDate)
    {
        var date = journalDate.Date;
        var balances = await EnsureCashDailyBalancesAsync(date);
        if (balances.Count == 0)
            return Array.Empty<CashDailyBalanceDto>();

        var movements = await GetCashMovementsAsync(
            balances.Select(x => x.AccountId).Distinct().ToList(),
            date,
            date.AddDays(1));

        return balances
            .Select(balance =>
            {
                var key = (balance.AccountId, balance.CurrencyId);
                var movement = movements.GetValueOrDefault(key);
                return new CashDailyBalanceDto
                {
                    Id = balance.Id,
                    JournalDate = balance.JournalDate,
                    AccountId = balance.AccountId,
                    AccountCode = balance.Account.AccountCode,
                    AccountName = balance.Account.AccountName,
                    CurrencyId = balance.CurrencyId,
                    CurrencyCode = balance.Currency.Code,
                    DecimalPlaces = balance.Currency.DecimalPlaces,
                    OpeningBalance = balance.OpeningBalance,
                    DailyMovement = movement,
                    CurrentBalance = balance.OpeningBalance + movement,
                    ClosingBalance = balance.ClosingBalance,
                    IsClosed = balance.IsClosed
                };
            })
            .OrderBy(x => x.AccountCode)
            .ThenBy(x => x.CurrencyCode)
            .ToList();
    }

    public async Task<IReadOnlyList<CurrentCashBalanceDto>> GetCurrentCashBalancesAsync()
    {
        var balances = await GetCashDailyBalancesAsync(DateTime.Today);
        return balances
            .Select(x => new CurrentCashBalanceDto
            {
                AccountId = x.AccountId,
                AccountCode = x.AccountCode,
                AccountName = x.AccountName,
                CurrencyId = x.CurrencyId,
                CurrencyCode = x.CurrencyCode,
                Balance = x.CurrentBalance
            })
            .OrderBy(x => x.AccountCode)
            .ThenBy(x => x.CurrencyCode)
            .ToList();
    }

    public async Task SaveCashOpeningBalancesAsync(
        DateTime journalDate,
        IReadOnlyCollection<UpdateCashOpeningBalanceDto> balances)
    {
        if (balances.Count == 0)
            throw new InvalidOperationException("حداقل یک موجودی آغاز روز باید ارسال شود.");
        if (balances.Any(x => x.OpeningBalance < 0))
            throw new InvalidOperationException("موجودی آغاز روز نمی‌تواند منفی باشد.");

        var date = journalDate.Date;
        var existing = await EnsureCashDailyBalancesAsync(date);
        var rows = existing.ToDictionary(x => (x.AccountId, x.CurrencyId));
        var updates = balances
            .GroupBy(x => (x.AccountId, x.CurrencyId))
            .Select(x => x.Last())
            .ToList();

        foreach (var update in updates)
        {
            if (!rows.TryGetValue((update.AccountId, update.CurrencyId), out var row))
                throw new InvalidOperationException("حساب صندوق یا ارز انتخاب‌شده معتبر نیست.");

            row.OpeningBalance = update.OpeningBalance;
            row.ModifiedAt = DateTime.UtcNow;
        }

        var closedRows = updates
            .Select(x => rows[(x.AccountId, x.CurrencyId)])
            .Where(x => x.IsClosed)
            .ToList();
        if (closedRows.Count > 0)
        {
            var movements = await GetCashMovementsAsync(
                closedRows.Select(x => x.AccountId).Distinct().ToList(),
                date,
                date.AddDays(1));
            foreach (var row in closedRows)
            {
                row.ClosingBalance =
                    row.OpeningBalance +
                    movements.GetValueOrDefault((row.AccountId, row.CurrencyId));
            }
        }

        await _context.SaveChangesAsync();

        if (closedRows.Count == 0)
            return;

        var tomorrowRows = await EnsureCashDailyBalancesAsync(date.AddDays(1));
        var tomorrowByKey = tomorrowRows.ToDictionary(x => (x.AccountId, x.CurrencyId));
        foreach (var row in closedRows)
        {
            if (tomorrowByKey.TryGetValue((row.AccountId, row.CurrencyId), out var tomorrow) &&
                !tomorrow.IsClosed)
            {
                tomorrow.OpeningBalance = row.ClosingBalance ?? row.OpeningBalance;
                tomorrow.ModifiedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task CloseCashDayAsync(DateTime journalDate)
    {
        var date = journalDate.Date;
        if (date > DateTime.Today)
            throw new InvalidOperationException("روز آینده را نمی‌توان بست.");

        var balances = await EnsureCashDailyBalancesAsync(date);
        if (balances.Count == 0)
            throw new InvalidOperationException("هیچ حساب صندوق فعالی برای بستن روز وجود ندارد.");

        var movements = await GetCashMovementsAsync(
            balances.Select(x => x.AccountId).Distinct().ToList(),
            date,
            date.AddDays(1));
        var closedAt = DateTime.UtcNow;
        foreach (var balance in balances)
        {
            balance.ClosingBalance =
                balance.OpeningBalance +
                movements.GetValueOrDefault((balance.AccountId, balance.CurrencyId));
            balance.IsClosed = true;
            balance.ClosedAt = closedAt;
            balance.ModifiedAt = closedAt;
        }

        await _context.SaveChangesAsync();

        var tomorrowRows = await EnsureCashDailyBalancesAsync(date.AddDays(1));
        var tomorrowByKey = tomorrowRows.ToDictionary(x => (x.AccountId, x.CurrencyId));
        foreach (var balance in balances)
        {
            if (tomorrowByKey.TryGetValue((balance.AccountId, balance.CurrencyId), out var tomorrow) &&
                !tomorrow.IsClosed)
            {
                tomorrow.OpeningBalance = balance.ClosingBalance ?? balance.OpeningBalance;
                tomorrow.ModifiedAt = closedAt;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task<List<CashDailyBalance>> EnsureCashDailyBalancesAsync(DateTime date)
    {
        date = date.Date;
        var cashAccounts = await _context.Accounts
            .AsNoTracking()
            .Where(x => x.AccountType == "Cash" && !x.IsArchived)
            .OrderBy(x => x.AccountCode)
            .ToListAsync();
        if (cashAccounts.Count == 0)
            return new List<CashDailyBalance>();

        var accountIds = cashAccounts.Select(x => x.Id).ToList();
        var tradedPairs = await _context.LedgerEntries
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId))
            .Select(x => new { x.AccountId, x.CurrencyId })
            .Distinct()
            .ToListAsync();
        if (tradedPairs.Count == 0)
            return new List<CashDailyBalance>();

        var tradedKeys = tradedPairs
            .Select(x => (x.AccountId, x.CurrencyId))
            .ToHashSet();
        var currencyIds = tradedPairs
            .Select(x => x.CurrencyId)
            .Distinct()
            .ToList();
        var currencies = await _context.Currencies
            .AsNoTracking()
            .Where(x => currencyIds.Contains(x.Id))
            .OrderBy(x => x.Code)
            .ToListAsync();
        var accountsById = cashAccounts.ToDictionary(x => x.Id);
        var currenciesById = currencies.ToDictionary(x => x.Id);

        var existing = await _context.CashDailyBalances
            .Where(x =>
                x.JournalDate == date &&
                accountIds.Contains(x.AccountId) &&
                currencyIds.Contains(x.CurrencyId))
            .ToListAsync();
        existing = existing
            .Where(x => tradedKeys.Contains((x.AccountId, x.CurrencyId)))
            .ToList();
        var existingKeys = existing
            .Select(x => (x.AccountId, x.CurrencyId))
            .ToHashSet();

        var hasMissingBalance = tradedKeys.Any(key => !existingKeys.Contains(key));
        if (hasMissingBalance)
        {
            var priorSnapshots = await _context.CashDailyBalances
                .AsNoTracking()
                .Where(x =>
                    x.JournalDate < date &&
                    accountIds.Contains(x.AccountId) &&
                    currencyIds.Contains(x.CurrencyId))
                .OrderByDescending(x => x.JournalDate)
                .ToListAsync();
            var latestPriorByKey = priorSnapshots
                .Where(x => tradedKeys.Contains((x.AccountId, x.CurrencyId)))
                .GroupBy(x => (x.AccountId, x.CurrencyId))
                .ToDictionary(x => x.Key, x => x.First());

            var utcStart = date.ToUniversalTime();
            var priorLedgerEntries = await _context.LedgerEntries
                .AsNoTracking()
                .Where(x => accountIds.Contains(x.AccountId) && x.CreatedAt < utcStart)
                .Select(x => new
                {
                    x.AccountId,
                    x.CurrencyId,
                    x.BadehKar,
                    x.TalabKar,
                    x.CreatedAt
                })
                .ToListAsync();

            foreach (var key in tradedKeys)
            {
                if (existingKeys.Contains(key) ||
                    !accountsById.ContainsKey(key.AccountId) ||
                    !currenciesById.ContainsKey(key.CurrencyId))
                    continue;

                decimal openingBalance;
                if (latestPriorByKey.TryGetValue(key, out var prior))
                {
                    var priorUtcStart = prior.JournalDate.Date.ToUniversalTime();
                    openingBalance = prior.OpeningBalance + priorLedgerEntries
                        .Where(x =>
                            x.AccountId == key.AccountId &&
                            x.CurrencyId == key.CurrencyId &&
                            x.CreatedAt >= priorUtcStart)
                        .Sum(x => x.BadehKar - x.TalabKar);
                }
                else
                {
                    openingBalance = priorLedgerEntries
                        .Where(x =>
                            x.AccountId == key.AccountId &&
                            x.CurrencyId == key.CurrencyId)
                        .Sum(x => x.BadehKar - x.TalabKar);
                }

                _context.CashDailyBalances.Add(new CashDailyBalance
                {
                    JournalDate = date,
                    AccountId = key.AccountId,
                    CurrencyId = key.CurrencyId,
                    OpeningBalance = openingBalance,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        var dailyBalances = await _context.CashDailyBalances
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Where(x =>
                x.JournalDate == date &&
                accountIds.Contains(x.AccountId) &&
                currencyIds.Contains(x.CurrencyId))
            .ToListAsync();
        return dailyBalances
            .Where(x => tradedKeys.Contains((x.AccountId, x.CurrencyId)))
            .OrderBy(x => x.Account.AccountCode)
            .ThenBy(x => x.Currency.Code)
            .ToList();
    }

    private async Task<Dictionary<(long AccountId, long CurrencyId), decimal>>
        GetCashMovementsAsync(
            IReadOnlyCollection<long> accountIds,
            DateTime fromDate,
            DateTime toDate)
    {
        var utcStart = fromDate.Date.ToUniversalTime();
        var utcEnd = toDate.Date.ToUniversalTime();
        var movements = await _context.LedgerEntries
            .AsNoTracking()
            .Where(x =>
                accountIds.Contains(x.AccountId) &&
                x.CreatedAt >= utcStart &&
                x.CreatedAt < utcEnd)
            .GroupBy(x => new { x.AccountId, x.CurrencyId })
            .Select(group => new
            {
                group.Key.AccountId,
                group.Key.CurrencyId,
                Amount = group.Sum(x => x.BadehKar - x.TalabKar)
            })
            .ToListAsync();

        return movements.ToDictionary(
            x => (x.AccountId, x.CurrencyId),
            x => x.Amount);
    }

    private static JournalEntryDto ToEntryDto(LedgerEntry entry) => new()
    {
        Id = entry.Id,
        CreatedAt = entry.CreatedAt,
        SourceType = GetSourceType(entry),
        SourceId = GetSourceId(entry),
        AccountId = entry.AccountId,
        AccountCode = entry.Account?.AccountCode ?? string.Empty,
        AccountName = entry.Account?.AccountName ?? string.Empty,
        IsCashAccount = string.Equals(
            entry.Account?.AccountType,
            "Cash",
            StringComparison.OrdinalIgnoreCase),
        CurrencyId = entry.CurrencyId,
        CurrencyCode = entry.Currency?.Code ?? string.Empty,
        TalabKar = entry.TalabKar,
        BadehKar = entry.BadehKar,
        Description = entry.Description
    };

    private static JournalOperationDto BuildOperation(
        string operationKey,
        IEnumerable<LedgerEntry> sourceEntries)
    {
        var entries = sourceEntries.Select(ToEntryDto)
            .OrderBy(x => x.Id)
            .ToList();
        var first = entries[0];
        var descriptions = entries
            .Select(x => x.Description?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        return new JournalOperationDto
        {
            OperationKey = operationKey,
            CreatedAt = entries.Min(x => x.CreatedAt),
            SourceType = first.SourceType,
            SourceId = first.SourceId,
            DocumentNumber = first.SourceId?.ToString() ?? $"دستی-{first.Id}",
            Description = descriptions.Count == 0 ? "-" : string.Join("؛ ", descriptions),
            AccountNames = entries
                .Select(x => x.AccountName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList(),
            CurrencySummaries = entries
                .GroupBy(x => new { x.CurrencyId, x.CurrencyCode })
                .Select(group => new JournalOperationCurrencySummaryDto
                {
                    CurrencyId = group.Key.CurrencyId,
                    CurrencyCode = group.Key.CurrencyCode,
                    TotalTalabKar = group.Sum(x => x.TalabKar),
                    TotalBadehKar = group.Sum(x => x.BadehKar)
                })
                .OrderBy(x => x.CurrencyCode)
                .ToList(),
            LedgerEntries = entries
        };
    }

    private async Task PopulateSourceDetailsAsync(List<JournalOperationDto> operations)
    {
        await PopulateHawalaDetailsAsync(operations);
        await PopulateExchangeDetailsAsync(operations);
        await PopulateCapitalDetailsAsync(operations);
        await PopulateExpenseDetailsAsync(operations);
        await PopulateAccountOperationDetailsAsync(operations);
        await PopulateTransferDetailsAsync(operations);
        await PopulateTransactionDetailsAsync(operations);

        foreach (var operation in operations.Where(x => x.SourceType == "ثبت دستی"))
        {
            Add(operation, "نوع ثبت", "ثبت مستقیم حسابداری");
            Add(operation, "شناسه ثبت", operation.LedgerEntries[0].Id.ToString());
        }

        foreach (var operation in operations)
            operation.SummarySentence = BuildSummarySentence(operation);
    }

    private async Task PopulateHawalaDetailsAsync(List<JournalOperationDto> operations)
    {
        var ids = SourceIds(operations, "حواله");
        if (ids.Count == 0)
            return;

        var sources = await _context.Hawalas
            .AsNoTracking()
            .Include(x => x.FromCurrency)
            .Include(x => x.ToCurrency)
            .Include(x => x.CommissionCurrency)
            .Include(x => x.AgentCommissionCurrency)
            .Include(x => x.Correspondent)
            .Include(x => x.PaymentLocation)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var operation in operations.Where(x => x.SourceType == "حواله"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            operation.DocumentNumber = source.Number.ToString();
            Add(operation, "شماره حواله", source.Number.ToString());
            Add(operation, "نوع حواله", AppDisplayText.HawalaType(source.HawalaType));
            Add(operation, "وضعیت", AppDisplayText.Status(source.Status, source.HawalaType));
            Add(operation, "فرستنده", source.SenderName);
            Add(operation, "نام پدر فرستنده", source.SenderFatherName);
            Add(operation, "شماره تماس فرستنده", source.SenderPhone);
            Add(operation, "شماره تذکره فرستنده", source.SenderTazkiraNumber);
            Add(operation, "گیرنده", source.ReceiverName);
            Add(operation, "نام پدر گیرنده", source.ReceiverFatherName);
            Add(operation, "شماره تماس گیرنده", source.ReceiverPhone);
            Add(operation, "شماره تذکره گیرنده", source.ReceiverTazkiraNumber);
            Add(operation, "مبلغ ارسالی", Money(source.FromAmount, source.FromCurrency?.Code));
            Add(operation, "مبلغ دریافتی", Money(source.ToAmount, source.ToCurrency?.Code));
            Add(operation, "نرخ تبادله", Number(source.ExchangeRate));
            Add(operation, "کمیسیون", Money(source.CommissionAmount, source.CommissionCurrency?.Code));
            Add(operation, "کمیسیون نماینده", Money(source.AgentCommissionAmount, source.AgentCommissionCurrency?.Code));
            Add(operation, "نماینده", source.Correspondent?.Name);
            Add(operation, "محل پرداخت", source.PaymentLocation?.Name);
            Add(operation, "نمبر متفرقه", source.ReferenceNumber);
            Add(operation, "یادداشت", source.Notes);
        }
    }

    private async Task PopulateExchangeDetailsAsync(List<JournalOperationDto> operations)
    {
        var ids = SourceIds(operations, "تبدیل پول");
        if (ids.Count == 0)
            return;

        var sources = await _context.MoneyExchangeOperations
            .AsNoTracking()
            .Include(x => x.FromAccount)
            .Include(x => x.ToAccount)
            .Include(x => x.FromCurrency)
            .Include(x => x.ToCurrency)
            .Include(x => x.ProfitCurrency)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var operation in operations.Where(x => x.SourceType == "تبدیل پول"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            Add(operation, "نوع عملیات", source.OperationType == "Customer" ? "تبدیل پول مشتری" : "تبدیل پول خود صرافی");
            Add(operation, "تاریخ تبدیل", source.ExchangeDate.ToString("yyyy/MM/dd HH:mm"));
            Add(operation, "حساب ارز فروش", source.FromAccount.AccountName);
            Add(operation, "ارز فروش / مبلغ پرداختی", Money(source.FromAmount, source.FromCurrency.Code));
            Add(operation, "حساب ارز خرید", source.ToAccount.AccountName);
            Add(operation, "ارز خرید / مبلغ دریافتی", Money(source.ToAmount, source.ToCurrency.Code));
            Add(operation, "نرخ تبادله", AmountValueHelper.Format(source.ExchangeRate));
            Add(operation, "کمیسیون", Money(source.CommissionAmount, source.ProfitCurrency?.Code));
            Add(operation, "هزینه خارجی", Money(source.ExternalFeeAmount, source.ProfitCurrency?.Code));
            Add(operation, "بهای تمام‌شده", Money(source.CostAmount, source.ProfitCurrency?.Code));
            Add(operation, "مفاد تبدیل پول", Money(source.ExchangeProfitAmount, source.ProfitCurrency?.Code));
            Add(operation, "وضعیت محاسبه مفاد", AppDisplayText.ProfitStatus(source.ProfitStatus));
            Add(operation, "توضیحات", source.Description);
        }
    }

    private async Task PopulateCapitalDetailsAsync(List<JournalOperationDto> operations)
    {
        var ids = SourceIds(operations, "ثبت سرمایه");
        if (ids.Count == 0)
            return;

        var sources = await _context.CapitalInvestments
            .AsNoTracking()
            .Include(x => x.Currency)
            .Include(x => x.ProfitCurrency)
            .Include(x => x.ReceivingAccount)
            .Include(x => x.CapitalAccount)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var operation in operations.Where(x => x.SourceType == "ثبت سرمایه"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            Add(operation, "تاریخ ثبت سرمایه", source.InvestmentDate.ToString("yyyy/MM/dd HH:mm"));
            Add(operation, "مبلغ سرمایه", Money(source.Amount, source.Currency.Code));
            Add(operation, "ارزش در ارز اصلی", Money(source.ProfitCurrencyAmount, source.ProfitCurrency?.Code));
            Add(operation, "حساب دریافت‌کننده", source.ReceivingAccount.AccountName);
            Add(operation, "حساب سرمایه مالک", source.CapitalAccount.AccountName);
            Add(operation, "توضیحات", source.Description);
        }
    }

    private async Task PopulateExpenseDetailsAsync(List<JournalOperationDto> operations)
    {
        var ids = SourceIds(operations, "مصرف");
        if (ids.Count == 0)
            return;

        var sources = await _context.Expenses
            .AsNoTracking()
            .Include(x => x.Currency)
            .Include(x => x.ExpenseAccount)
            .Include(x => x.PaidFromAccount)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var operation in operations.Where(x => x.SourceType == "مصرف"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            operation.DocumentNumber = source.Id.ToString();
            Add(operation, "عنوان مصرف", source.Title);
            Add(operation, "تاریخ مصرف", source.ExpenseDate.ToString("yyyy/MM/dd HH:mm"));
            Add(operation, "مبلغ مصرف", Money(source.Amount, source.Currency.Code));
            Add(operation, "حساب مصرف", source.ExpenseAccount.AccountName);
            Add(operation, "پرداخت از حساب", source.PaidFromAccount.AccountName);
            Add(operation, "توضیحات", source.Description);
        }
    }

    private async Task PopulateAccountOperationDetailsAsync(List<JournalOperationDto> operations)
    {
        var ids = SourceIds(operations, "واریز / برداشت / پرداخت");
        if (ids.Count == 0)
            return;

        var sources = await _context.AccountMoneyOperations
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.CashOrBankAccount)
            .Include(x => x.Currency)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var operation in operations.Where(x => x.SourceType == "واریز / برداشت / پرداخت"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            Add(operation, "نوع عملیات", source.OperationType == "Deposit" ? "رسید" : source.OperationType == "Withdraw" ? "برد" : "عملیات حساب");
            Add(operation, "تاریخ عملیات", source.OperationDate.ToString("yyyy/MM/dd HH:mm"));
            Add(operation, "مبلغ", Money(source.Amount, source.Currency.Code));
            Add(operation, "حساب طرف", source.Account.AccountName);
            Add(operation, "صندوق / بانک", source.CashOrBankAccount.AccountName);
            Add(operation, "توضیحات", source.Description);
        }
    }

    private async Task PopulateTransferDetailsAsync(List<JournalOperationDto> operations)
    {
        var ids = SourceIds(operations, "انتقال");
        if (ids.Count == 0)
            return;

        var sources = await _context.Transfers
            .AsNoTracking()
            .Include(x => x.FromAccount)
            .Include(x => x.ToAccount)
            .Include(x => x.Currency)
            .Include(x => x.ProfitCurrency)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var operation in operations.Where(x => x.SourceType == "انتقال"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            operation.DocumentNumber = string.IsNullOrWhiteSpace(source.ReferenceNumber)
                ? source.Id.ToString()
                : source.ReferenceNumber;
            Add(operation, "از حساب", source.FromAccount?.AccountName);
            Add(operation, "به حساب", source.ToAccount?.AccountName);
            Add(operation, "مبلغ انتقال", Money(source.Amount, source.Currency?.Code));
            if (source.ProfitCurrencyAmount is > 0)
                Add(operation, "ارزش انتقالی", Money(source.ProfitCurrencyAmount.Value, source.ProfitCurrency?.Code));
            Add(operation, "روش انتقال", AppDisplayText.TransferMethod(source.TransferMethod));
            Add(operation, "شماره مرجع", source.ReferenceNumber);
            Add(operation, "توضیحات", source.Remarks);
        }
    }

    private async Task PopulateTransactionDetailsAsync(List<JournalOperationDto> operations)
    {
        var ids = SourceIds(operations, "تراکنش");
        if (ids.Count == 0)
            return;

        var sources = await _context.Transactions
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Customer)
            .Include(x => x.TransactionDetails)!
                .ThenInclude(x => x.FromCurrency)
            .Include(x => x.TransactionDetails)!
                .ThenInclude(x => x.ToCurrency)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var settlementConversions = await _context.CorrespondentSettlementConversions
            .AsNoTracking()
            .Include(x => x.Correspondent)
            .Include(x => x.TargetCurrency)
            .Include(x => x.Items)
                .ThenInclude(x => x.SourceCurrency)
            .Include(x => x.HawalaItems)
                .ThenInclude(x => x.SourceCurrency)
            .Include(x => x.HawalaItems)
                .ThenInclude(x => x.Hawala)
            .Include(x => x.Hawalas)
            .Where(x => ids.Contains(x.TransactionId))
            .ToDictionaryAsync(x => x.TransactionId);

        var aedDeals = await _context.AedDeals
            .AsNoTracking()
            .Include(x => x.SourceCorrespondent)
            .Include(x => x.DubaiCorrespondent)
            .Include(x => x.SourceCurrency)
            .Where(x => ids.Contains(x.HoldingTransactionId) ||
                        (x.ReversalTransactionId.HasValue && ids.Contains(x.ReversalTransactionId.Value)))
            .ToListAsync();

        var aedConversions = await _context.AedDealConversions
            .AsNoTracking()
            .Include(x => x.Deal).ThenInclude(x => x.SourceCorrespondent)
            .Include(x => x.Deal).ThenInclude(x => x.DubaiCorrespondent)
            .Include(x => x.Deal).ThenInclude(x => x.SourceCurrency)
            .Where(x => ids.Contains(x.PostingTransactionId) ||
                        (x.ReversalTransactionId.HasValue && ids.Contains(x.ReversalTransactionId.Value)))
            .ToListAsync();

        foreach (var operation in operations.Where(x => x.SourceType == "تراکنش"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            operation.DocumentNumber = source.TransactionNo;
            Add(operation, "شماره تراکنش", source.TransactionNo);
            Add(operation, "نوع تراکنش", AppDisplayText.TransactionType(source.TransactionType));
            Add(operation, "وضعیت", AppDisplayText.Status(source.Status));
            Add(operation, "شعبه", source.Branch?.Name);
            Add(operation, "مشتری", source.CustomerFullName ?? source.Customer?.FullName);
            Add(operation, "ملاحظات", source.Remarks);

            if (source.TransactionType == "CorrespondentSettlementConversion" &&
                settlementConversions.TryGetValue(source.Id, out var settlement))
            {
                var conversionType = settlement.SourceMode == "Hawalas"
                    ? $"تبدیل {settlement.Hawalas.Count} حواله به ارز توافقی"
                    : "تبدیل مانده حساب نمایندگی به ارز توافقی";
                var conversionSummary = settlement.SourceMode == "Hawalas"
                    ? string.Join("؛ ", settlement.HawalaItems.Select(item =>
                    {
                        var sourceAmount = item.SourceTalabKar > 0 ? item.SourceTalabKar : item.SourceBadehKar;
                        var targetAmount = item.TargetTalabKar > 0 ? item.TargetTalabKar : item.TargetBadehKar;
                        return $"حواله شماره {item.Hawala.Number}: {AmountValueHelper.Format(sourceAmount)} {item.SourceCurrency.Code} " +
                               $"با نرخ {AmountValueHelper.Format(item.ExchangeRate)} به " +
                               $"{AmountValueHelper.Format(targetAmount)} {settlement.TargetCurrency.Code}";
                    }))
                    : string.Join("؛ ", settlement.Items.Select(item =>
                {
                    var sourceAmount = item.SourceTalabKar > 0
                        ? item.SourceTalabKar
                        : item.SourceBadehKar;
                    var targetAmount = item.TargetTalabKar > 0
                        ? item.TargetTalabKar
                        : item.TargetBadehKar;
                    var direction = item.SourceTalabKar > 0 ? "طلبکار" : "بدهکار";
                    return $"ماندهٔ {direction} {AmountValueHelper.Format(sourceAmount)} {item.SourceCurrency.Code} " +
                           $"با نرخ {AmountValueHelper.Format(item.ExchangeRate)} به " +
                           $"{AmountValueHelper.Format(targetAmount)} {settlement.TargetCurrency.Code}";
                }));

                Add(operation, "نوع تبدیل", conversionType);
                Add(operation, "نمایندگی", settlement.Correspondent.Name);
                Add(operation, "خلاصه تبدیل", conversionSummary);
            }

            var aedDeal = aedDeals.FirstOrDefault(x =>
                x.HoldingTransactionId == source.Id || x.ReversalTransactionId == source.Id);
            var aedConversion = aedConversions.FirstOrDefault(x =>
                x.PostingTransactionId == source.Id || x.ReversalTransactionId == source.Id);
            if (aedDeal != null)
            {
                Add(operation, "نمبر معامله درهم", aedDeal.DealNumber);
                Add(operation, "طرف کویته", aedDeal.SourceCorrespondent.Name);
                Add(operation, "طرف دبی", aedDeal.DubaiCorrespondent.Name);
                Add(operation, "مبلغ اصل معامله", Money(aedDeal.OriginalAmount, aedDeal.SourceCurrency.Code));
                Add(operation, "وضعیت معامله", AppDisplayText.Status(aedDeal.Status));
            }
            else if (aedConversion != null)
            {
                Add(operation, "نمبر معامله درهم", aedConversion.Deal.DealNumber);
                Add(operation, "طرف کویته", aedConversion.Deal.SourceCorrespondent.Name);
                Add(operation, "طرف دبی", aedConversion.Deal.DubaiCorrespondent.Name);
                Add(operation, "مبلغ تبدیل", Money(aedConversion.SourceAmount, aedConversion.Deal.SourceCurrency.Code));
                Add(operation, "مبلغ نهایی", Money(aedConversion.FinalUsdAmount, "USD"));
                Add(operation, "مبلغ اعلامی به کویته", Money(aedConversion.DeclaredUsdAmount, "USD"));
                Add(operation, aedConversion.ProfitUsd >= 0 ? "مفاد" : "زیان",
                    Money(Math.Abs(aedConversion.ProfitUsd), "USD"));
                Add(operation, "وضعیت تبدیل", aedConversion.Status == "Posted" ? "ثبت‌شده" : "برگشت‌شده");
            }

            var index = 1;
            foreach (var detail in source.TransactionDetails ?? [])
            {
                Add(operation, $"جزئیات {index} - مبلغ مبدأ", Money(detail.FromAmount, detail.FromCurrency?.Code));
                Add(operation, $"جزئیات {index} - مبلغ مقصد", Money(detail.ToAmount, detail.ToCurrency?.Code));
                Add(operation, $"جزئیات {index} - نرخ", Number(detail.ExchangeRate));
                Add(operation, $"جزئیات {index} - فرستنده", detail.SenderName);
                Add(operation, $"جزئیات {index} - گیرنده", detail.ReceiverName);
                Add(operation, $"جزئیات {index} - شماره مرجع", detail.ReferenceNumber);
                index++;
            }
        }
    }

    private static HashSet<long> SourceIds(
        IEnumerable<JournalOperationDto> operations,
        string sourceType) =>
        operations
            .Where(x => x.SourceType == sourceType && x.SourceId.HasValue)
            .Select(x => x.SourceId!.Value)
            .ToHashSet();

    private static void Add(JournalOperationDto operation, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            operation.SourceDetails.Add(new JournalOperationFieldDto
            {
                Label = label,
                Value = value
            });
        }
    }

    private static string BuildSummarySentence(JournalOperationDto operation)
    {
        var accounts = operation.AccountNames.Count == 0
            ? "حساب نامشخص"
            : string.Join(" و ", operation.AccountNames.Take(3));
        const string suffix = ".";

        return operation.SourceType switch
        {
            "حواله" =>
                $"حواله شماره {operation.DocumentNumber} از {Detail(operation, "فرستنده", "فرستنده نامشخص")} به {Detail(operation, "گیرنده", "گیرنده نامشخص")} به مبلغ {Detail(operation, "مبلغ ارسالی", CurrencyMovement(operation))} ثبت شد{suffix}",

            "تبدیل پول" =>
                $"در {Detail(operation, "نوع عملیات", "عملیات تبدیل پول")} شماره {operation.DocumentNumber}، {Detail(operation, "ارز فروش / مبلغ پرداختی", "مبلغ پرداختی نامشخص")} از حساب {Detail(operation, "حساب ارز فروش", "نامشخص")} پرداخت و {Detail(operation, "ارز خرید / مبلغ دریافتی", "مبلغ دریافتی نامشخص")} به حساب {Detail(operation, "حساب ارز خرید", "نامشخص")} دریافت شد{suffix}",

            "ثبت سرمایه" =>
                $"سرمایه مالک به مبلغ {Detail(operation, "مبلغ سرمایه", CurrencyMovement(operation))} در حساب {Detail(operation, "حساب دریافت‌کننده", accounts)} ثبت و حساب {Detail(operation, "حساب سرمایه مالک", "سرمایه مالک")} طرف مقابل گردید{suffix}",

            "مصرف" =>
                $"مصرف «{Detail(operation, "عنوان مصرف", "بدون عنوان")}» به مبلغ {Detail(operation, "مبلغ مصرف", CurrencyMovement(operation))} از حساب {Detail(operation, "پرداخت از حساب", accounts)} پرداخت و در {Detail(operation, "حساب مصرف", "حساب مصرف")} ثبت شد{suffix}",

            "واریز / برداشت / پرداخت" =>
                $"{Detail(operation, "نوع عملیات", "رسید یا برد")} شماره {operation.DocumentNumber} به مبلغ {Detail(operation, "مبلغ", CurrencyMovement(operation))} میان حساب {Detail(operation, "حساب طرف", accounts)} و {Detail(operation, "صندوق / بانک", "صندوق یا بانک")} ثبت شد{suffix}",

            "انتقال" =>
                $"مبلغ {Detail(operation, "مبلغ انتقال", CurrencyMovement(operation))} از حساب {Detail(operation, "از حساب", "نامشخص")} به حساب {Detail(operation, "به حساب", "نامشخص")} انتقال شد{TransferDescription(operation)}{suffix}",

            "تراکنش" when Detail(operation, "نوع تراکنش", "") == AppDisplayText.TransactionType("CorrespondentSettlementConversion") =>
                $"{Detail(operation, "خلاصه تبدیل", CurrencyMovement(operation))} تبدیل شد و در حساب ثبت شد{suffix}",

            "تراکنش" when Detail(operation, "نوع تراکنش", "") == AppDisplayText.TransactionType("AedDealHolding") =>
                $"معامله درهم شماره {Detail(operation, "نمبر معامله درهم", operation.DocumentNumber)} به مبلغ {Detail(operation, "مبلغ اصل معامله", CurrencyMovement(operation))} از طرف {Detail(operation, "طرف کویته", "کویته")} نزد {Detail(operation, "طرف دبی", "طرف دبی")} ثبت شد{suffix}",

            "تراکنش" when Detail(operation, "نوع تراکنش", "") == AppDisplayText.TransactionType("AedDealConversion") =>
                $"از معامله درهم شماره {Detail(operation, "نمبر معامله درهم", operation.DocumentNumber)}، مبلغ {Detail(operation, "مبلغ تبدیل", CurrencyMovement(operation))} به {Detail(operation, "مبلغ نهایی", "دالر")} تبدیل و در حساب ثبت شد{suffix}",

            "تراکنش" when Detail(operation, "نوع تراکنش", "") == AppDisplayText.TransactionType("AedDealReversal") =>
                $"سند مربوط به معامله درهم شماره {Detail(operation, "نمبر معامله درهم", operation.DocumentNumber)} برگشت داده شد{suffix}",

            "تراکنش" =>
                $"تراکنش {Detail(operation, "نوع تراکنش", "عمومی")} به شماره {operation.DocumentNumber} برای {Detail(operation, "مشتری", "حساب‌های مرتبط")} در حساب‌های {accounts} ثبت شد{suffix}",

            "ثبت دستی" =>
                $"ثبت دستی شماره {operation.DocumentNumber} در حساب {accounts} به مبلغ {CurrencyMovement(operation)} انجام شد{suffix}",

            _ =>
                $"عملیات {operation.SourceType} شماره {operation.DocumentNumber} در حساب‌های {accounts} با گردش {CurrencyMovement(operation)} ثبت شد{suffix}"
        };
    }

    private static string Detail(
        JournalOperationDto operation,
        string label,
        string fallback) =>
        operation.SourceDetails
            .FirstOrDefault(x => x.Label == label)?.Value
        ?? fallback;

    private static string TransferDescription(JournalOperationDto operation)
    {
        var description = Detail(operation, "توضیحات", string.Empty);
        return string.IsNullOrWhiteSpace(description)
            ? string.Empty
            : $"؛ توضیحات: {description.Trim()}";
    }

    private static string CurrencyMovement(JournalOperationDto operation)
    {
        if (operation.CurrencySummaries.Count == 0)
            return "نامشخص";

        return string.Join("، ", operation.CurrencySummaries.Select(x =>
        {
            var debit = x.TotalBadehKar > 0
                ? $"بدهکار {AmountValueHelper.Format(x.TotalBadehKar)}"
                : string.Empty;
            var credit = x.TotalTalabKar > 0
                ? $"طلبکار {AmountValueHelper.Format(x.TotalTalabKar)}"
                : string.Empty;
            var separator = debit.Length > 0 && credit.Length > 0 ? " و " : string.Empty;
            return $"{x.CurrencyCode} {debit}{separator}{credit}".Trim();
        }));
    }

    private static string? Money(decimal? amount, string? currencyCode) =>
        amount.HasValue
            ? $"{AmountValueHelper.Format(amount.Value)} {currencyCode}".Trim()
            : null;

    private static string? Number(decimal? value) =>
        value.HasValue ? AmountValueHelper.Format(value.Value) : null;

    private static string GetOperationKey(LedgerEntry entry)
    {
        if (entry.HawalaId.HasValue)
            return $"Hawala:{entry.HawalaId.Value}";
        if (entry.CapitalInvestmentId.HasValue)
            return $"CapitalInvestment:{entry.CapitalInvestmentId.Value}";
        if (entry.ExpenseId.HasValue)
            return $"Expense:{entry.ExpenseId.Value}";
        if (entry.AccountMoneyOperationId.HasValue)
            return $"AccountMoneyOperation:{entry.AccountMoneyOperationId.Value}";
        if (entry.MoneyExchangeOperationId.HasValue)
            return $"MoneyExchangeOperation:{entry.MoneyExchangeOperationId.Value}";
        if (entry.TransferId.HasValue)
            return $"Transfer:{entry.TransferId.Value}";
        if (entry.TransactionId.HasValue)
            return $"Transaction:{entry.TransactionId.Value}";
        return $"Manual:{entry.Id}";
    }

    private static string GetSourceType(LedgerEntry entry)
    {
        if (entry.HawalaId.HasValue)
            return "حواله";
        if (entry.CapitalInvestmentId.HasValue)
            return "ثبت سرمایه";
        if (entry.ExpenseId.HasValue)
            return "مصرف";
        if (entry.AccountMoneyOperationId.HasValue)
            return "واریز / برداشت / پرداخت";
        if (entry.MoneyExchangeOperationId.HasValue)
            return "تبدیل پول";
        if (entry.TransferId.HasValue)
            return "انتقال";
        if (entry.TransactionId.HasValue)
            return "تراکنش";
        return "ثبت دستی";
    }

    private static long? GetSourceId(LedgerEntry entry)
    {
        if (entry.HawalaId.HasValue)
            return entry.HawalaId;
        if (entry.CapitalInvestmentId.HasValue)
            return entry.CapitalInvestmentId;
        if (entry.ExpenseId.HasValue)
            return entry.ExpenseId;
        if (entry.AccountMoneyOperationId.HasValue)
            return entry.AccountMoneyOperationId;
        if (entry.MoneyExchangeOperationId.HasValue)
            return entry.MoneyExchangeOperationId;
        if (entry.TransferId.HasValue)
            return entry.TransferId;
        if (entry.TransactionId.HasValue)
            return entry.TransactionId;
        return null;
    }
}
