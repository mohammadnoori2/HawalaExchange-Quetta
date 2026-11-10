using System.Data;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    public async Task<AgentBillDto> CreateHistoricalAgentReceiptAsync(AgentBillHistoricalDto request)
    {
        if (!request.Bill.HistoricalReceipt || !request.Bill.ConfirmPreviouslySettled || request.RequestId == Guid.Empty)
            throw new InvalidOperationException("تأیید کنید که این حواله‌ها قبلاً با عامل تسویه شده‌اند و رسید حاضر ثبت مالی جدید ندارد.");
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Mode = "HistoricalReceipt", request.Bill.CorrespondentId,
                Ids = request.Bill.HawalaIds.Distinct().OrderBy(x => x), request.Bill.RecipientName, request.Bill.RecipientPhone,
                request.Bill.Notes, request.Bill.PreviousSettlementReference, request.Bill.PreviewToken }))));
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context, request.Bill.CorrespondentId);
            await _context.EnsureCurrentTenantWriteAccessAsync();
            var key = request.RequestId.ToString();
            var previous = await _context.AgentPaymentBills.AsNoTracking().Where(x => x.CorrespondentId == request.Bill.CorrespondentId && x.SnapshotJson.Contains(key)).ToListAsync();
            foreach (var row in previous)
            {
                var saved = ReadAgentBill(row);
                if (saved.ExecutionRequestId != request.RequestId) continue;
                if (!saved.IsHistoricalReceipt || saved.ExecutionFingerprint != fingerprint)
                    throw new InvalidOperationException("این درخواست قبلاً با اطلاعات دیگری ثبت شده است؛ فرم را دوباره باز کنید.");
                await tx.CommitAsync(); return saved;
            }
            var dto = await PreviewAgentBillAsync(request.Bill);
            if (request.Bill.PreviewToken != dto.ConfirmationToken)
                throw new InvalidOperationException("جزئیات انتخاب تغییر کرده؛ پیش‌نمایش رسید را دوباره بگیرید.");
            dto.ExecutionRequestId = request.RequestId; dto.ExecutionFingerprint = fingerprint; dto.Status = "Historical";
            var bill = new AgentPaymentBill { CorrespondentId = dto.CorrespondentId, Status = "Historical", CreatedAt = dto.CreatedAt,
                CreatedBy = GetCurrentUserId(), SnapshotJson = JsonSerializer.Serialize(dto),
                Items = request.Bill.HawalaIds.Distinct().Select(id => new AgentPaymentBillItem { HawalaId = id }).ToList() };
            _context.AgentPaymentBills.Add(bill); await _context.SaveChangesAsync();
            await _auditLogService.LogAsync("CREATE_HISTORICAL_RECEIPT", "AgentPaymentBills", bill.Id, null,
                "صدور رسید بابت تسویه قبلی؛ بدون سند حسابداری، برداشت صندوق یا تغییر اجرای حواله‌ها", GetCurrentUserId());
            await tx.CommitAsync(); dto.Id = bill.Id; return dto;
        }
        catch { await tx.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    public async Task<AgentBillDto> ExecuteAndCreateAgentBillAsync(AgentBillExecuteDto request)
    {
        if (request.Bill.HistoricalReceipt) throw new InvalidOperationException("برای تسویه قبلی، گزینه صدور رسید بدون ثبت حسابداری را استفاده کنید.");
        if (request.RequestId == Guid.Empty) throw new InvalidOperationException("شناسه عملیات معتبر نیست؛ فرم را دوباره باز کنید.");
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { request.Bill.CorrespondentId, Ids = request.Bill.HawalaIds.Distinct().OrderBy(x => x),
                request.Bill.RecipientName, request.Bill.RecipientPhone, request.Bill.Notes, request.Bill.ConfirmUnsettled, request.Bill.PreviewToken,
                Accounts = request.Accounts.OrderBy(x => x.CurrencyId).Select(x => new { x.CurrencyId, x.AccountId }) }))));
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context, request.Bill.CorrespondentId);
            await _context.EnsureCurrentTenantWriteAccessAsync();
            var key = request.RequestId.ToString();
            var previous = await _context.AgentPaymentBills.AsNoTracking().Where(x => x.CorrespondentId == request.Bill.CorrespondentId && x.SnapshotJson.Contains(key)).ToListAsync();
            foreach (var row in previous)
            {
                var saved = ReadAgentBill(row);
                if (saved.ExecutionRequestId != request.RequestId) continue;
                if (saved.ExecutionFingerprint != fingerprint) throw new InvalidOperationException("این درخواست قبلاً با اطلاعات دیگری ثبت شده است؛ فرم را دوباره باز کنید.");
                await tx.CommitAsync();
                return saved;
            }
            var bill = await CreateAgentBillAsync(request.Bill);
            bill.ExecutionRequestId = request.RequestId; bill.ExecutionFingerprint = fingerprint;
            var stored = await _context.AgentPaymentBills.SingleAsync(x => x.Id == bill.Id);
            stored.SnapshotJson = JsonSerializer.Serialize(bill); await _context.SaveChangesAsync();
            await PayAgentBillAsync(new() { BillId = bill.Id, ConfirmExecution = true, Accounts = request.Accounts });
            var result = await GetAgentBillAsync(bill.Id);
            await tx.CommitAsync();
            return result;
        }
        catch { await tx.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    public async Task<AgentBillDto> PreviewAgentBillAsync(AgentBillCreateDto request)
    {
        var ids = ValidateBulkPaymentIds(request.HawalaIds);
        var rows = await _context.Hawalas.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
        await ValidateAgentHawalasAsync(request.CorrespondentId, ids, rows, historicalReceipt: request.HistoricalReceipt);
        return await BuildAgentBillAsync(request, rows);
    }

    private async Task ValidateAgentHawalasAsync(long correspondentId, long[] ids, List<Hawala> rows, long? currentBill = null, bool historicalReceipt = false)
    {
        if (rows.Count != ids.Length || rows.Any(x => x.IsDeleted || x.CorrespondentId != correspondentId ||
            x.HawalaType != "HawalaSend" || x.Status is not ("Pending" or "Paid")))
            throw new InvalidOperationException("فقط حواله‌های ارسالی معتبر و لغونشده همین نمایندگی را انتخاب کنید.");
        if (!await _context.Correspondents.AnyAsync(x => x.Id == correspondentId && !x.IsArchived))
            throw new InvalidOperationException("نمایندگی فعال یافت نشد.");
        if (await _context.AgentPaymentBillItems.AnyAsync(x => ids.Contains(x.HawalaId) && x.IsActive && (!currentBill.HasValue || x.BillId != currentBill)))
            throw new InvalidOperationException("بعضی حواله‌ها در بیل فعال دیگری هستند یا قبلاً از طریق بیل پرداخت شده‌اند.");
        if (historicalReceipt && rows.Any(x => x.Status != "Paid"))
            throw new InvalidOperationException("رسید تسویه قبلی فقط برای حواله‌های اجراشده قابل صدور است؛ وضعیت حواله‌های اجرا‌نشده را ابتدا بررسی کنید.");
        if (!historicalReceipt && await _context.CorrespondentSettlementConversionHawalas.AnyAsync(x => ids.Contains(x.HawalaId)))
            throw new InvalidOperationException("برای حواله تبدیل‌شده به ارز توافقی، ابتدا تبدیل را برگشت دهید؛ بیل به ارز اصلی حواله صادر می‌شود.");
        if(!historicalReceipt && await _context.MoneyExchangeOperations.AnyAsync(x=>x.LinkedHawalaId.HasValue && ids.Contains(x.LinkedHawalaId.Value) && !x.IsDeleted))
            throw new InvalidOperationException("ابتدا تبدیل مستقل اسعار این حواله را حذف کنید؛ بیل به ارز اصلی حواله صادر می‌شود.");
        if (rows.Any(x => (x.ToAmount ?? x.FromAmount) <= 0 || x.AgentCommissionAmount < 0 ||
            x.AgentCommissionAmount > 0 && !x.AgentCommissionCurrencyId.HasValue))
            throw new InvalidOperationException("مبلغ یا ارز کمیشن حواله معتبر نیست.");
    }

    private async Task<AgentBillDto> BuildAgentBillAsync(AgentBillCreateDto request, List<Hawala> rows)
    {
        if (request.HistoricalReceipt && (string.IsNullOrWhiteSpace(request.PreviousSettlementReference) || request.PreviousSettlementReference.Length > 200))
            throw new InvalidOperationException("مرجع تسویه قبلی (نمبر سند یا توضیح پرداخت قبلی) را حداکثر در 200 نویسه وارد کنید.");
        if (string.IsNullOrWhiteSpace(request.RecipientName) || request.RecipientName.Length > 200 ||
            request.RecipientPhone?.Length > 50 || request.Notes?.Length > 1000)
            throw new InvalidOperationException("نام دریافت‌کننده پول الزامی است؛ طول نام، تماس یا توضیحات معتبر نیست.");
        var currencies = await _context.Currencies.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var locationIds = rows.Where(x => x.PaymentLocationId.HasValue).Select(x => x.PaymentLocationId!.Value).Distinct().ToArray();
        var locations = await _context.PaymentLocations.AsNoTracking().Where(x => locationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var name = await _context.Correspondents.Where(x => x.Id == request.CorrespondentId).Select(x => x.Name).SingleAsync();
        var dto = new AgentBillDto { CorrespondentId = request.CorrespondentId, CorrespondentName = name,
            RecipientName = request.RecipientName.Trim(), RecipientPhone = request.RecipientPhone?.Trim(), Notes = request.Notes?.Trim(), CreatedAt = DateTime.UtcNow,
            IsHistoricalReceipt = request.HistoricalReceipt, PreviousSettlementReference = request.HistoricalReceipt ? request.PreviousSettlementReference?.Trim() : null };
        foreach (var h in rows)
        {
            var currency = currencies.GetValueOrDefault(h.ToCurrencyId);
            var commissionCurrency = currencies.GetValueOrDefault(h.AgentCommissionCurrencyId ?? h.ToCurrencyId);
            if (currency is null || h.AgentCommissionAmount > 0 && commissionCurrency is null ||
                !request.HistoricalReceipt && (!currency.IsActive || h.AgentCommissionAmount > 0 && commissionCurrency is not { IsActive: true }))
                throw new InvalidOperationException("ارز حواله یا کمیشن فعال نیست.");
            dto.Items.Add(new() { HawalaId = h.Id, Number = h.Number, ReferenceNumber = h.ReferenceNumber ?? "",
                SenderName = h.SenderName ?? "", ReceiverName = h.ReceiverName ?? "", CorrespondentName = name,
                PaymentLocation = locations.GetValueOrDefault(h.PaymentLocationId ?? 0) ?? "", RegisteredAt = h.CreatedAt,
                Amount = h.ToAmount ?? h.FromAmount, CurrencyId = h.ToCurrencyId, CurrencyCode = currency.Code,
                AgentCommission = h.AgentCommissionAmount ?? 0, CommissionCurrencyId = h.AgentCommissionCurrencyId,
                CommissionCurrencyCode = commissionCurrency?.Code ?? "" });
        }
        dto.Totals = dto.Items.Select(x => new BulkHawalaPaymentCurrencyDto { CurrencyId=x.CurrencyId, CurrencyCode=x.CurrencyCode, Principal=x.Amount })
            .Concat(dto.Items.Where(x => x.AgentCommission > 0).Select(x => new BulkHawalaPaymentCurrencyDto {
                CurrencyId=x.CommissionCurrencyId!.Value, CurrencyCode=x.CommissionCurrencyCode, AgentCommission=x.AgentCommission }))
            .GroupBy(x => new { x.CurrencyId, x.CurrencyCode }).Select(g => new BulkHawalaPaymentCurrencyDto {
                CurrencyId=g.Key.CurrencyId, CurrencyCode=g.Key.CurrencyCode, Principal=g.Sum(x => x.Principal), AgentCommission=g.Sum(x => x.AgentCommission) })
            .OrderBy(x => x.CurrencyCode).ToList();
        dto.ConfirmationToken=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { dto.CorrespondentId, dto.RecipientName, dto.RecipientPhone, dto.Notes, dto.Items, dto.IsHistoricalReceipt, dto.PreviousSettlementReference }))));
        return dto;
    }

    public async Task<AgentBillDto> CreateAgentBillAsync(AgentBillCreateDto request)
    {
        if (request.HistoricalReceipt) throw new InvalidOperationException("برای تسویه قبلی، گزینه صدور رسید بدون ثبت حسابداری را استفاده کنید.");
        if (!request.ConfirmUnsettled) throw new InvalidOperationException("ابتدا بررسی و تأیید کنید که این حواله‌ها قبلاً با عامل تسویه نشده‌اند.");
        await using var tx = _context.Database.CurrentTransaction is null ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        try
        {
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context, request.CorrespondentId);
            var dto = await PreviewAgentBillAsync(request);
            if (request.PreviewToken != dto.ConfirmationToken)
                throw new InvalidOperationException("جزئیات انتخاب یا مبلغ تغییر کرده؛ پیش‌نمایش بیل را دوباره بگیرید.");
            var bill = new AgentPaymentBill { CorrespondentId=request.CorrespondentId, CreatedBy=GetCurrentUserId(),
                CreatedAt=dto.CreatedAt, SnapshotJson=JsonSerializer.Serialize(dto),
                Items=request.HawalaIds.Distinct().Select(id => new AgentPaymentBillItem { HawalaId=id }).ToList() };
            _context.AgentPaymentBills.Add(bill);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync("CREATE", "AgentPaymentBills", bill.Id, null, "صدور بیل؛ عدم تسویه قبلی توسط کاربر تأیید شد.", GetCurrentUserId());
            if(tx is not null) await tx.CommitAsync(); dto.Id=bill.Id; return dto;
        }
        catch { if(tx is not null) await tx.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    public async Task<AgentBillDto> GetAgentBillAsync(long id)
    {
        var bill = await _context.AgentPaymentBills.AsNoTracking().SingleOrDefaultAsync(x => x.Id==id)
            ?? throw new InvalidOperationException("بیل یافت نشد یا در دسترس نیست.");
        return ReadAgentBill(bill);
    }

    public async Task<IReadOnlyList<AgentBillDto>> GetAgentBillsForHawalasAsync(long correspondentId, IReadOnlyCollection<long> ids)
    {
        var keys = ValidateBulkPaymentIds(ids);
        var bills = await _context.AgentPaymentBills.AsNoTracking()
            .Where(x => x.CorrespondentId == correspondentId && x.Status != "Cancelled" && x.Items.Any(i => keys.Contains(i.HawalaId) && i.IsActive))
            .OrderByDescending(x => x.Id).ToListAsync();
        return bills.Select(ReadAgentBill).ToList();
    }

    public async Task<IReadOnlyList<AccountDto>> GetAgentBillPaymentAccountsAsync() =>
        await _context.Accounts.AsNoTracking().Where(x => !x.IsArchived && (x.AccountType == "Cash" || x.AccountType == "Bank"))
            .OrderBy(x => x.Id).Select(x => new AccountDto { Id = x.Id, AccountCode = x.AccountCode, AccountName = x.AccountName,
                AccountType = x.AccountType, IsActive = true }).ToListAsync();

    private static AgentBillDto ReadAgentBill(AgentPaymentBill bill)
    {
        var dto=JsonSerializer.Deserialize<AgentBillDto>(bill.SnapshotJson) ?? throw new InvalidOperationException("جزئیات بیل معتبر نیست.");
        dto.Id=bill.Id; dto.Status=bill.Status; dto.CreatedAt=bill.CreatedAt; dto.PaidAt=bill.PaidAt; dto.CancelledAt=bill.CancelledAt;
        return dto;
    }

    public async Task<PaginatedResult<AgentBillDto>> GetAgentBillsAsync(long correspondentId, int page=1, int pageSize=20)
    {
        page=Math.Max(1,page); pageSize=Math.Clamp(pageSize,1,100);
        var query=_context.AgentPaymentBills.AsNoTracking().Where(x => x.CorrespondentId==correspondentId);
        var count=await query.CountAsync();
        var bills=await query.OrderByDescending(x => x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync();
        return new PaginatedResult<AgentBillDto> { Items=bills.Select(ReadAgentBill).ToList(), TotalCount=count, PageNumber=page, PageSize=pageSize, TotalPages=(count+pageSize-1)/pageSize };
    }

    public async Task ConfirmAgentExecutionAsync(long correspondentId, IReadOnlyCollection<long> ids)
    {
        var keys=ValidateBulkPaymentIds(ids);
        await using var tx=await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context,correspondentId);
            await _context.EnsureCurrentTenantWriteAccessAsync();
            var rows=await _context.Hawalas.AsNoTracking().Where(x => keys.Contains(x.Id)).ToListAsync();
            if(rows.Count!=keys.Length || rows.Any(x => x.IsDeleted || x.HawalaType!="HawalaSend" || x.CorrespondentId!=correspondentId || x.Status is not ("Pending" or "Paid")))
                throw new InvalidOperationException("حواله‌های معتبر همین نمایندگی را انتخاب کنید.");
            var now=DateTime.UtcNow; var user=GetCurrentUserId();
            await _context.Hawalas.Where(x => keys.Contains(x.Id) && x.Status=="Pending").ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status,"Paid").SetProperty(x => x.PaidAt,now).SetProperty(x => x.PaidBy,user));
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync("EXECUTE", "Correspondents", correspondentId, null, $"تأیید اجرای {keys.Length} حواله بدون برداشت از صندوق",GetCurrentUserId());
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    public async Task PayAgentBillAsync(AgentBillPayDto request)
    {
        var owner=await _context.AgentPaymentBills.AsNoTracking().Where(x => x.Id==request.BillId).Select(x => (long?)x.CorrespondentId).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("بیل یافت نشد.");
        await using var tx=_context.Database.CurrentTransaction is null ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        try
        {
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context,owner);
            var bill=await _context.AgentPaymentBills.Include(x => x.Items).SingleAsync(x => x.Id==request.BillId);
            await _context.Entry(bill).ReloadAsync();
            if (ReadAgentBill(bill).IsHistoricalReceipt) throw new InvalidOperationException("این رسید بابت تسویه قبلی است و قابل پرداخت دوباره نیست.");
            if(bill.Status=="Paid") { if(tx is not null) await tx.CommitAsync(); return; }
            if(bill.Status!="Pending") throw new InvalidOperationException("بیل لغوشده قابل پرداخت نیست.");
            var snapshot=ReadAgentBill(bill); var ids=bill.Items.Select(x => x.HawalaId).ToArray();
            var rows=await _context.Hawalas.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
            await ValidateAgentHawalasAsync(owner,ids,rows,bill.Id);
            var fresh=await BuildAgentBillAsync(new AgentBillCreateDto { CorrespondentId=owner, RecipientName=snapshot.RecipientName, RecipientPhone=snapshot.RecipientPhone, Notes=snapshot.Notes },rows);
            if(JsonSerializer.Serialize(fresh.Items)!=JsonSerializer.Serialize(snapshot.Items))
                throw new InvalidOperationException("جزئیات حواله پس از صدور بیل تغییر کرده؛ بیل را لغو و دوباره صادر کنید.");
            if(rows.Any(x => x.Status!="Paid") && !request.ConfirmExecution)
                throw new InvalidOperationException("بعضی حواله‌ها اجرا نشده‌اند؛ ابتدا اجرا را تأیید کنید یا اجرا و پرداخت هم‌زمان را انتخاب کنید.");
            var needed=snapshot.Totals.Select(x => x.CurrencyId).OrderBy(x => x).ToArray();
            if(request.Accounts.Count!=needed.Length || !request.Accounts.Select(x => x.CurrencyId).OrderBy(x => x).SequenceEqual(needed))
                throw new InvalidOperationException("برای هر ارز دقیقاً یک حساب پرداخت انتخاب کنید.");
            var accountIds=request.Accounts.Select(x => x.AccountId).Distinct().OrderBy(x => x).ToArray();
            foreach(var id in accountIds) await LockPaymentAccountAsync(id);
            var cash=await _context.Accounts.Where(x => accountIds.Contains(x.Id) && !x.IsArchived && (x.AccountType=="Cash" || x.AccountType=="Bank")).ToListAsync();
            if(cash.Count!=accountIds.Length) throw new InvalidOperationException("حساب پرداخت باید صندوق یا بانک فعال همین صرافی باشد.");
            var agent=await _context.Accounts.Where(x => x.CorrespondentId==owner && !x.IsArchived).OrderBy(x => x.Id).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("حساب نمایندگی یافت نشد.");
            var now=DateTime.UtcNow; var user=GetCurrentUserId();
            if(await _context.CorrespondentAccountPeriods.AnyAsync(x => x.CorrespondentId==owner && x.PeriodTo>=now))
                throw new InvalidOperationException("دوره باز برای پرداخت موجود نیست.");
            await _context.Hawalas.Where(x => ids.Contains(x.Id) && x.Status=="Pending").ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status,"Paid").SetProperty(x => x.PaidAt,now).SetProperty(x => x.PaidBy,user));
            var posted = new List<LedgerEntry>();
            foreach(var item in snapshot.Items)
            {
                Add(item.HawalaId,item.CurrencyId,item.Amount);
                if(item.AgentCommission>0) Add(item.HawalaId,item.CommissionCurrencyId!.Value,item.AgentCommission);
            }
            void Add(long hawalaId,long currencyId,decimal amount)
            {
                var from=request.Accounts.Single(x => x.CurrencyId==currencyId).AccountId;
                posted.Add(new LedgerEntry { HawalaId=hawalaId,AccountId=agent.Id,CurrencyId=currencyId,BadehKar=amount,CreatedAt=now,Description=$"تسویه بیل عامل پرداخت #{bill.Id}" });
                posted.Add(new LedgerEntry { HawalaId=hawalaId,AccountId=from,CurrencyId=currencyId,TalabKar=amount,CreatedAt=now,Description=$"پرداخت بیل عامل #{bill.Id} به {snapshot.RecipientName}" });
            }
            bill.Status="Paid"; bill.PaidAt=now; bill.PaidBy=user; snapshot.PaymentAccounts=request.Accounts;
            bill.SnapshotJson=JsonSerializer.Serialize(snapshot);
            _context.LedgerEntries.AddRange(posted);
            await _context.SaveChangesAsync();
            snapshot.PaymentLedgerEntryIds = posted.Select(x => x.Id).ToList();
            bill.SnapshotJson = JsonSerializer.Serialize(snapshot);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync("PAY", "AgentPaymentBills",bill.Id,null,"پرداخت و تسویه بیل عامل؛ اصل و کمیشن دوباره شناسایی نشد.",user);
            if(tx is not null) await tx.CommitAsync();
        }
        catch { if(tx is not null) await tx.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    public async Task CancelAgentBillAsync(long id)
    {
        var owner=await _context.AgentPaymentBills.AsNoTracking().Where(x => x.Id==id).Select(x => (long?)x.CorrespondentId).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("بیل یافت نشد.");
        await using var tx=await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context,owner);
            var bill=await _context.AgentPaymentBills.Include(x => x.Items).SingleAsync(x => x.Id==id);
            await _context.Entry(bill).ReloadAsync();
            await _context.EnsureCurrentTenantWriteAccessAsync();
            var historical = ReadAgentBill(bill).IsHistoricalReceipt;
            var wasPaid = bill.Status == "Paid" && !historical;
            if(wasPaid) await ReverseAgentBillPaymentAsync(bill);
            if(bill.Status!="Cancelled") {
                bill.Status="Cancelled"; bill.CancelledAt=DateTime.UtcNow; bill.CancelledBy=GetCurrentUserId();
                foreach(var i in bill.Items) i.IsActive=false;
                await _context.SaveChangesAsync();
                await _auditLogService.LogAsync("CANCEL","AgentPaymentBills",id,null,historical ? "لغو رسید تسویه قبلی؛ بدون برگشت پرداخت یا تغییر حسابداری" : wasPaid ? "لغو بیل و برگشت پرداخت؛ اجرای حواله‌ها حفظ شد." : "لغو بیل پرداخت‌نشده و آزادسازی حواله‌ها",GetCurrentUserId());
            }
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); _context.ChangeTracker.Clear(); throw; }
    }

    private async Task ReverseAgentBillPaymentAsync(AgentPaymentBill bill)
    {
        var snapshot = ReadAgentBill(bill);
        var ids = snapshot.Items.Select(x => x.HawalaId).ToArray();
        var cashIds = snapshot.PaymentAccounts.Select(x => x.AccountId).Distinct().OrderBy(x => x).ToArray();
        if (cashIds.Length == 0 || bill.PaidAt is null) throw new InvalidOperationException("اطلاعات حساب پرداخت این بیل کامل نیست؛ برگشت خودکار مجاز نیست.");
        foreach (var accountId in cashIds) await LockPaymentAccountAsync(accountId);
        var now = DateTime.UtcNow;
        if (await _context.CorrespondentAccountPeriods.AnyAsync(x => x.CorrespondentId == bill.CorrespondentId && x.PeriodTo >= now))
            throw new InvalidOperationException("برای برگشت پرداخت بیل، دوره باز در تاریخ امروز لازم است.");
        var query = _context.LedgerEntries.AsNoTracking();
        var entries = snapshot.PaymentLedgerEntryIds.Count > 0
            ? await query.Where(x => snapshot.PaymentLedgerEntryIds.Contains(x.Id)).ToListAsync()
            : await query.Where(x => ids.Contains(x.HawalaId ?? 0) && x.CreatedAt == bill.PaidAt &&
                (x.Description == $"تسویه بیل عامل پرداخت #{bill.Id}" || x.Description == $"پرداخت بیل عامل #{bill.Id} به {snapshot.RecipientName}")).ToListAsync();
        if (entries.Count == 0 || (snapshot.PaymentLedgerEntryIds.Count > 0 && entries.Count != snapshot.PaymentLedgerEntryIds.Count) ||
            entries.Any(x => !x.HawalaId.HasValue || !ids.Contains(x.HawalaId.Value) || x.CreatedAt != bill.PaidAt || x.TalabKar < 0 || x.BadehKar < 0))
            throw new InvalidOperationException("اسناد پرداخت این بیل کامل نیست؛ برگشت خودکار متوقف شد.");
        var agentIds = entries.Where(x => x.BadehKar > 0).Select(x => x.AccountId).Distinct().ToArray();
        if (agentIds.Length != 1 || !await _context.Accounts.AnyAsync(x => x.Id == agentIds[0] && x.CorrespondentId == bill.CorrespondentId))
            throw new InvalidOperationException("حساب تسویه بیل با نمایندگی مطابقت ندارد.");
        var expected = new Dictionary<(long Hawala, long Account, long Currency), decimal>();
        foreach (var item in snapshot.Items)
        {
            AddExpected(item.HawalaId, item.CurrencyId, item.Amount);
            if (item.AgentCommission > 0) AddExpected(item.HawalaId, item.CommissionCurrencyId!.Value, item.AgentCommission);
        }
        void AddExpected(long hawala, long currency, decimal amount)
        {
            var cash = snapshot.PaymentAccounts.Single(x => x.CurrencyId == currency).AccountId;
            var credit = (hawala, cash, currency); var debit = (hawala, agentIds[0], currency);
            expected[credit] = expected.GetValueOrDefault(credit) + amount;
            expected[debit] = expected.GetValueOrDefault(debit) - amount;
        }
        var actual = entries.GroupBy(x => (x.HawalaId!.Value, x.AccountId, x.CurrencyId)).ToDictionary(g => g.Key, g => g.Sum(x => x.TalabKar - x.BadehKar));
        if (actual.Count != expected.Count || expected.Any(x => actual.GetValueOrDefault(x.Key) != x.Value))
            throw new InvalidOperationException("مبالغ سند پرداخت با بیل مطابقت ندارد؛ برگشت خودکار متوقف شد.");
        foreach (var entry in entries)
            _context.LedgerEntries.Add(new LedgerEntry { HawalaId = entry.HawalaId, AccountId = entry.AccountId, CurrencyId = entry.CurrencyId,
                TalabKar = entry.BadehKar, BadehKar = entry.TalabKar, CreatedAt = now, Description = $"برگشت پرداخت بیل عامل #{bill.Id}" });
    }
}
