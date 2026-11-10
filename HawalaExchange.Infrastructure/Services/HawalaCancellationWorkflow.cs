using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    private sealed record CancellationPlan(Hawala Root, List<Hawala> Affected, HawalaCancellationPreviewDto Preview);

    public async Task<HawalaCancellationPreviewDto> GetCancellationPreviewAsync(long id)
        => (await BuildCancellationPlanAsync(id)).Preview;

    private async Task<CancellationPlan> BuildCancellationPlanAsync(long id)
    {
        var requested = await _context.Hawalas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException("حواله یافت نشد.");
        if (requested.CompensatesHawalaId.HasValue)
            throw new InvalidOperationException("حواله جبرانی به لغو اصلی متصل است و مستقلاً قابل لغو، حذف یا ویرایش نیست.");
        var rootId = requested.IsSystemGenerated && requested.SourceHawalaId.HasValue ? requested.SourceHawalaId.Value : requested.Id;
        var root = await _context.Hawalas.AsNoTracking().Include(x => x.PaymentLocation).SingleAsync(x => x.Id == rootId);
        if (root.Status == "Cancel" && root.CancellationAccountingMode is null)
            throw new InvalidOperationException("این حواله پیش از فعال‌شدن روند جدید لغو شده است؛ جبران دوباره مجاز نیست.");
        var affected = await _context.Hawalas.AsNoTracking().Where(x => x.Id == rootId || x.SourceHawalaId == rootId).OrderBy(x => x.Id).ToListAsync();
        var linkedIds=affected.Select(x=>x.Id).ToArray();
        if(await _context.MoneyExchangeOperations.AnyAsync(x=>x.LinkedHawalaId.HasValue && linkedIds.Contains(x.LinkedHawalaId.Value) && !x.IsDeleted))
            throw new InvalidOperationException("ابتدا تبدیل مستقل اسعار حواله را حذف کنید، سپس لغو را انجام دهید.");
        var ids = affected.Select(x => x.Id).ToArray();
        var commissions = await _context.CorrespondentCommissionBatchItems.AsNoTracking()
            .Where(x => ids.Contains(x.HawalaId) && x.IsActive && x.Batch.Status == "Posted")
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.CommissionAfn, x.BatchId }).ToListAsync();
        var entries = await _context.LedgerEntries.AsNoTracking().Where(x => x.HawalaId.HasValue && ids.Contains(x.HawalaId.Value)).ToListAsync();
        var hasCommission = commissions.Count > 0 || affected.Any(x => x.CommissionAmount > 0 || x.AgentCommissionAmount > 0) || entries.Any(IsCommissionLedgerEntry);
        // Fees are intentionally preserved in every new cancellation mode.
        entries = entries.Where(x => !IsCommissionLedgerEntry(x)).ToList();
        var links = await _context.CorrespondentSettlementConversionHawalas.AsNoTracking()
            .Where(x => ids.Contains(x.HawalaId) && x.Conversion.Transaction.Status != "Cancel")
            .Select(x => new { x.ConversionId, x.HawalaId }).ToListAsync();
        var conversionIds = links.Select(x => x.ConversionId).Distinct().ToArray();
        var items = await _context.CorrespondentSettlementConversionHawalaItems.AsNoTracking()
            .Where(x => ids.Contains(x.HawalaId) && conversionIds.Contains(x.ConversionId))
            .Include(x => x.SourceCurrency).Include(x => x.Conversion).ThenInclude(x => x.TargetCurrency)
            .Include(x => x.Conversion).ThenInclude(x => x.Correspondent).Include(x => x.LedgerEntries).ToListAsync();
        items = items.OrderBy(x => x.Id).ToList();
        if (links.Any(link => !items.Any(item => item.ConversionId == link.ConversionId && item.HawalaId == link.HawalaId)))
            throw new InvalidOperationException("تبدیل قدیمی فاقد جزئیات مبلغ هر حواله است؛ لغو خودکار بدون ثبت دقیق نرخ و سهم حواله مجاز نیست.");
        entries.AddRange(items.SelectMany(x => x.LedgerEntries));
        entries = entries.DistinctBy(x => x.Id).ToList();
        var accountIds = entries.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = await _context.Accounts.AsNoTracking().Where(x => accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var currencyIds = entries.Select(x => x.CurrencyId).Distinct().ToArray();
        var currencies = await _context.Currencies.AsNoTracking().Where(x => currencyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var preview = new HawalaCancellationPreviewDto
        {
            HawalaId = root.Id, Number = root.Number, Sender = root.SenderName ?? "", Receiver = root.ReceiverName ?? "",
            PaymentLocation = root.PaymentLocation?.Name ?? "", HasCommission = hasCommission, HasConversion = links.Count > 0,
            IsCancelled = root.Status == "Cancel", AccountingMode = root.CancellationAccountingMode ?? "",
            IsClosedPeriod = await _context.CorrespondentAccountPeriodHawalas.AnyAsync(x => ids.Contains(x.HawalaId)),
            CompensationHawalaId = await _context.Hawalas.Where(x => x.CompensatesHawalaId == root.Id).Select(x => (long?)x.Id).SingleOrDefaultAsync(),
            Rates = items.Select(x => new HawalaCancellationRateDto { Correspondent = x.Conversion.Correspondent.Name,
                SourceCurrency = x.SourceCurrency.Code, TargetCurrency = x.Conversion.TargetCurrency.Code,
                SourceAmount = Math.Abs(x.SourceBadehKar - x.SourceTalabKar), TargetAmount = Math.Abs(x.TargetBadehKar - x.TargetTalabKar), Rate = x.ExchangeRate }).ToList(),
            Entries = entries.GroupBy(x => (x.AccountId, x.CurrencyId)).Select(g => new { g.Key, Net = g.Sum(x => x.BadehKar - x.TalabKar) })
                .Where(x => x.Net != 0).OrderBy(x => x.Key.AccountId).ThenBy(x => x.Key.CurrencyId)
                .Select(x => new HawalaCancellationEntryDto { AccountId = x.Key.AccountId, Account = accounts[x.Key.AccountId].AccountName,
                    CurrencyId = x.Key.CurrencyId, Currency = currencies[x.Key.CurrencyId].Code,
                    BadehKar = Math.Max(-x.Net, 0), TalabKar = Math.Max(x.Net, 0) }).ToList()
        };
        preview.UsdAmount = items.Where(x => x.HawalaId == root.Id && x.Conversion.TargetCurrency.Code == "USD")
            .Select(x => (decimal?)Math.Abs(x.TargetBadehKar - x.TargetTalabKar)).Sum();
        if (preview.UsdAmount is null or <= 0)
            preview.UsdAmount = preview.Entries.Where(x => x.Currency == "USD" && accounts[x.AccountId].CorrespondentId == root.CorrespondentId)
                .Select(x => (decimal?)Math.Max(x.BadehKar, x.TalabKar)).Sum();
        if ((preview.UsdAmount is null or <= 0) && root.CommissionBaseUsdAmount > 0) preview.UsdAmount = root.CommissionBaseUsdAmount;
        if (preview.RequiresCompensation && (preview.UsdAmount is null or <= 0))
            preview.UsdAmount = await (_context.Database.IsNpgsql()
                ? _context.Database.SqlQuery<decimal?>($"SELECT \"UsdAmount\" AS \"Value\" FROM public.\"CorrespondentSettlementValuations\" WHERE \"TenantId\"={_context.CurrentTenantId} AND \"HawalaId\"={root.Id}")
                : _context.Database.SqlQuery<decimal?>($"SELECT UsdAmount AS Value FROM dbo.CorrespondentSettlementValuations WHERE TenantId={_context.CurrentTenantId} AND HawalaId={root.Id}"))
                .SingleOrDefaultAsync();
        if (preview.RequiresCompensation && (preview.UsdAmount is null or <= 0))
            preview.UsdAmount = items.Where(x => x.Conversion.TargetCurrency.Code == "USD")
                .Select(x => (decimal?)Math.Abs(x.TargetBadehKar - x.TargetTalabKar)).Sum();
        preview.ConfirmationToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Tenant = _context.CurrentTenantId, Root = root.Id, Status = root.Status, root.CancellationAccountingMode,
            Versions = affected.Select(x => new { x.Id, Version = Convert.ToHexString(x.RowVersion) }),
            Commissions = commissions, Conversions = items.Select(x => new { x.Id, x.ExchangeRate, x.TargetTalabKar, x.TargetBadehKar }),
            preview.Entries, preview.IsClosedPeriod, preview.UsdAmount
        }))));
        return new(root, affected, preview);
    }

    private async Task<long> ResolveCancellationRootAsync(long id)
    {
        var requested = await _context.Hawalas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException("حواله یافت نشد.");
        return requested.IsSystemGenerated && requested.SourceHawalaId.HasValue ? requested.SourceHawalaId.Value : id;
    }

    private async Task LockCancellationAsync(long rootId)
    {
        var resource = $"HawalaCancellation:{_context.CurrentTenantId}:{rootId}";
        if(_context.Database.IsNpgsql())await HawalaExchange.Infrastructure.Data.DatabaseLocks.AcquireAsync(_context,resource);
        else await _context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @r int;
            EXEC @r=sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=5000;
            IF @r<0 THROW 50028,N'حواله در حال اصلاح است؛ دوباره تلاش کنید.',1;
            """);
        var owners = await _context.Hawalas.Where(x => x.Id == rootId || x.SourceHawalaId == rootId)
            .Where(x => x.CorrespondentId.HasValue).Select(x => x.CorrespondentId!.Value).Distinct().Order().ToListAsync();
        foreach (var owner in owners) await CorrespondentCommissionPeriodLock.AcquireAsync(_context, owner);
    }

    private async Task<HawalaDto> CancelWithAccountingAsync(long id, CancelHawalaDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CancelReason) || request.CancelReason.Trim().Length > 450)
            throw new InvalidOperationException("دلیل لغو را وارد کنید (حداکثر 450 حرف).");
        if (request.CompensationMode is not ("Manual" or "Automatic")) throw new InvalidOperationException("روش جبران معتبر نیست.");
        var rootId = await ResolveCancellationRootAsync(id);
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await LockCancellationAsync(rootId);
            var plan = await BuildCancellationPlanAsync(id);
            if (plan.Root.Id != rootId) throw new InvalidOperationException("ارتباط حواله تغییر کرده است؛ پیش‌نمایش را دوباره باز کنید.");
            if (plan.Preview.IsCancelled) return _mapper.Map<HawalaDto>(plan.Root);
            await AgentBillMaintenance.CancelUnpaidAsync(_context, plan.Affected.Select(x => x.Id).ToArray());
            if (request.ConfirmationToken != plan.Preview.ConfirmationToken)
                throw new InvalidOperationException("اطلاعات حواله تغییر کرده است؛ پیش‌نمایش لغو را دوباره باز کنید.");
            var tracked = await _context.Hawalas.Where(x => plan.Affected.Select(h => h.Id).Contains(x.Id)).ToListAsync();
            foreach (var hawala in tracked)
            {
                hawala.Status = "Cancel"; hawala.CancelledAt = DateTime.UtcNow; hawala.CancelledBy = GetCurrentUserId();
                hawala.CancelReason = request.CancelReason.Trim();
                hawala.CancellationAccountingMode = plan.Preview.RequiresCompensation ? "ManualPending" : "Immediate";
            }
            await _context.SaveChangesAsync();
            if (!plan.Preview.RequiresCompensation) AddCancellationEntries(plan, plan.Root.Id, "لغو حواله — خنثی‌سازی اصل مبلغ");
            else if (request.CompensationMode == "Automatic") await CreateCompensationAsync(plan, new() { ConfirmationToken = request.ConfirmationToken });
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync("CANCEL", "Hawalas", plan.Root.Id, plan.Root.Status, "Cancel: " + request.CancelReason.Trim(), GetCurrentUserId());
            await transaction.CommitAsync();
            return _mapper.Map<HawalaDto>(tracked.Single(x => x.Id == plan.Root.Id));
        }
        catch { await transaction.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    public async Task<HawalaDto> CompleteCancellationAsync(long id, HawalaCompensationRequestDto request)
    {
        var rootId = await ResolveCancellationRootAsync(id);
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await LockCancellationAsync(rootId);
            var plan = await BuildCancellationPlanAsync(id);
            if (plan.Root.Id != rootId) throw new InvalidOperationException("ارتباط حواله تغییر کرده است؛ پیش‌نمایش را دوباره باز کنید.");
            if (plan.Preview.CompensationHawalaId.HasValue)
                return _mapper.Map<HawalaDto>(await _context.Hawalas.SingleAsync(x => x.Id == plan.Preview.CompensationHawalaId));
            if (!plan.Preview.IsCancelled || plan.Root.CancellationAccountingMode != "ManualPending")
                throw new InvalidOperationException("این حواله در انتظار ثبت جبران نیست.");
            if (request.ConfirmationToken != plan.Preview.ConfirmationToken)
                throw new InvalidOperationException("اطلاعات جبران تغییر کرده است؛ پیش‌نمایش را دوباره باز کنید.");
            var result = await CreateCompensationAsync(plan, request);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return _mapper.Map<HawalaDto>(result);
        }
        catch { await transaction.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    private void AddCancellationEntries(CancellationPlan plan, long hawalaId, string description)
    {
        foreach (var row in plan.Preview.Entries)
            _context.LedgerEntries.Add(new LedgerEntry { HawalaId = hawalaId, AccountId = row.AccountId, CurrencyId = row.CurrencyId,
                BadehKar = row.BadehKar, TalabKar = row.TalabKar, Description = description, CreatedAt = DateTime.UtcNow });
    }

    private async Task<Hawala> CreateCompensationAsync(CancellationPlan plan, HawalaCompensationRequestDto request)
    {
        if (request.Manual && (request.Number is null or <= 0 || string.IsNullOrWhiteSpace(request.ReceiverName)))
            throw new InvalidOperationException("برای ثبت دستی، نمبر مسلسل و نام گیرنده را وارد کنید.");
        if (request.ReferenceNumber?.Length > 100 || request.ReceiverName?.Length > 200 || request.Notes?.Length > 800)
            throw new InvalidOperationException("طول مشخصات حواله جبرانی بیش از حد مجاز است.");
        var ids = plan.Preview.Entries.Select(x => x.AccountId).ToArray();
        var accounts = await _context.Accounts.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var target = plan.Preview.Entries.Where(x => x.TalabKar > 0 && accounts[x.AccountId].CorrespondentId.HasValue)
            .OrderByDescending(x => accounts[x.AccountId].CorrespondentId == plan.Root.CorrespondentId).ThenByDescending(x => x.Currency == "USD").FirstOrDefault()
            ?? throw new InvalidOperationException("حساب نمایندگی طلبکار برای حواله جبرانی مشخص نیست.");
        var source = plan.Preview.Entries.FirstOrDefault(x => x.BadehKar > 0 && accounts[x.AccountId].AccountType != "CurrencyConversionClearing")
            ?? throw new InvalidOperationException("حساب بدهکار برای حواله جبرانی مشخص نیست.");
        var owner = accounts[target.AccountId].CorrespondentId!.Value;
        await LockRequestedOutgoingNumberAsync(owner, null);
        var numbering = (await GetOutgoingNumberingAsync([owner]))[owner];
        var number = request.Manual ? request.Number!.Value : numbering.Next;
        if (number < numbering.Minimum)
            throw new InvalidOperationException("نمبر مسلسل 1 برای مانده طلبکاری دوره قبلی محفوظ است؛ حواله جبرانی از نمبر مسلسل 2 شروع می‌شود.");
        if (await _context.Hawalas.AnyAsync(x => x.CorrespondentId == owner && x.HawalaType == "HawalaSend" && x.Number == number &&
            !_context.CorrespondentAccountPeriodHawalas.Any(p => p.HawalaId == x.Id)))
            throw new InvalidOperationException("نمبر مسلسل حواله جبرانی تکراری است.");
        var usdId = await _context.Currencies.Where(x => x.Code == "USD").Select(x => x.Id).SingleAsync();
        if (plan.Preview.UsdAmount is null or <= 0) throw new InvalidOperationException("معادل دالری ثبت‌شده حواله مشخص نیست؛ نرخ امروز جایگزین نمی‌شود.");
        var simpleRate = source.CurrencyId == usdId ? null : plan.Preview.Rates
            .Where(x => x.SourceCurrency == source.Currency && x.TargetCurrency == "USD" &&
                x.SourceAmount == source.BadehKar && x.TargetAmount == plan.Preview.UsdAmount)
            .Select(x => (decimal?)x.Rate).FirstOrDefault();
        var compensation = new Hawala { CompensatesHawalaId = plan.Root.Id, Number = number, HawalaType = "HawalaSend", CorrespondentId = owner,
            IsSystemGenerated = !request.Manual,
            FromCurrencyId = source.CurrencyId, FromAmount = source.BadehKar, ToCurrencyId = usdId, ToAmount = plan.Preview.UsdAmount,
            ExchangeRate = simpleRate, PaidFromAccountId = source.AccountId, PaymentLocationId = plan.Root.PaymentLocationId,
            SenderName = plan.Root.ReceiverName, ReceiverName = request.Manual ? request.ReceiverName!.Trim() : plan.Root.SenderName,
            ReferenceNumber = request.ReferenceNumber?.Trim(), Notes = $"جبران لغو حواله مسلسل {plan.Root.Number}. {request.Notes}".Trim(),
            Status = "Paid", CreatedAt = DateTime.UtcNow, CreatedBy = GetCurrentUserId(), PaidAt = DateTime.UtcNow, PaidBy = GetCurrentUserId() };
        _context.Hawalas.Add(compensation); await _context.SaveChangesAsync();
        AddCancellationEntries(plan, compensation.Id, $"حواله جبرانی لغو مسلسل {plan.Root.Number} — اصل مبلغ با نرخ و ارز ثبت اصلی");
        var affectedIds = plan.Affected.Select(x => x.Id).ToArray();
        foreach (var row in await _context.Hawalas.Where(x => affectedIds.Contains(x.Id)).ToListAsync()) row.CancellationAccountingMode = "Compensated";
        await _auditLogService.LogAsync("CREATE", "Hawalas", compensation.Id, null, $"حواله جبرانی برای لغو {plan.Root.Id}", GetCurrentUserId());
        return compensation;
    }
}
