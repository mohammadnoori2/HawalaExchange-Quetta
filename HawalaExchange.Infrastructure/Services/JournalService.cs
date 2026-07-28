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

    private static JournalEntryDto ToEntryDto(LedgerEntry entry) => new()
    {
        Id = entry.Id,
        CreatedAt = entry.CreatedAt,
        SourceType = GetSourceType(entry),
        SourceId = GetSourceId(entry),
        AccountId = entry.AccountId,
        AccountCode = entry.Account?.AccountCode ?? string.Empty,
        AccountName = entry.Account?.AccountName ?? string.Empty,
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
            Add(operation, "نوع ثبت", "ثبت مستقیم در لیجر");
            Add(operation, "شناسه ردیف لیجر", operation.LedgerEntries[0].Id.ToString());
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
            Add(operation, "نوع حواله", source.HawalaType);
            Add(operation, "وضعیت", source.Status);
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
            Add(operation, "شماره مرجع", source.ReferenceNumber);
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
            Add(operation, "نرخ تبادله", source.ExchangeRate.ToString("N6"));
            Add(operation, "کمیسیون", Money(source.CommissionAmount, source.ProfitCurrency?.Code));
            Add(operation, "هزینه خارجی", Money(source.ExternalFeeAmount, source.ProfitCurrency?.Code));
            Add(operation, "بهای تمام‌شده", Money(source.CostAmount, source.ProfitCurrency?.Code));
            Add(operation, "مفاد تبدیل پول", Money(source.ExchangeProfitAmount, source.ProfitCurrency?.Code));
            Add(operation, "وضعیت محاسبه مفاد", source.ProfitStatus);
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
        var ids = SourceIds(operations, "واریز / برداشت");
        if (ids.Count == 0)
            return;

        var sources = await _context.AccountMoneyOperations
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.CashOrBankAccount)
            .Include(x => x.Currency)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var operation in operations.Where(x => x.SourceType == "واریز / برداشت"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            Add(operation, "نوع عملیات", source.OperationType == "Deposit" ? "واریز" : source.OperationType == "Withdraw" ? "برداشت" : source.OperationType);
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
            Add(operation, "روش انتقال", source.TransferMethod);
            Add(operation, "شماره مرجع", source.ReferenceNumber);
            Add(operation, "ملاحظات", source.Remarks);
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

        foreach (var operation in operations.Where(x => x.SourceType == "تراکنش"))
        {
            if (!operation.SourceId.HasValue ||
                !sources.TryGetValue(operation.SourceId.Value, out var source))
                continue;

            operation.DocumentNumber = source.TransactionNo;
            Add(operation, "شماره تراکنش", source.TransactionNo);
            Add(operation, "نوع تراکنش", source.TransactionType);
            Add(operation, "وضعیت", source.Status);
            Add(operation, "شعبه", source.Branch?.Name);
            Add(operation, "مشتری", source.CustomerFullName ?? source.Customer?.FullName);
            Add(operation, "ملاحظات", source.Remarks);

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
        var suffix = $" و شامل {operation.LedgerEntriesCount:N0} ردیف لیجر است.";

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

            "واریز / برداشت" =>
                $"{Detail(operation, "نوع عملیات", "واریز / برداشت")} شماره {operation.DocumentNumber} به مبلغ {Detail(operation, "مبلغ", CurrencyMovement(operation))} میان حساب {Detail(operation, "حساب طرف", accounts)} و {Detail(operation, "صندوق / بانک", "صندوق یا بانک")} ثبت شد{suffix}",

            "انتقال" =>
                $"مبلغ {Detail(operation, "مبلغ انتقال", CurrencyMovement(operation))} از حساب {Detail(operation, "از حساب", "نامشخص")} به حساب {Detail(operation, "به حساب", "نامشخص")} با شماره مرجع {operation.DocumentNumber} انتقال شد{suffix}",

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

    private static string CurrencyMovement(JournalOperationDto operation)
    {
        if (operation.CurrencySummaries.Count == 0)
            return "نامشخص";

        return string.Join("، ", operation.CurrencySummaries.Select(x =>
        {
            var debit = x.TotalBadehKar > 0
                ? $"بدهکار {x.TotalBadehKar:N2}"
                : string.Empty;
            var credit = x.TotalTalabKar > 0
                ? $"طلبکار {x.TotalTalabKar:N2}"
                : string.Empty;
            var separator = debit.Length > 0 && credit.Length > 0 ? " و " : string.Empty;
            return $"{x.CurrencyCode} {debit}{separator}{credit}".Trim();
        }));
    }

    private static string? Money(decimal? amount, string? currencyCode) =>
        amount.HasValue
            ? $"{amount.Value:N2} {currencyCode}".Trim()
            : null;

    private static string? Number(decimal? value) =>
        value.HasValue ? value.Value.ToString("N6") : null;

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
            return "واریز / برداشت";
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
