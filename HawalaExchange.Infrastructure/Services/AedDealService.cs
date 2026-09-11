using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class AedDealService(ApplicationDbContext context) : IAedDealService
{
    private const decimal FixedAedPerUsdRate = 3.67m;
    private const string ProfitAccountCode = "3003";
    private const string LossAccountCode = "4003";

    public async Task<IReadOnlyList<AedDealDto>> GetAllAsync(
        long? correspondentId = null,
        CancellationToken cancellationToken = default)
    {
        var deals = await context.AedDeals.AsNoTracking()
            .Include(x => x.SourceCorrespondent)
            .Include(x => x.DubaiCorrespondent)
            .Include(x => x.SourceCurrency)
            .Include(x => x.Conversions)
            .Where(x => !correspondentId.HasValue ||
                        x.SourceCorrespondentId == correspondentId.Value ||
                        x.DubaiCorrespondentId == correspondentId.Value)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        return deals.Select(Map).ToList();
    }

    public async Task<AedDealDto> CreateAsync(
        CreateAedDealDto dto,
        CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        ValidateCreate(dto);
        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var dealNumber = dto.DealNumber.Trim();
        if (await context.AedDeals.AnyAsync(x => x.DealNumber == dealNumber, cancellationToken))
            throw new InvalidOperationException("نمبر معامله قبلاً ثبت شده است.");

        var (sourceCorrespondent, sourceAccount) = await GetCorrespondentAsync(dto.SourceCorrespondentId, cancellationToken);
        var (dubaiCorrespondent, dubaiAccount) = await GetCorrespondentAsync(dto.DubaiCorrespondentId, cancellationToken);
        var currency = await GetDealCurrencyAsync(dto.SourceCurrencyId, cancellationToken);
        var transaction = new Transaction
        {
            TransactionNo = await GenerateTransactionNumberAsync("AEDH", cancellationToken),
            TransactionType = "AedDealHolding",
            BranchId = await context.GetDefaultBranchIdAsync(cancellationToken),
            Status = "Paid",
            Remarks = $"ثبت معامله درهم {dealNumber}: {sourceCorrespondent.Name} به {dubaiCorrespondent.Name}",
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);

        var description = $"نگهداری معامله {dealNumber} نزد {dubaiCorrespondent.Name}";
        context.LedgerEntries.AddRange(
            NewEntry(transaction.Id, dubaiAccount.Id, currency.Id, 0, dto.Amount, description),
            NewEntry(transaction.Id, sourceAccount.Id, currency.Id, dto.Amount, 0, description));

        var deal = new AedDeal
        {
            DealNumber = dealNumber,
            SourceCorrespondentId = sourceCorrespondent.Id,
            DubaiCorrespondentId = dubaiCorrespondent.Id,
            SourceCurrencyId = currency.Id,
            OriginalAmount = dto.Amount,
            AedPerUsdRate = FixedAedPerUsdRate,
            RoundingDecimalPlaces = dto.RoundingDecimalPlaces,
            Status = "Held",
            HoldingTransactionId = transaction.Id,
            Note = dto.Note?.Trim(),
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        context.AedDeals.Add(deal);
        await context.SaveChangesAsync(cancellationToken);
        AddAudit("CREATE_AED_DEAL", "AedDeals", deal.Id,
            $"معامله {deal.DealNumber} به مبلغ {deal.OriginalAmount} {currency.Code} ثبت شد.");
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(deal.Id, cancellationToken);
    }

    public async Task<AedConversionPreviewDto> PreviewConversionAsync(
        PreviewAedConversionDto dto,
        CancellationToken cancellationToken = default)
    {
        var deal = await GetDealForCalculationAsync(dto.DealId, cancellationToken);
        return Calculate(deal, dto);
    }

    public async Task<AedDealDto> ConvertAsync(
        PreviewAedConversionDto dto,
        CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var deal = await GetDealForCalculationAsync(dto.DealId, cancellationToken, tracked: true);
        var preview = Calculate(deal, dto);
        var sourceAccountId = await GetCorrespondentAccountIdAsync(deal.SourceCorrespondentId, cancellationToken);
        var dubaiAccountId = await GetCorrespondentAccountIdAsync(deal.DubaiCorrespondentId, cancellationToken);
        var usd = await context.Currencies.SingleOrDefaultAsync(x => x.Code == "USD" && x.IsActive, cancellationToken)
                  ?? throw new InvalidOperationException("ارز فعال USD در سیستم یافت نشد.");
        var transaction = new Transaction
        {
            TransactionNo = await GenerateTransactionNumberAsync("AEDC", cancellationToken),
            TransactionType = "AedDealConversion",
            BranchId = await context.GetDefaultBranchIdAsync(cancellationToken),
            Status = "Paid",
            Remarks = $"تبدیل {dto.SourceAmount} {deal.SourceCurrency.Code} از معامله {deal.DealNumber} به USD",
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);

        var conversion = new AedDealConversion
        {
            SourceAmount = dto.SourceAmount,
            AedPerUsdRate = deal.AedPerUsdRate,
            ActualMarker = dto.ActualMarker,
            DeclaredMarker = dto.DeclaredMarker,
            ActualAdjustmentSource = Round(dto.SourceAmount / 100_000m * dto.ActualMarker, 4),
            DeclaredAdjustmentSource = Round(dto.SourceAmount / 100_000m * dto.DeclaredMarker, 4),
            FinalUsdAmount = preview.FinalUsdAmount,
            DeclaredUsdAmount = Round(ToUsd(deal.SourceCurrency.Code,
                dto.SourceAmount + dto.SourceAmount / 100_000m * dto.DeclaredMarker, deal.AedPerUsdRate),
                deal.RoundingDecimalPlaces),
            ProfitUsd = preview.ProfitUsd,
            PostingTransactionId = transaction.Id,
            Status = "Posted",
            Note = dto.Note?.Trim(),
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        deal.Conversions.Add(conversion);
        deal.ConvertedAmount += dto.SourceAmount;
        deal.TotalFinalUsd += preview.FinalUsdAmount;
        deal.TotalProfitUsd += preview.ProfitUsd;
        deal.Status = deal.ConvertedAmount == deal.OriginalAmount ? "Converted" : "PartiallyConverted";

        var description = $"تبدیل معامله {deal.DealNumber} به USD";
        context.LedgerEntries.AddRange(
            NewEntry(transaction.Id, sourceAccountId, deal.SourceCurrencyId, 0, dto.SourceAmount, description),
            NewEntry(transaction.Id, dubaiAccountId, deal.SourceCurrencyId, dto.SourceAmount, 0, description),
            NewEntry(transaction.Id, dubaiAccountId, usd.Id, 0, preview.FinalUsdAmount, description),
            NewEntry(transaction.Id, sourceAccountId, usd.Id, conversion.DeclaredUsdAmount, 0, description));

        if (conversion.ProfitUsd > 0)
        {
            var profitAccount = await GetOrCreateAccountAsync(
                ProfitAccountCode, "مفاد معاملات درهم", "Income", cancellationToken);
            context.LedgerEntries.Add(NewEntry(transaction.Id, profitAccount.Id, usd.Id,
                conversion.ProfitUsd, 0, description));
        }
        else if (conversion.ProfitUsd < 0)
        {
            var lossAccount = await GetOrCreateAccountAsync(
                LossAccountCode, "زیان معاملات درهم", "Expense", cancellationToken);
            context.LedgerEntries.Add(NewEntry(transaction.Id, lossAccount.Id, usd.Id,
                0, Math.Abs(conversion.ProfitUsd), description));
        }

        await context.SaveChangesAsync(cancellationToken);
        AddAudit("CONVERT_AED_DEAL", "AedDealConversions", conversion.Id,
            $"معامله {deal.DealNumber}: {conversion.SourceAmount} {deal.SourceCurrency.Code} به {conversion.FinalUsdAmount} USD تبدیل شد.");
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(deal.Id, cancellationToken);
    }

    public async Task ReverseConversionAsync(
        long conversionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        context.ChangeTracker.Clear();
        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var conversion = await context.AedDealConversions
            .Include(x => x.Deal).ThenInclude(x => x.SourceCurrency)
            .SingleOrDefaultAsync(x => x.Id == conversionId, cancellationToken)
            ?? throw new KeyNotFoundException("تبدیل معامله یافت نشد.");
        if (conversion.Status != "Posted")
            throw new InvalidOperationException("این تبدیل قبلاً برگشت داده شده است.");
        var originalEntries = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == conversion.PostingTransactionId)
            .ToListAsync(cancellationToken);
        if (originalEntries.Count == 0)
            throw new InvalidOperationException("سند حسابداری تبدیل یافت نشد.");

        var reversal = await CreateReversalTransactionAsync(
            conversion.PostingTransactionId, "AEDCR", $"برگشت تبدیل معامله {conversion.Deal.DealNumber}: {reason.Trim()}", cancellationToken);
        foreach (var entry in originalEntries)
            context.LedgerEntries.Add(NewEntry(reversal.Id, entry.AccountId, entry.CurrencyId,
                entry.BadehKar, entry.TalabKar, $"برگشت: {entry.Description}"));

        conversion.Status = "Reversed";
        conversion.ReversalTransactionId = reversal.Id;
        conversion.ReversalReason = reason.Trim();
        conversion.ReversedAt = DateTime.UtcNow;
        conversion.ReversedBy = context.RequireCurrentUserId();
        conversion.Deal.ConvertedAmount -= conversion.SourceAmount;
        conversion.Deal.TotalFinalUsd -= conversion.FinalUsdAmount;
        conversion.Deal.TotalProfitUsd -= conversion.ProfitUsd;
        conversion.Deal.Status = conversion.Deal.ConvertedAmount <= 0 ? "Held" : "PartiallyConverted";
        await CancelOriginalTransactionAsync(conversion.PostingTransactionId, reason, cancellationToken);
        AddAudit("REVERSE_AED_CONVERSION", "AedDealConversions", conversion.Id,
            $"تبدیل معامله {conversion.Deal.DealNumber} برگشت داده شد.");
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    public async Task CancelAsync(long dealId, string reason, CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        context.ChangeTracker.Clear();
        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var deal = await context.AedDeals.Include(x => x.Conversions)
            .SingleOrDefaultAsync(x => x.Id == dealId, cancellationToken)
            ?? throw new KeyNotFoundException("معامله یافت نشد.");
        if (deal.Status == "Cancelled")
            throw new InvalidOperationException("معامله قبلاً لغو شده است.");
        if (deal.Conversions.Any(x => x.Status == "Posted"))
            throw new InvalidOperationException("ابتدا تمام تبدیل‌های فعال این معامله را برگشت دهید.");
        var originalEntries = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == deal.HoldingTransactionId).ToListAsync(cancellationToken);
        var reversal = await CreateReversalTransactionAsync(
            deal.HoldingTransactionId, "AEDR", $"لغو معامله {deal.DealNumber}: {reason.Trim()}", cancellationToken);
        foreach (var entry in originalEntries)
            context.LedgerEntries.Add(NewEntry(reversal.Id, entry.AccountId, entry.CurrencyId,
                entry.BadehKar, entry.TalabKar, $"لغو: {entry.Description}"));

        deal.Status = "Cancelled";
        deal.ReversalTransactionId = reversal.Id;
        deal.CancelReason = reason.Trim();
        deal.CancelledAt = DateTime.UtcNow;
        deal.CancelledBy = context.RequireCurrentUserId();
        await CancelOriginalTransactionAsync(deal.HoldingTransactionId, reason, cancellationToken);
        AddAudit("CANCEL_AED_DEAL", "AedDeals", deal.Id, $"معامله {deal.DealNumber} لغو شد.");
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    private static void ValidateCreate(CreateAedDealDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DealNumber)) throw new InvalidOperationException("نمبر معامله الزامی است.");
        if (dto.SourceCorrespondentId <= 0 || dto.DubaiCorrespondentId <= 0) throw new InvalidOperationException("طرف کویته و طرف دبی الزامی است.");
        if (dto.SourceCorrespondentId == dto.DubaiCorrespondentId) throw new InvalidOperationException("طرف کویته و طرف دبی نمی‌تواند یکسان باشد.");
        if (dto.Amount <= 0) throw new InvalidOperationException("مبلغ معامله باید بزرگ‌تر از صفر باشد.");
        if (dto.RoundingDecimalPlaces is < 0 or > 4) throw new InvalidOperationException("تعداد اعشار باید بین صفر تا چهار باشد.");
    }

    private AedConversionPreviewDto Calculate(AedDeal deal, PreviewAedConversionDto dto)
    {
        if (deal.Status == "Cancelled") throw new InvalidOperationException("معامله لغو شده قابل تبدیل نیست.");
        var remaining = deal.OriginalAmount - deal.ConvertedAmount;
        if (dto.SourceAmount <= 0 || dto.SourceAmount > remaining)
            throw new InvalidOperationException($"مبلغ تبدیل باید بین صفر و {remaining} {deal.SourceCurrency.Code} باشد.");
        var actualAdjusted = dto.SourceAmount + dto.SourceAmount / 100_000m * dto.ActualMarker;
        var declaredAdjusted = dto.SourceAmount + dto.SourceAmount / 100_000m * dto.DeclaredMarker;
        if (actualAdjusted <= 0 || declaredAdjusted <= 0)
            throw new InvalidOperationException("حاصل مشخصه معامله نباید منفی یا صفر شود.");
        var finalUsd = Round(ToUsd(deal.SourceCurrency.Code, actualAdjusted, deal.AedPerUsdRate), deal.RoundingDecimalPlaces);
        var declaredUsd = Round(ToUsd(deal.SourceCurrency.Code, declaredAdjusted, deal.AedPerUsdRate), deal.RoundingDecimalPlaces);
        if (finalUsd <= 0 || declaredUsd <= 0)
            throw new InvalidOperationException("حاصل تبدیل پس از گردکردن باید بزرگ‌تر از صفر باشد.");
        return new AedConversionPreviewDto
        {
            DealId = deal.Id, SourceCurrencyCode = deal.SourceCurrency.Code,
            SourceAmount = dto.SourceAmount, RemainingAmountBefore = remaining,
            AedPerUsdRate = deal.AedPerUsdRate, ActualMarker = dto.ActualMarker,
            DeclaredMarker = dto.DeclaredMarker, FinalUsdAmount = finalUsd,
            DeclaredUsdAmount = declaredUsd, ProfitUsd = finalUsd - declaredUsd,
            RoundingDecimalPlaces = deal.RoundingDecimalPlaces
        };
    }

    private async Task<AedDeal> GetDealForCalculationAsync(long id, CancellationToken cancellationToken, bool tracked = false)
    {
        var query = context.AedDeals.Include(x => x.SourceCurrency).Include(x => x.Conversions).AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
               ?? throw new KeyNotFoundException("معامله درهم یافت نشد.");
    }

    private async Task<AedDealDto> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var deal = await context.AedDeals.AsNoTracking().Include(x => x.SourceCorrespondent).Include(x => x.DubaiCorrespondent)
            .Include(x => x.SourceCurrency).Include(x => x.Conversions)
            .SingleAsync(x => x.Id == id, cancellationToken);
        return Map(deal);
    }

    private static AedDealDto Map(AedDeal x) => new()
    {
        Id = x.Id, DealNumber = x.DealNumber,
        SourceCorrespondentId = x.SourceCorrespondentId, SourceCorrespondentName = x.SourceCorrespondent.Name,
        DubaiCorrespondentId = x.DubaiCorrespondentId, DubaiCorrespondentName = x.DubaiCorrespondent.Name,
        SourceCurrencyId = x.SourceCurrencyId, SourceCurrencyCode = x.SourceCurrency.Code,
        OriginalAmount = x.OriginalAmount, ConvertedAmount = x.ConvertedAmount,
        TotalFinalUsd = x.TotalFinalUsd,
        TotalDeclaredUsd = x.Conversions.Where(c => c.Status == "Posted").Sum(c => c.DeclaredUsdAmount),
        TotalProfitUsd = x.TotalProfitUsd,
        AedPerUsdRate = x.AedPerUsdRate, RoundingDecimalPlaces = x.RoundingDecimalPlaces,
        Status = x.Status, Note = x.Note, CreatedAt = x.CreatedAt,
        Conversions = x.Conversions.OrderByDescending(c => c.CreatedAt).Select(c => new AedDealConversionDto
        {
            Id = c.Id, SourceAmount = c.SourceAmount, AedPerUsdRate = c.AedPerUsdRate,
            ActualMarker = c.ActualMarker, DeclaredMarker = c.DeclaredMarker,
            FinalUsdAmount = c.FinalUsdAmount, DeclaredUsdAmount = c.DeclaredUsdAmount,
            ProfitUsd = c.ProfitUsd,
            Status = c.Status, CreatedAt = c.CreatedAt
        }).ToList()
    };

    private async Task<(Correspondent Correspondent, Account Account)> GetCorrespondentAsync(long id, CancellationToken cancellationToken)
    {
        var correspondent = await context.Correspondents.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, cancellationToken)
            ?? throw new InvalidOperationException("نمایندگی فعال یافت نشد.");
        var accountId = await GetCorrespondentAccountIdAsync(id, cancellationToken);
        var account = await context.Accounts.SingleAsync(x => x.Id == accountId, cancellationToken);
        return (correspondent, account);
    }

    private async Task<long> GetCorrespondentAccountIdAsync(long id, CancellationToken cancellationToken) =>
        await context.Accounts.Where(x => x.CorrespondentId == id && !x.IsArchived).Select(x => (long?)x.Id)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("حساب فعال نمایندگی یافت نشد.");

    private async Task<Currency> GetDealCurrencyAsync(long id, CancellationToken cancellationToken)
    {
        var currency = await context.Currencies.SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("ارز فعال یافت نشد.");
        if (currency.Code is not ("AED" or "USD"))
            throw new InvalidOperationException("ارز معامله فقط می‌تواند AED یا USD باشد.");
        return currency;
    }

    private async Task<Account> GetOrCreateAccountAsync(string code, string name, string type, CancellationToken cancellationToken)
    {
        var account = await context.Accounts.SingleOrDefaultAsync(x => x.AccountCode == code, cancellationToken);
        if (account != null)
        {
            if (account.IsArchived || account.AccountType != type)
                throw new InvalidOperationException($"حساب {code} باید حساب فعال {name} باشد.");
            return account;
        }
        account = new Account { AccountCode = code, AccountName = name, AccountType = type, CreatedAt = DateTime.UtcNow };
        context.Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        return account;
    }

    private async Task<Transaction> CreateReversalTransactionAsync(long originalId, string prefix, string remarks, CancellationToken cancellationToken)
    {
        var transaction = new Transaction
        {
            TransactionNo = await GenerateTransactionNumberAsync(prefix, cancellationToken), TransactionType = "AedDealReversal",
            BranchId = await context.GetDefaultBranchIdAsync(cancellationToken), Status = "Paid", Remarks = remarks,
            CreatedBy = context.RequireCurrentUserId(), CreatedAt = DateTime.UtcNow, ReversedTransactionId = originalId
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    private async Task CancelOriginalTransactionAsync(long id, string reason, CancellationToken cancellationToken)
    {
        var original = await context.Transactions.SingleAsync(x => x.Id == id, cancellationToken);
        original.Status = "Cancel";
        original.CancelReason = reason.Trim();
        original.CancelledAt = DateTime.UtcNow;
        original.CancelledBy = context.RequireCurrentUserId();
    }

    private void AddAudit(string action, string table, long id, string value) => context.AuditLogs.Add(new AuditLog
    {
        UserId = context.RequireCurrentUserId(), Action = action, TableName = table,
        RecordId = id, NewValue = value, CreatedAt = DateTime.UtcNow
    });

    private async Task<string> GenerateTransactionNumberAsync(string prefix, CancellationToken cancellationToken)
    {
        var start = $"{prefix}-{DateTime.Now:yyyyMMdd}-";
        var last = await context.Transactions.Where(x => x.TransactionNo.StartsWith(start))
            .OrderByDescending(x => x.TransactionNo).Select(x => x.TransactionNo).FirstOrDefaultAsync(cancellationToken);
        var next = last != null && int.TryParse(last[(last.LastIndexOf('-') + 1)..], out var value) ? value + 1 : 1;
        return $"{start}{next:D4}";
    }

    private static LedgerEntry NewEntry(long transactionId, long accountId, long currencyId,
        decimal talabKar, decimal badehKar, string description) => new()
    {
        TransactionId = transactionId, AccountId = accountId, CurrencyId = currencyId,
        TalabKar = talabKar, BadehKar = badehKar, Description = description, CreatedAt = DateTime.UtcNow
    };

    private static decimal ToUsd(string sourceCode, decimal amount, decimal rate) =>
        sourceCode == "AED" ? amount / rate : amount;
    private static decimal Round(decimal value, int places) =>
        decimal.Round(value, Math.Clamp(places, 0, 4), MidpointRounding.AwayFromZero);
    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("دلیل برگشت الزامی است.");
        if (reason.Trim().Length > 500) throw new InvalidOperationException("دلیل برگشت نمی‌تواند بیشتر از ۵۰۰ حرف باشد.");
    }
}
