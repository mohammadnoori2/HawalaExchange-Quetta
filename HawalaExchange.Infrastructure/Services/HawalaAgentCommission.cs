using System.Data;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    public async Task<HawalaDto> UpdateAgentCommissionAsync(long id, UpdateHawalaAgentCommissionDto commission)
    {
        var owner = await _context.Hawalas.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.CorrespondentId).SingleOrDefaultAsync();
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (owner.HasValue) await CorrespondentCommissionPeriodLock.AcquireAsync(_context, owner.Value);
        var hawala = await _context.Hawalas.SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException("حواله یافت نشد.");
        // Always operate on this outgoing record, never redirect to its incoming source.
        await _context.Entry(hawala).ReloadAsync();
        if (hawala.CorrespondentId != owner)
            throw new InvalidOperationException("نمایندگی حواله تغییر کرده است؛ لیست را تازه کنید.");
        if (hawala.HawalaType != "HawalaSend" || !hawala.CorrespondentId.HasValue || hawala.CompensatesHawalaId.HasValue)
            throw new InvalidOperationException("ویرایش کمیشن فقط برای حواله ارسالی غیرجبرانی مجاز است.");
        if (hawala.Status == "Cancel")
            throw new InvalidOperationException("کمیشن حواله لغوشده قابل ویرایش نیست.");
        if (await _context.CorrespondentAccountPeriodHawalas.AnyAsync(x => x.HawalaId == id))
            throw new InvalidOperationException("کمیشن حواله مربوط به دوره بسته‌شده قابل ویرایش نیست.");
        if (await _context.AgentPaymentBillItems.AnyAsync(x => x.HawalaId == id && x.IsActive))
            throw new InvalidOperationException("حواله در بیل پرداخت عامل قرار دارد؛ بیل پرداخت‌نشده را ابتدا لغو کنید. بیل پرداخت‌شده قابل تغییر نیست.");
        if (hawala.AgentCommissionAmount is not > 0)
            throw new InvalidOperationException("برای این حواله ابتدا کمیشن نمایندگی ثبت کنید.");
        if (hawala.AgentCommissionAmount != commission.PreviousAmount || hawala.AgentCommissionCurrencyId != commission.PreviousCurrencyId)
            throw new InvalidOperationException("کمیشن تغییر کرده است؛ لیست را تازه کرده و دوباره تلاش کنید.");
        if (commission.Amount <= 0 || commission.Amount > 9999999999999999.99m || decimal.Round(commission.Amount, 2) != commission.Amount)
            throw new InvalidOperationException("مبلغ کمیشن باید مثبت و حداکثر دارای دو رقم اعشار باشد.");
        if (!await _context.Currencies.AnyAsync(x => x.Id == commission.CurrencyId && x.IsActive))
            throw new InvalidOperationException("ارز کمیشن معتبر یا فعال نیست.");
        if (commission.Amount == hawala.AgentCommissionAmount && commission.CurrencyId == hawala.AgentCommissionCurrencyId)
        {
            await transaction.CommitAsync();
            return _mapper.Map<HawalaDto>(hawala);
        }

        var previousAmount = hawala.AgentCommissionAmount.Value;
        var previousCurrency = hawala.AgentCommissionCurrencyId ?? hawala.ToCurrencyId;
        // Preserve the original posting account, including older manual sends posted to income.
        var counterAccount = await _context.LedgerEntries.Where(x => x.HawalaId == id && x.BadehKar > 0 &&
                (x.Account.AccountType == "Income" || x.Account.AccountType == "Expense"))
            .OrderBy(x => x.Id).Select(x => (long?)x.AccountId).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("ثبت حسابداری کمیشن قبلی پیدا نشد؛ ابتدا حسابداری حواله بررسی شود.");
        var correspondentAccount = await _context.Accounts.Where(x => x.CorrespondentId == hawala.CorrespondentId && !x.IsArchived)
            .Select(x => (long?)x.Id).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("حساب نمایندگی پیدا نشد.");
        var oldValue = JsonSerializer.Serialize(new { hawala.AgentCommissionAmount, hawala.AgentCommissionCurrencyId });
        var conversion = await DetachSettlementConversionAsync(id);
        var description = $"حواله ارسالی {hawala.Number}: اصلاح کمیشن نمایندگی";
        await CreateLedgerEntry(id, correspondentAccount, previousCurrency, 0, previousAmount, description);
        await CreateLedgerEntry(id, counterAccount, previousCurrency, previousAmount, 0, description);
        await CreateLedgerEntry(id, correspondentAccount, commission.CurrencyId, commission.Amount, 0, description);
        await CreateLedgerEntry(id, counterAccount, commission.CurrencyId, 0, commission.Amount, description);
        hawala.AgentCommissionAmount = commission.Amount;
        hawala.AgentCommissionCurrencyId = commission.CurrencyId;
        var importRow = await _context.HawalaImportRows.SingleOrDefaultAsync(x => x.GeneratedSendHawalaId == id);
        if (importRow != null)
        {
            importRow.AgentCommissionAmount = commission.Amount;
            importRow.AgentCommissionCurrencyId = commission.CurrencyId;
        }
        await _context.SaveChangesAsync();
        if (conversion != null) await ReconvertSettlementAsync(hawala, conversion);
        await _auditLogService.LogAsync("UPDATE", "Hawalas", id, oldValue,
            JsonSerializer.Serialize(new { hawala.AgentCommissionAmount, hawala.AgentCommissionCurrencyId }), GetCurrentUserId());
        await transaction.CommitAsync();
        return _mapper.Map<HawalaDto>(hawala);
    }
}
