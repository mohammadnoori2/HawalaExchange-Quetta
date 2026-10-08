using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    public async Task<BulkHawalaPaymentPreviewDto> PreviewBulkPaymentAsync(IReadOnlyCollection<long> ids, long paidFromAccountId)
    {
        var selectedIds = ValidateBulkPaymentIds(ids);
        var hawalas = await _context.Hawalas.AsNoTracking().Where(x => selectedIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
        return await BuildBulkPaymentPreviewAsync(selectedIds, hawalas, paidFromAccountId);
    }

    public async Task<int> PayBulkAsync(BulkHawalaPaymentRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = ValidateBulkPaymentIds(request.Items.Select(x => x.Id).ToArray());
        if (request.Items.Count != ids.Length || request.Items.Any(x => x.RowVersion.Length == 0))
            throw new InvalidOperationException("پیش‌نمایش اجرای گروهی معتبر نیست؛ دوباره پیش‌نمایش بگیرید.");
        GetCurrentUserId();
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Keep the existing destination numbering lock order before locking received rows.
            await LockRequestedOutgoingNumberAsync(null, request.PaidFromAccountId);
            await LockPaymentAccountAsync(request.PaidFromAccountId);
            var hawalas = await _context.Hawalas
                .FromSqlRaw("SELECT * FROM dbo.Hawalas WITH (UPDLOCK, HOLDLOCK)")
                .AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
            var preview = await BuildBulkPaymentPreviewAsync(ids, hawalas, request.PaidFromAccountId);
            if (!preview.CanExecute)
                throw new InvalidOperationException(string.Join("\n", preview.Errors.Concat(preview.Items.Where(x => x.Error != null).Select(x => $"حواله {x.Number}: {x.Error}"))));
            var versions = request.Items.ToDictionary(x => x.Id, x => x.RowVersion);
            if (hawalas.Any(x => !x.RowVersion.SequenceEqual(versions[x.Id])))
                throw new InvalidOperationException("یک یا چند حواله پس از پیش‌نمایش تغییر کرده‌اند؛ دوباره پیش‌نمایش بگیرید.");

            foreach (var hawala in hawalas)
            {
                // Refresh any entity previously tracked by this scoped context.
                var tracked = _context.ChangeTracker.Entries<Hawala>().FirstOrDefault(x => x.Entity.Id == hawala.Id);
                if (tracked != null) await tracked.ReloadAsync();
                await MarkAsPaidAsync(hawala.Id, new PayHawalaDto
                {
                    PaidFromAccountId = request.PaidFromAccountId,
                    ReceiverName = hawala.ReceiverName!, ReceiverFatherName = hawala.ReceiverFatherName,
                    ReceiverPhone = hawala.ReceiverPhone, ReceiverAddress = hawala.ReceiverAddress,
                    ReceiverTazkiraNumber = hawala.ReceiverTazkiraNumber, ReceiverTazkiraImagePath = hawala.ReceiverTazkiraImagePath,
                    AgentCommissionAmount = hawala.AgentCommissionAmount, AgentCommissionCurrencyId = hawala.AgentCommissionCurrencyId
                });
            }
            await SavePaymentBatchAsync(hawalas, preview);
            await transaction.CommitAsync();
            return hawalas.Count;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            // Saved changes were rolled back too; do not retain Paid entities or generated entries in memory.
            _context.ChangeTracker.Clear();
            if (ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 })
                throw new InvalidOperationException("عملیات هم‌زمان روی این حواله‌ها یا حساب انجام شد؛ دوباره پیش‌نمایش بگیرید.", ex);
            throw;
        }
    }

    private async Task LockPaymentAccountAsync(long accountId)
    {
        // Serialize payments sharing an account before checking balances or locking hawalas.
        await _context.Accounts.FromSqlRaw("SELECT * FROM dbo.Accounts WITH (UPDLOCK, HOLDLOCK)")
            .AsNoTracking().Where(x => x.Id == accountId).Select(x => x.Id).FirstOrDefaultAsync();
    }

    private static long[] ValidateBulkPaymentIds(IReadOnlyCollection<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0 || ids.Count > 1000 || ids.Any(x => x <= 0))
            throw new InvalidOperationException("برای اجرای گروهی بین 1 تا 1000 حواله انتخاب کنید.");
        return ids.Distinct().OrderBy(x => x).ToArray();
    }

    private async Task<BulkHawalaPaymentPreviewDto> BuildBulkPaymentPreviewAsync(long[] ids, List<Hawala> hawalas, long accountId)
    {
        var result = new BulkHawalaPaymentPreviewDto { PaidFromAccountId = accountId };
        if (hawalas.Count != ids.Length) result.Errors.Add("یک یا چند حواله یافت نشد یا در دسترس شما نیست.");
        var currencies = await _context.Currencies.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId);
        if (account == null || account.IsArchived || !new[] { "Cash", "Bank", "Customer", "Correspondent" }.Contains(account.AccountType))
            result.Errors.Add("حساب پرداخت معتبر و فعال انتخاب کنید.");
        else
        {
            result.AccountName = account.AccountName;
            result.IsCashOrBank = account.AccountType is "Cash" or "Bank";
            result.IsCorrespondent = account.AccountType == "Correspondent";
            if (result.IsCorrespondent && (!account.CorrespondentId.HasValue || !await _context.Correspondents.AnyAsync(x => x.Id == account.CorrespondentId && !x.IsArchived)))
                result.Errors.Add("نمایندگی مرتبط با حساب پرداخت معتبر و فعال نیست.");
            if (account.AccountType == "Customer" && (!account.CustomerId.HasValue || !await _context.Customers.AnyAsync(x => x.Id == account.CustomerId && !x.IsArchived)))
                result.Errors.Add("مشتری مرتبط با حساب پرداخت معتبر و فعال نیست.");
        }
        var totals = new Dictionary<long, BulkHawalaPaymentCurrencyDto>();
        foreach (var hawala in hawalas)
        {
            var amount = hawala.ToAmount ?? hawala.FromAmount;
            var issues = new List<string>();
            if (hawala.HawalaType != "HawalaReceive" || hawala.Status != "Pending") issues.Add("حواله دریافتی اجرا نشده نیست");
            if (string.IsNullOrWhiteSpace(hawala.ReceiverName)) issues.Add("نام گیرنده تکمیل نیست");
            if (amount <= 0) issues.Add("مبلغ پرداخت معتبر نیست");
            if (!currencies.TryGetValue(hawala.ToCurrencyId, out var currency) || !currency.IsActive) issues.Add("ارز پرداخت فعال نیست");
            if (hawala.AgentCommissionAmount < 0) issues.Add("کمیشن عامل پرداخت منفی است");
            if (hawala.AgentCommissionAmount > 0 && (!hawala.AgentCommissionCurrencyId.HasValue || !currencies.TryGetValue(hawala.AgentCommissionCurrencyId.Value, out var commissionCurrency) || !commissionCurrency.IsActive))
                issues.Add("ارز کمیشن عامل پرداخت معتبر نیست");
            result.Items.Add(new BulkHawalaPaymentItemDto
            {
                Id = hawala.Id, Number = hawala.Number, ReceiverName = hawala.ReceiverName ?? "",
                Amount = amount, CurrencyCode = currency?.Code ?? "?", RowVersion = hawala.RowVersion,
                Error = issues.Count == 0 ? null : string.Join("؛ ", issues)
            });
            AddAmount(hawala.ToCurrencyId, amount, false);
            if (hawala.AgentCommissionAmount > 0 && hawala.AgentCommissionCurrencyId.HasValue)
                AddAmount(hawala.AgentCommissionCurrencyId.Value, hawala.AgentCommissionAmount.Value, true);
        }
        if (account != null && !account.IsArchived)
        {
            var balances = (await BalanceService.ReadAccountBalanceAsync(_context, account.Id)).ToDictionary(x => x.CurrencyId, x => x.Balance);
            foreach (var total in totals.Values)
            {
                total.AccountBalance = balances.GetValueOrDefault(total.CurrencyId);
            }
        }
        result.Totals = totals.Values.OrderBy(x => x.CurrencyCode).ToList();
        return result;

        void AddAmount(long currencyId, decimal value, bool commission)
        {
            if (!totals.TryGetValue(currencyId, out var total))
                totals[currencyId] = total = new() { CurrencyId = currencyId, CurrencyCode = currencies.GetValueOrDefault(currencyId)?.Code ?? "?" };
            if (commission) total.AgentCommission += value; else total.Principal += value;
        }
    }
}
