using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class CorrespondentSettlementService(
    ApplicationDbContext context) : ICorrespondentSettlementService
{
    private const string ClearingAccountCode = "SYS-SETTLEMENT-CLEARING";

    public async Task<CorrespondentSettlementPreviewDto> GetPreviewAsync(
        long correspondentId,
        CancellationToken cancellationToken = default)
    {
        var correspondent = await GetConfiguredCorrespondentAsync(correspondentId, cancellationToken);
        var accountId = await GetCorrespondentAccountIdAsync(correspondentId, cancellationToken);
        var balances = await GetNetBalancesAsync(accountId, correspondent.SettlementCurrencyId!.Value, cancellationToken);
        var pendingCount = await PendingHawalas(correspondentId, correspondent.SettlementCurrencyId.Value)
            .CountAsync(cancellationToken);

        return new CorrespondentSettlementPreviewDto
        {
            CorrespondentId = correspondent.Id,
            CorrespondentName = correspondent.Name,
            TargetCurrencyId = correspondent.SettlementCurrencyId.Value,
            TargetCurrencyCode = correspondent.SettlementCurrency!.Code,
            Balances = balances,
            PendingHawalaCount = pendingCount
        };
    }

    public async Task<CorrespondentSettlementResultDto> ConvertHawalasAsync(
        ConvertHawalasToSettlementDto dto,
        CancellationToken cancellationToken = default)
    {
        var ids = dto.HawalaIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            throw new InvalidOperationException("حداقل یک حواله را برای تبدیل انتخاب کنید.");

        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var correspondent = await GetConfiguredCorrespondentAsync(dto.CorrespondentId, cancellationToken);
        var targetCurrencyId = correspondent.SettlementCurrencyId!.Value;
        var hawalas = await PendingHawalas(dto.CorrespondentId, targetCurrencyId)
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id })
            .ToListAsync(cancellationToken);

        if (hawalas.Count != ids.Length)
            throw new InvalidOperationException("یک یا چند حواله معتبر نیست، قبلاً تبدیل شده یا به ارز توافقی ثبت شده است.");

        var accountId = await GetCorrespondentAccountIdAsync(dto.CorrespondentId, cancellationToken);
        var balances = await GetHawalaNetBalancesAsync(accountId, ids, targetCurrencyId, cancellationToken);
        var result = await CreateConversionAsync(
            correspondent,
            accountId,
            balances,
            dto.Rates,
            "Hawalas",
            dto.Note,
            ids,
            cancellationToken);

        await dbTransaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CorrespondentSettlementResultDto> ConvertBalanceAsync(
        ConvertCorrespondentBalanceDto dto,
        CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var correspondent = await GetConfiguredCorrespondentAsync(dto.CorrespondentId, cancellationToken);
        var targetCurrencyId = correspondent.SettlementCurrencyId!.Value;
        var accountId = await GetCorrespondentAccountIdAsync(dto.CorrespondentId, cancellationToken);
        var balances = await GetNetBalancesAsync(accountId, targetCurrencyId, cancellationToken);
        if (balances.Count == 0)
            throw new InvalidOperationException("مانده‌ای در ارزهای دیگر برای تبدیل وجود ندارد.");

        var sourceCurrencyIds = balances.Select(x => x.SourceCurrencyId).ToArray();
        var hawalaIds = await PendingHawalas(dto.CorrespondentId, targetCurrencyId)
            .Where(x => sourceCurrencyIds.Contains(x.HawalaType == "HawalaSend" ? x.ToCurrencyId : x.FromCurrencyId))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        var result = await CreateConversionAsync(
            correspondent,
            accountId,
            balances,
            dto.Rates,
            "Account",
            dto.Note,
            hawalaIds,
            cancellationToken);

        await dbTransaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<CorrespondentSettlementResultDto> CreateConversionAsync(
        Correspondent correspondent,
        long correspondentAccountId,
        IReadOnlyList<CorrespondentSettlementBalanceDto> balances,
        IEnumerable<SettlementRateDto> submittedRates,
        string sourceMode,
        string? note,
        IReadOnlyCollection<long> hawalaIds,
        CancellationToken cancellationToken)
    {
        var rates = submittedRates
            .Where(x => x.SourceCurrencyId > 0 && x.Rate > 0)
            .GroupBy(x => x.SourceCurrencyId)
            .ToDictionary(x => x.Key, x => x.Last().Rate);
        var targetCurrency = correspondent.SettlementCurrency!;
        var sourceCurrencies = await context.Currencies
            .Where(x => balances.Select(b => b.SourceCurrencyId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var balance in balances)
        {
            if (!rates.ContainsKey(balance.SourceCurrencyId))
                throw new InvalidOperationException($"نرخ تبدیل ارز {balance.SourceCurrencyCode} وارد نشده است.");
        }

        var clearingAccountId = await GetOrCreateClearingAccountIdAsync(cancellationToken);
        var transaction = new Transaction
        {
            TransactionNo = await GenerateTransactionNumberAsync(cancellationToken),
            TransactionType = "CorrespondentSettlementConversion",
            BranchId = await context.GetDefaultBranchIdAsync(cancellationToken),
            Status = "Paid",
            Remarks = $"تبدیل حساب نمایندگی {correspondent.Name} به {targetCurrency.Code}. {note}".Trim(),
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);

        var conversion = new CorrespondentSettlementConversion
        {
            CorrespondentId = correspondent.Id,
            TargetCurrencyId = targetCurrency.Id,
            TransactionId = transaction.Id,
            SourceMode = sourceMode,
            Note = note?.Trim(),
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        context.CorrespondentSettlementConversions.Add(conversion);
        var resultItems = new List<CorrespondentSettlementResultItemDto>();

        foreach (var hawalaId in hawalaIds)
        {
            conversion.Hawalas.Add(new CorrespondentSettlementConversionHawala
            {
                HawalaId = hawalaId
            });
        }

        foreach (var balance in balances)
        {
            var source = sourceCurrencies[balance.SourceCurrencyId];
            var sourceAmount = Math.Abs(balance.NetAmount);
            if (sourceAmount == 0) continue;

            var rate = rates[balance.SourceCurrencyId];
            var converted = CurrencyQuotationCalculator.ConvertFromAmount(
                source.Id, source.Code, source.QuotationPriority, sourceAmount,
                targetCurrency.Id, targetCurrency.Code, targetCurrency.QuotationPriority, rate);
            var targetAmount = decimal.Round(
                converted.ToAmount,
                Math.Clamp(targetCurrency.DecimalPlaces, 0, 8),
                MidpointRounding.AwayFromZero);
            if (targetAmount <= 0)
                throw new InvalidOperationException($"حاصل تبدیل {source.Code} معتبر نیست.");

            conversion.Items.Add(new CorrespondentSettlementConversionItem
            {
                SourceCurrencyId = source.Id,
                SourceTalabKar = balance.TalabKar,
                SourceBadehKar = balance.BadehKar,
                ExchangeRate = rate,
                TargetTalabKar = balance.NetAmount > 0 ? targetAmount : 0,
                TargetBadehKar = balance.NetAmount < 0 ? targetAmount : 0
            });
            resultItems.Add(new CorrespondentSettlementResultItemDto
            {
                SourceCurrencyCode = source.Code,
                SourceAmount = sourceAmount,
                BalanceDirection = balance.NetAmount > 0 ? "طلبکار" : "بدهکار",
                ExchangeRate = rate,
                TargetCurrencyCode = targetCurrency.Code,
                TargetAmount = targetAmount
            });

            AddBalancedLedgerEntries(
                transaction.Id,
                correspondentAccountId,
                clearingAccountId,
                source.Id,
                targetCurrency.Id,
                sourceAmount,
                targetAmount,
                balance.NetAmount > 0,
                correspondent.Name);
        }

        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = context.RequireCurrentUserId(),
            Action = sourceMode == "Hawalas" ? "CONVERT_HAWALAS_TO_SETTLEMENT" : "CONVERT_BALANCE_TO_SETTLEMENT",
            TableName = "CorrespondentSettlementConversions",
            RecordId = conversion.Id,
            NewValue = $"نمایندگی {correspondent.Name}: {balances.Count} ارز و {hawalaIds.Count} حواله به {targetCurrency.Code} تبدیل شد.",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);

        return new CorrespondentSettlementResultDto
        {
            ConversionId = conversion.Id,
            TransactionId = transaction.Id,
            HawalaCount = hawalaIds.Count,
            CurrencyCount = conversion.Items.Count,
            Items = resultItems
        };
    }

    private void AddBalancedLedgerEntries(
        long transactionId,
        long correspondentAccountId,
        long clearingAccountId,
        long sourceCurrencyId,
        long targetCurrencyId,
        decimal sourceAmount,
        decimal targetAmount,
        bool correspondentWasCreditor,
        string correspondentName)
    {
        var description = $"تبدیل مانده نمایندگی {correspondentName} به ارز توافقی";
        if (correspondentWasCreditor)
        {
            AddEntry(transactionId, correspondentAccountId, sourceCurrencyId, 0, sourceAmount, description);
            AddEntry(transactionId, clearingAccountId, sourceCurrencyId, sourceAmount, 0, description);
            AddEntry(transactionId, correspondentAccountId, targetCurrencyId, targetAmount, 0, description);
            AddEntry(transactionId, clearingAccountId, targetCurrencyId, 0, targetAmount, description);
        }
        else
        {
            AddEntry(transactionId, correspondentAccountId, sourceCurrencyId, sourceAmount, 0, description);
            AddEntry(transactionId, clearingAccountId, sourceCurrencyId, 0, sourceAmount, description);
            AddEntry(transactionId, correspondentAccountId, targetCurrencyId, 0, targetAmount, description);
            AddEntry(transactionId, clearingAccountId, targetCurrencyId, targetAmount, 0, description);
        }
    }

    private void AddEntry(
        long transactionId,
        long accountId,
        long currencyId,
        decimal talabKar,
        decimal badehKar,
        string description) => context.LedgerEntries.Add(new LedgerEntry
        {
            TransactionId = transactionId,
            AccountId = accountId,
            CurrencyId = currencyId,
            TalabKar = talabKar,
            BadehKar = badehKar,
            Description = description,
            CreatedAt = DateTime.UtcNow
        });

    private IQueryable<Hawala> PendingHawalas(long correspondentId, long targetCurrencyId) =>
        context.Hawalas.Where(x =>
            x.CorrespondentId == correspondentId &&
            x.Status != "Cancel" &&
            (x.HawalaType == "HawalaSend" ? x.ToCurrencyId : x.FromCurrencyId) != targetCurrencyId &&
            !x.SettlementConversionLinks.Any());

    private async Task<List<CorrespondentSettlementBalanceDto>> GetHawalaNetBalancesAsync(
        long accountId,
        IReadOnlyCollection<long> hawalaIds,
        long targetCurrencyId,
        CancellationToken cancellationToken) => await context.LedgerEntries
        .Where(x => x.AccountId == accountId && x.HawalaId.HasValue &&
                    hawalaIds.Contains(x.HawalaId.Value) && x.CurrencyId != targetCurrencyId)
        .GroupBy(x => new { x.CurrencyId, x.Currency!.Code })
        .Select(x => new CorrespondentSettlementBalanceDto
        {
            SourceCurrencyId = x.Key.CurrencyId,
            SourceCurrencyCode = x.Key.Code,
            TalabKar = x.Sum(y => y.TalabKar) > x.Sum(y => y.BadehKar)
                ? x.Sum(y => y.TalabKar) - x.Sum(y => y.BadehKar) : 0,
            BadehKar = x.Sum(y => y.BadehKar) > x.Sum(y => y.TalabKar)
                ? x.Sum(y => y.BadehKar) - x.Sum(y => y.TalabKar) : 0
        })
        .Where(x => x.TalabKar > 0 || x.BadehKar > 0)
        .ToListAsync(cancellationToken);

    private async Task<List<CorrespondentSettlementBalanceDto>> GetNetBalancesAsync(
        long accountId,
        long targetCurrencyId,
        CancellationToken cancellationToken) => await context.LedgerEntries
        .Where(x => x.AccountId == accountId && x.CurrencyId != targetCurrencyId)
        .GroupBy(x => new { x.CurrencyId, x.Currency!.Code })
        .Select(x => new CorrespondentSettlementBalanceDto
        {
            SourceCurrencyId = x.Key.CurrencyId,
            SourceCurrencyCode = x.Key.Code,
            TalabKar = x.Sum(y => y.TalabKar) > x.Sum(y => y.BadehKar)
                ? x.Sum(y => y.TalabKar) - x.Sum(y => y.BadehKar) : 0,
            BadehKar = x.Sum(y => y.BadehKar) > x.Sum(y => y.TalabKar)
                ? x.Sum(y => y.BadehKar) - x.Sum(y => y.TalabKar) : 0
        })
        .Where(x => x.TalabKar > 0 || x.BadehKar > 0)
        .OrderBy(x => x.SourceCurrencyCode)
        .ToListAsync(cancellationToken);

    private async Task<Correspondent> GetConfiguredCorrespondentAsync(long correspondentId, CancellationToken cancellationToken)
    {
        var correspondent = await context.Correspondents
            .Include(x => x.SettlementCurrency)
            .SingleOrDefaultAsync(x => x.Id == correspondentId, cancellationToken)
            ?? throw new KeyNotFoundException("نمایندگی یافت نشد.");
        if (!correspondent.SettlementCurrencyId.HasValue || correspondent.SettlementCurrency is null)
            throw new InvalidOperationException("برای این نمایندگی ارز توافقی تعیین نشده است.");
        if (!correspondent.SettlementCurrency.IsActive)
            throw new InvalidOperationException("ارز توافقی این نمایندگی غیرفعال است.");
        return correspondent;
    }

    private async Task<long> GetCorrespondentAccountIdAsync(long correspondentId, CancellationToken cancellationToken) =>
        await context.Accounts
            .Where(x => x.CorrespondentId == correspondentId && !x.IsArchived)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken) is var accountId && accountId > 0
                ? accountId
                : throw new InvalidOperationException("حساب فعال نمایندگی یافت نشد.");

    private async Task<long> GetOrCreateClearingAccountIdAsync(CancellationToken cancellationToken)
    {
        var existingAccount = await context.Accounts
            .Where(x => x.AccountCode == ClearingAccountCode)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingAccount != null)
        {
            if (existingAccount.AccountType != "CurrencyConversionClearing")
                throw new InvalidOperationException("کد حساب واسط تبدیل ارز قبلاً برای حساب دیگری استفاده شده است.");
            return existingAccount.Id;
        }

        var account = new Account
        {
            AccountCode = ClearingAccountCode,
            AccountName = "حساب واسط تبدیل ارز نمایندگی‌ها",
            AccountType = "CurrencyConversionClearing",
            IsArchived = false,
            CreatedAt = DateTime.UtcNow
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        return account.Id;
    }

    private async Task<string> GenerateTransactionNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = $"SC-{DateTime.UtcNow:yyyyMMdd}-";
        var last = await context.Transactions
            .Where(x => x.TransactionNo.StartsWith(prefix))
            .OrderByDescending(x => x.TransactionNo)
            .Select(x => x.TransactionNo)
            .FirstOrDefaultAsync(cancellationToken);
        var sequence = last is not null && int.TryParse(last[prefix.Length..], out var number) ? number + 1 : 1;
        return $"{prefix}{sequence:D4}";
    }
}
