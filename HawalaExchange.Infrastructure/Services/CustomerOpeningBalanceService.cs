using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class CustomerService
{
    private async Task<Account> OpeningAccountAsync(long id, bool write)
    {
        var customer = await _context.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException("مشتری یافت نشد.");
        if (write && customer.IsArchived) throw new InvalidOperationException("ابتدا مشتری را از بایگانی خارج کنید.");
        return await _context.Accounts.SingleOrDefaultAsync(x => x.CustomerId == id && (!write || !x.IsArchived))
            ?? throw new KeyNotFoundException("حساب فعال مشتری یافت نشد.");
    }

    public async Task<IReadOnlyList<CustomerOpeningBalanceDto>> GetOpeningBalancesAsync(long id)
    {
        var account = await OpeningAccountAsync(id, false);

        var rows = await _context.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == account.Id && x.TransactionId != null && x.Transaction!.TransactionType == "OpeningBalance")
            .Where(x => x.Description == null || !x.Description.StartsWith("طرف مقابل"))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, TxId = x.TransactionId!.Value, x.Transaction!.TransactionNo, x.Transaction.Status,
                x.CurrencyId, Currency = x.Currency!.Code, x.BadehKar, x.TalabKar, x.CreatedAt, x.Description }).ToListAsync();
        var ids = rows.Select(x => x.Id).ToArray();
        var transactionIds = rows.Select(x => x.TxId).Distinct().ToArray();
        var opposite = await _context.LedgerEntries.AsNoTracking().Where(x => x.TransactionId != null && transactionIds.Contains(x.TransactionId.Value) && x.AccountId != account.Id)
            .Select(x => new { TxId = x.TransactionId!.Value, x.CurrencyId, Name = x.Account!.AccountName, Code = x.Account.AccountCode }).ToListAsync();
        var changes = await _context.CustomerOpeningBalanceChanges.AsNoTracking().Where(x => ids.Contains(x.OriginalLedgerEntryId))
            .ToDictionaryAsync(x => x.OriginalLedgerEntryId);
        return rows.Select(x => new CustomerOpeningBalanceDto
        {
            Id = x.Id, TransactionId = x.TxId, TransactionNo = x.TransactionNo, CurrencyId = x.CurrencyId, Currency = x.Currency,
            Amount = x.BadehKar + x.TalabKar, Direction = x.BadehKar > 0 ? "Debit" : "Credit", Date = x.CreatedAt,
            Description = OpeningNote(x.Description), IsClosedPeriod = false,
            OppositeAccount = string.Join("، ", opposite.Where(o => o.TxId == x.TxId && o.CurrencyId == x.CurrencyId).Select(o => o.Name).Distinct()),
            HasLegacyCounterparty = opposite.Any(o => o.TxId == x.TxId && o.CurrencyId == x.CurrencyId && o.Code != ApplicationDbContext.OpeningBalanceEquityAccountCode),
            State = changes.TryGetValue(x.Id, out var change) ? change.ReplacementTransactionId.HasValue ? "Edited" : "Deleted" : x.Status == "Cancel" ? "Deleted" : "Active",
            ReversalTransactionId = changes.GetValueOrDefault(x.Id)?.ReversalTransactionId,
            ReplacementTransactionId = changes.GetValueOrDefault(x.Id)?.ReplacementTransactionId
        }).ToList();
    }

    private static string OpeningNote(string? description)
    {
        var text = description ?? "";
        if (text.StartsWith("موجودی افتتاحیه مشتری:") || text.StartsWith("موجودی اولیه مشتری:"))
            text = text[(text.IndexOf(':') + 1)..].Trim();
        return text == "بدون توضیح" ? "" : text;
    }

    private static void ValidateOpening(CustomerOpeningBalanceRequestDto request)
    {
        if (request.CurrencyId <= 0 || request.Amount <= 0 || request.Amount > 999999999999m ||
            request.Direction is not ("Debit" or "Credit") || request.Description?.Length > 300 ||
            request.OpeningDate.Date < new DateTime(1900, 1, 1) || request.OpeningDate.Date > DateTime.Today)
            throw new InvalidOperationException("ارز، مبلغ مثبت، جهت بدهکار/طلبکار و تاریخ معتبر موجودی افتتاحیه را وارد کنید؛ یادداشت حداکثر 300 حرف است.");
        if (decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero) <= 0)
            throw new InvalidOperationException("مبلغ موجودی افتتاحیه پس از گرد کردن باید بزرگ‌تر از صفر باشد.");
    }

    private async Task EnsureOpeningCurrencyAvailableAsync(long accountId, long currencyId, long? except = null)
    {
        if (!await _context.Currencies.AnyAsync(x => x.Id == currencyId && x.IsActive))
            throw new InvalidOperationException("ارز انتخاب‌شده فعال و معتبر نیست.");
        if (await _context.LedgerEntries.AnyAsync(x => x.AccountId == accountId && x.CurrencyId == currencyId &&
            x.Id != except && x.Transaction != null && x.Transaction.TransactionType == "OpeningBalance" && x.Transaction.Status != "Cancel" &&
            !_context.CustomerOpeningBalanceChanges.Any(c => c.OriginalLedgerEntryId == x.Id)))
            throw new InvalidOperationException("موجودی افتتاحیه این ارز قبلاً ثبت شده است؛ همان ردیف را ویرایش کنید.");
    }

    private static Task<DateTime> OpeningPostingDateAsync(long id, DateTime requestedDate, DateTime? oldDate = null)
        => Task.FromResult(DateTime.SpecifyKind(requestedDate.Date, DateTimeKind.Unspecified).ToUniversalTime());

    private Task AcquireOpeningLockAsync(long id)
    {
        var resource = $"CustomerOpeningBalance:{_context.CurrentTenantId}:{id}";
        if(_context.Database.IsNpgsql())return DatabaseLocks.AcquireAsync(_context,resource);
        return _context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = N'Exclusive',
                @LockOwner = N'Transaction', @LockTimeout = 5000;
            IF @result < 0 THROW 50028, N'موجودی افتتاحیه مشتری در حال اصلاح است؛ دوباره تلاش کنید.', 1;
            """);
    }

    private async Task<Transaction> PostOpeningAsync(Account account, CustomerOpeningBalanceRequestDto request, DateTime date)
    {
        await _context.EnsureSystemAccountsAsync(_context.CurrentTenantId);
        var equity = await _context.Accounts.SingleAsync(x => x.AccountCode == ApplicationDbContext.OpeningBalanceEquityAccountCode);
        var transaction = await NewOpeningTransactionAsync("OpeningBalance", "OP", date, $"موجودی افتتاحیه مشتری {account.AccountName}");
        var amount = decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero);
        var debit = request.Direction == "Debit" ? amount : 0;
        var credit = request.Direction == "Credit" ? amount : 0;
        _context.LedgerEntries.AddRange(
            new LedgerEntry { TransactionId = transaction.Id, AccountId = account.Id, CurrencyId = request.CurrencyId,
                BadehKar = debit, TalabKar = credit, CreatedAt = date, Description = $"موجودی افتتاحیه مشتری: {request.Description?.Trim()}" },
            new LedgerEntry { TransactionId = transaction.Id, AccountId = equity.Id, CurrencyId = request.CurrencyId,
                BadehKar = credit, TalabKar = debit, CreatedAt = date, Description = $"طرف مقابل موجودی افتتاحیه مشتری {account.AccountName}" });
        await _context.SaveChangesAsync();
        return transaction;
    }

    private async Task<Transaction> NewOpeningTransactionAsync(string type, string prefix, DateTime date, string remarks)
    {
        var transaction = new Transaction { TransactionNo = $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}",
            TransactionType = type, CreatedAt = date, CreatedBy = _context.RequireCurrentUserId(),
            BranchId = await _context.GetDefaultBranchIdAsync(), Status = "Paid", Remarks = remarks };
        _context.Transactions.Add(transaction); await _context.SaveChangesAsync(); return transaction;
    }

    public async Task AddOpeningBalanceAsync(long id, CustomerOpeningBalanceRequestDto request)
    {
        ValidateOpening(request);
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await AcquireOpeningLockAsync(id);
            var account = await OpeningAccountAsync(id, true);
            await EnsureOpeningCurrencyAvailableAsync(account.Id, request.CurrencyId);
            var posted = await PostOpeningAsync(account, request, await OpeningPostingDateAsync(id, request.OpeningDate));
            await _auditLogService.LogAsync("CREATE", "Transactions", posted.Id, null, "ثبت موجودی افتتاحیه مشتری", _context.RequireCurrentUserId());
            await transaction.CommitAsync();
        }
        catch { await transaction.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    public Task UpdateOpeningBalanceAsync(long id, long openingId, CustomerOpeningBalanceRequestDto request)
        => ChangeOpeningAsync(id, openingId, request, "ویرایش موجودی افتتاحیه");

    public Task DeleteOpeningBalanceAsync(long id, long openingId, string? reason = null)
        => ChangeOpeningAsync(id, openingId, null, string.IsNullOrWhiteSpace(reason) ? "حذف موجودی افتتاحیه" : reason.Trim());

    private async Task ChangeOpeningAsync(long id, long openingId, CustomerOpeningBalanceRequestDto? replacement, string reason)
    {
        if (replacement is not null) ValidateOpening(replacement);
        if (reason.Length > 450) throw new InvalidOperationException("دلیل اصلاح حداکثر 450 حرف است.");
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await AcquireOpeningLockAsync(id);
            var account = await OpeningAccountAsync(id, true);
            var original = await _context.LedgerEntries.AsNoTracking().Include(x => x.Transaction)
                .SingleOrDefaultAsync(x => x.Id == openingId && x.AccountId == account.Id && x.Transaction != null && x.Transaction.TransactionType == "OpeningBalance" &&
                    (x.Description == null || !x.Description.StartsWith("طرف مقابل")))
                ?? throw new KeyNotFoundException("ردیف موجودی افتتاحیه این مشتری یافت نشد.");
            var previous = await _context.CustomerOpeningBalanceChanges.SingleOrDefaultAsync(x => x.OriginalLedgerEntryId == openingId);
            if (previous is not null || original.Transaction!.Status == "Cancel")
            {
                if (replacement is null) return;
                throw new InvalidOperationException("این موجودی قبلاً اصلاح شده است؛ فهرست را تازه کنید.");
            }
            if (replacement is not null) await EnsureOpeningCurrencyAvailableAsync(account.Id, replacement.CurrencyId, openingId);
            var opposite = await _context.LedgerEntries.AsNoTracking().Where(x => x.TransactionId == original.TransactionId &&
                x.AccountId != account.Id && x.CurrencyId == original.CurrencyId && x.BadehKar == original.TalabKar && x.TalabKar == original.BadehKar).ToListAsync();
            if (opposite.Count != 1) throw new InvalidOperationException("طرف مقابل ثبت قدیمی یکتا نیست؛ اصلاح خودکار بدون مشخص‌بودن سند مقابل مجاز نیست.");
            var effective = await OpeningPostingDateAsync(id, replacement?.OpeningDate ?? original.CreatedAt, original.CreatedAt);
            var reversalDate = original.CreatedAt;
            var reversal = await NewOpeningTransactionAsync("OpeningBalanceReversal", "OR", reversalDate, reason);
            foreach (var entry in new[] { original, opposite[0] })
                _context.LedgerEntries.Add(new LedgerEntry { TransactionId = reversal.Id, AccountId = entry.AccountId,
                    CurrencyId = entry.CurrencyId, BadehKar = entry.TalabKar, TalabKar = entry.BadehKar,
                    CreatedAt = reversalDate, Description = $"برگشت موجودی افتتاحیه — سند {original.Transaction.TransactionNo}" });
            await _context.SaveChangesAsync();
            var posted = replacement is null ? null : await PostOpeningAsync(account, replacement, effective);
            _context.CustomerOpeningBalanceChanges.Add(new() { OriginalLedgerEntryId = openingId, ReversalTransactionId = reversal.Id,
                ReplacementTransactionId = posted?.Id, ChangedBy = _context.RequireCurrentUserId(), Reason = reason });
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(replacement is null ? "DELETE" : "UPDATE", "LedgerEntries", openingId,
                original.Description, $"{reason}؛ سند برگشت {reversal.TransactionNo}؛ جایگزین {posted?.TransactionNo}", _context.RequireCurrentUserId());
            await transaction.CommitAsync();
        }
        catch { await transaction.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }
}
