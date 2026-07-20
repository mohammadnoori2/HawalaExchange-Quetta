    using AutoMapper;
    using HawalaExchange.Application.DTOs;
    using HawalaExchange.Application.Interfaces.Services;
    using HawalaExchange.Domain.Entities;
    using HawalaExchange.Infrastructure.Data;
    using Microsoft.EntityFrameworkCore;
    using System.Data;
    using System.Linq.Expressions;

    namespace HawalaExchange.Application.Services
    {
        public class HawalaService : IHawalaService
        {
            private readonly ApplicationDbContext _context;
            private readonly IMapper _mapper;
            private readonly ILedgerService _ledgerService;
            private readonly IAccountService _accountService;
            private readonly IAuditLogService _auditLogService;
            private readonly IFileService _fileService;


            public HawalaService(
                ApplicationDbContext context,
                IMapper mapper,
                ILedgerService ledgerService,
                IAccountService accountService,
                IAuditLogService auditLogService,
                IFileService fileService)
            {
                _context = context;
                _mapper = mapper;
                _ledgerService = ledgerService;
                _accountService = accountService;
                _auditLogService = auditLogService;
                _fileService = fileService;
            }

            public async Task<HawalaDto> CreateHawalaAsync(CreateHawalaDto dto)
            {
                using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                try
                {
                    ValidateCreateHawala(dto);

                    var hawala = _mapper.Map<Hawala>(dto);
                    hawala.CreatedAt = DateTime.UtcNow;
                    hawala.CreatedBy = GetCurrentUserId();
                    hawala.Status = dto.Status ?? "Pending";
                    if (hawala.Status == "Paid")
                    {
                        hawala.PaidAt = hawala.CreatedAt;
                        hawala.PaidBy = GetCurrentUserId();
                    }

                    // منطق شماره (نمبر)
                    if (dto.Number != null && dto.Number > 0)
                    {
                        hawala.Number = dto.Number;
                    }
                    else
                    {
                        //if (hawala.HawalaType == "HawalaReceive")
                        //    throw new InvalidOperationException("برای حواله آمد، شماره (نمبر) الزامی است.");

                        // تولید خودکار شماره برای حواله رفت و متفرقه
                        var lastNumber = await _context.Hawalas
                            .Where(h => h.CorrespondentId == hawala.CorrespondentId && h.HawalaType == hawala.HawalaType)
                            .OrderByDescending(h => h.Number)
                            .Select(h => (long?)h.Number)
                            .FirstOrDefaultAsync();

                        hawala.Number = (lastNumber ?? 0) + 1;
                    }

                    // بررسی یکتا بودن شماره
                    var exists = await _context.Hawalas
                        .AnyAsync(h => h.CorrespondentId == hawala.CorrespondentId &&
                                       h.HawalaType == hawala.HawalaType &&
                                       h.Number == hawala.Number);
                    if (exists)
                        throw new InvalidOperationException($"شماره {hawala.Number} برای حواله {hawala.HawalaType} این نمایندگی قبلاً ثبت شده است.");

                    await _context.Hawalas.AddAsync(hawala);
                    await _context.SaveChangesAsync();

                    // ثبت ورودی‌های دفتر کل
                    await ProcessLedgerEntries(hawala, dto.FromAccountId);
                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync("CREATE", "Hawalas", hawala.Id, null, $"حواله {hawala.HawalaType} با شماره {hawala.Number} ایجاد شد", GetCurrentUserId());

                    await transaction.CommitAsync();

                    return _mapper.Map<HawalaDto>(hawala);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            private async Task DeleteHawalaLedgerEntriesAsync(long hawalaId)
            {
                var ledgerEntries = await _context.LedgerEntries
                    .Where(x => x.HawalaId == hawalaId)
                    .ToListAsync();

                if (ledgerEntries.Any())
                {
                    _context.LedgerEntries.RemoveRange(ledgerEntries);
                }
            }
            public async Task<long> GetNextNumberAsync(long correspondentId, string hawalaType)
            {
                var lastNumber = await _context.Hawalas
                    .Where(h => h.CorrespondentId == correspondentId && h.HawalaType == hawalaType)
                    .OrderByDescending(h => h.Number)
                    .Select(h => (long?)h.Number)
                    .FirstOrDefaultAsync();

                return (lastNumber ?? 0) + 1;
            }

            public async Task<HawalaDto?> GetHawalaByIdAsync(long id)
            {
                var hawala = await _context.Hawalas
                    .Include(h => h.Correspondent)
                    .Include(h => h.FromCurrency)
                    .Include(h => h.ToCurrency)
                    .Include(h => h.CommissionCurrency)
                    .Include(h => h.AgentCommissionCurrency)
                    .Include(h => h.PaymentLocation)
                    .Include(h => h.PaidFromAccount)
                    .FirstOrDefaultAsync(h => h.Id == id);

                return hawala == null ? null : _mapper.Map<HawalaDto>(hawala);
            }

            public async Task<HawalaListResultDto> GetHawalasAsync(HawalaFilterDto filter)
            {
                var query = _context.Hawalas
                    .Include(h => h.Correspondent)
                    .Include(h => h.FromCurrency)
                    .Include(h => h.ToCurrency)
                    .Include(h => h.CommissionCurrency)
                    .Include(h => h.AgentCommissionCurrency)
                    .Include(h => h.PaymentLocation)
                    .Include(h => h.PaidFromAccount)
                    .AsQueryable();

                if (filter.Number  > 0)
                    query = query.Where(h => h.Number == filter.Number);

                if (!string.IsNullOrEmpty(filter.SearchTerm))
                {
                    var term = filter.SearchTerm.Trim();
                    query = query.Where(h =>
                        (h.SenderName != null && h.SenderName.Contains(term)) ||
                        (h.ReceiverName != null && h.ReceiverName.Contains(term)) ||
                        (h.ReferenceNumber != null && h.ReferenceNumber.Contains(term))
                    );
                }

                if (!string.IsNullOrEmpty(filter.HawalaType))
                    query = query.Where(h => h.HawalaType == filter.HawalaType);

                if (!string.IsNullOrEmpty(filter.Status))
                    query = query.Where(h => h.Status == filter.Status);

                if (filter.CorrespondentId.HasValue)
                    query = query.Where(h => h.CorrespondentId == filter.CorrespondentId);

                query = filter.SortDirection == "asc"
                    ? query.OrderBy(GetSortExpression(filter.SortColumn))
                    : query.OrderByDescending(GetSortExpression(filter.SortColumn));

                var totalCount = await query.CountAsync();

                if (filter.PageSize > 0)
                {
                    query = query.Skip((filter.PageNumber - 1) * filter.PageSize)
                                 .Take(filter.PageSize);
                }

                var items = await query.ToListAsync();
                return new HawalaListResultDto
                {
                    Items = _mapper.Map<List<HawalaDto>>(items),
                    TotalCount = totalCount,
                    TotalPages = (int)Math.Ceiling((double)totalCount / (filter.PageSize > 0 ? filter.PageSize : totalCount))
                };
            }

            private Expression<Func<Hawala, object>> GetSortExpression(string column)
            {
                return column switch
                {
                    "Number" => h => h.Number,
                    "HawalaType" => h => h.HawalaType,
                    "SenderName" => h => h.SenderName ?? "",
                    "ReceiverName" => h => h.ReceiverName ?? "",
                    "FromAmount" => h => h.FromAmount,
                    "Status" => h => h.Status,
                    "CreatedAt" => h => h.CreatedAt,
                    _ => h => h.Id
                };
            }

            public async Task<HawalaStatisticsDto> GetStatisticsAsync()
            {
                var all = await _context.Hawalas.ToListAsync();

                return new HawalaStatisticsDto
                {
                    HawalaSendCount = all.Count(h => h.HawalaType == "HawalaSend"),
                    HawalaReceiveCount = all.Count(h => h.HawalaType == "HawalaReceive"),
                    HawalaOtherCount = all.Count(h => h.HawalaType == "HawalaOther"),
                    PendingCount = all.Count(h => h.Status == "Pending"),
                    PaidCount = all.Count(h => h.Status == "Paid")
                };
            }

            public async Task<HawalaDto> UpdateHawalaAsync(long id, UpdateHawalaDto dto)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var hawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.Id == id);

                    if (hawala == null)
                        throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

                    if (hawala.Status == "Paid")
                        throw new InvalidOperationException("حواله پرداخت شده قابل ویرایش نیست.");

                    if (hawala.Status == "Cancel")
                        throw new InvalidOperationException("حواله لغو شده قابل ویرایش نیست.");

                    // حذف لیجرهای قبلی
                    await DeleteHawalaLedgerEntriesAsync(hawala.Id);

                    // آپدیت خود حواله
                    _mapper.Map(dto, hawala);

                    // ایجاد دوباره لیجرها براساس معلومات جدید
                    await ProcessLedgerEntries(hawala, dto.FromAccountId);

                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync(
                        "UPDATE",
                        "Hawalas",
                        hawala.Id,
                        null,
                        $"حواله با شناسه {hawala.Id} ویرایش شد و لیجر آن دوباره ساخته شد",
                        GetCurrentUserId());

                    await transaction.CommitAsync();

                    return _mapper.Map<HawalaDto>(hawala);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            public async Task DeleteHawalaAsync(long id)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var hawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.Id == id);

                    if (hawala == null)
                        throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

                    if (hawala.Status == "Paid")
                        throw new InvalidOperationException("حواله پرداخت شده قابل حذف نیست.");

                    // اول لیجرهای مربوط به حواله حذف شود
                    await DeleteHawalaLedgerEntriesAsync(hawala.Id);

                    // بعد خود حواله حذف شود
                    _context.Hawalas.Remove(hawala);
                    await _fileService.DeleteFileAsync(hawala.SenderTazkiraImagePath);
                    await _fileService.DeleteFileAsync(hawala.ReceiverTazkiraImagePath);
                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync(
                        "DELETE",
                        "Hawalas",
                        id,
                        null,
                        $"حواله با شناسه {id} و لیجرهای مربوطه حذف شد",
                        GetCurrentUserId());

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            public async Task<HawalaDto> MarkAsPaidAsync(long id, long paidFromAccountId)
            {
                var hawala = await _context.Hawalas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (hawala == null)
                    throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

                return await MarkAsPaidAsync(id, new PayHawalaDto
                {
                    PaidFromAccountId = paidFromAccountId,
                    ReceiverName = hawala.ReceiverName ?? string.Empty,
                    ReceiverFatherName = hawala.ReceiverFatherName,
                    ReceiverPhone = hawala.ReceiverPhone,
                    ReceiverTazkiraNumber = hawala.ReceiverTazkiraNumber,
                    ReceiverTazkiraImagePath = hawala.ReceiverTazkiraImagePath,
                    ReceiverAddress = hawala.ReceiverAddress
                });
            }

            public async Task<HawalaDto> MarkAsPaidAsync(long id, PayHawalaDto payment)
            {
                using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                try
                {
                    var hawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.Id == id);

                    if (hawala == null)
                        throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

                    if (hawala.Status == "Paid")
                        throw new InvalidOperationException("حواله قبلاً پرداخت شده است.");

                    if (hawala.Status == "Cancel")
                        throw new InvalidOperationException("حواله لغو شده قابل پرداخت نیست.");

                    if (payment.PaidFromAccountId <= 0)
                        throw new InvalidOperationException("انتخاب حساب پرداخت‌کننده الزامی است.");

                    if (string.IsNullOrWhiteSpace(payment.ReceiverName))
                        throw new InvalidOperationException("نام گیرنده الزامی است.");

                    hawala.ReceiverName = payment.ReceiverName.Trim();
                    hawala.ReceiverFatherName = payment.ReceiverFatherName?.Trim();
                    hawala.ReceiverPhone = payment.ReceiverPhone?.Trim();
                    hawala.ReceiverTazkiraNumber = payment.ReceiverTazkiraNumber?.Trim();
                    hawala.ReceiverTazkiraImagePath = payment.ReceiverTazkiraImagePath;
                    hawala.ReceiverAddress = payment.ReceiverAddress?.Trim();

                    if (hawala.HawalaType == "HawalaReceive")
                    {
                        await ProcessHawalaReceivePaymentAsync(hawala, payment.PaidFromAccountId);
                    }
                    else
                    {
                        throw new InvalidOperationException("فعلاً پرداخت مرحله‌ای فقط برای حواله دریافتی استفاده می‌شود.");
                    }

                    hawala.Status = "Paid";
                    hawala.PaidAt = DateTime.UtcNow;
                    hawala.PaidBy = GetCurrentUserId();

                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync(
                        "UPDATE",
                        "Hawalas",
                        hawala.Id,
                        "Pending",
                        "Paid",
                        GetCurrentUserId());

                    await transaction.CommitAsync();

                    return _mapper.Map<HawalaDto>(hawala);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            public async Task<HawalaDto> CancelHawalaAsync(long id, string cancelReason)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var hawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.Id == id);

                    if (hawala == null)
                        throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

                    if (hawala.Status == "Paid")
                        throw new InvalidOperationException("حواله پرداخت شده قابل لغو نیست.");

                    if (hawala.Status == "Cancel")
                        throw new InvalidOperationException("حواله قبلاً لغو شده است.");

                    await DeleteHawalaLedgerEntriesAsync(hawala.Id);

                    hawala.Status = "Cancel";
                    hawala.CancelledAt = DateTime.UtcNow;
                    hawala.CancelledBy = GetCurrentUserId();
                    hawala.CancelReason = cancelReason;

                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync(
                        "CANCEL",
                        "Hawalas",
                        hawala.Id,
                        "Pending",
                        "Cancel",
                        GetCurrentUserId());

                    await transaction.CommitAsync();

                    return _mapper.Map<HawalaDto>(hawala);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            // ===== منطق دفتر کل =====

            private async Task ProcessLedgerEntries(Hawala hawala, long? fromAccountId)
            {
                if (hawala.HawalaType == "HawalaSend")
                    await ProcessHawalaSendLedgerAsync(hawala, fromAccountId);
                else if (hawala.HawalaType == "HawalaReceive")
                    await ProcessHawalaReceiveLedgerAsync(hawala, fromAccountId);
                else
                    await ProcessHawalaOtherLedgerAsync(hawala);
            }

            private async Task ProcessHawalaSendLedgerAsync(Hawala hawala, long? fromAccountId)
            {
                if (!fromAccountId.HasValue)
                    throw new InvalidOperationException("برای حواله ارسالی، انتخاب حساب مبدأ الزامی است.");

                var fromAccount = await _context.Accounts.FindAsync(fromAccountId.Value);
                if (fromAccount == null)
                    throw new InvalidOperationException("حساب مبدأ انتخاب شده معتبر نیست.");

                if (!hawala.CorrespondentId.HasValue)
                    throw new InvalidOperationException("برای حواله ارسالی، انتخاب نماینده مقصد الزامی است.");

                var correspondentAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == hawala.CorrespondentId);
                if (correspondentAccount == null)
                    throw new InvalidOperationException("حساب نماینده مقصد یافت نشد.");

                var commissionAccount = await GetOrCreateCommissionAccountAsync();

                // ۱. حساب مبدأ بدهکار به مبلغ حواله
                await CreateLedgerEntry(hawala.Id, fromAccount.Id, hawala.FromCurrencyId, 0, hawala.FromAmount,
                    $"حواله ارسالی {hawala.Id}: مبلغ حواله");

                // ۲. حساب مبدأ بدهکار به مبلغ کارمزد
                if (hawala.CommissionAmount > 0)
                {
                    var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                    await CreateLedgerEntry(hawala.Id, fromAccount.Id, commissionCurrencyId, 0, hawala.CommissionAmount.Value,
                        $"حواله ارسالی {hawala.Id}: کارمزد دریافتی از مشتری");
                }

                // ۳. حساب نماینده مقصد بستانکار به مبلغ حواله (با ارز مقصد)
                var toAmount = hawala.ToAmount ?? hawala.FromAmount;
                await CreateLedgerEntry(hawala.Id, correspondentAccount.Id, hawala.ToCurrencyId, toAmount, 0,
                    $"حواله ارسالی {hawala.Id}: مبلغ قابل پرداخت به گیرنده");

                // ۴. حساب نماینده مقصد بستانکار به مبلغ کارمزد نمایندگی
                if (hawala.AgentCommissionAmount > 0)
                {
                    var agentCommissionCurrencyId = hawala.AgentCommissionCurrencyId ?? hawala.ToCurrencyId;
                    await CreateLedgerEntry(hawala.Id, correspondentAccount.Id, agentCommissionCurrencyId, hawala.AgentCommissionAmount.Value, 0,
                        $"حواله ارسالی {hawala.Id}: کارمزد نمایندگی");

                    await CreateLedgerEntry(hawala.Id, commissionAccount.Id, agentCommissionCurrencyId, 0, hawala.AgentCommissionAmount.Value,
                        $"حواله ارسالی {hawala.Id}: درآمد کارمزد");
                }

                // ۵. حساب درآمد کارمزد بستانکار
                if (hawala.CommissionAmount > 0)
                {
                    var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                    await CreateLedgerEntry(hawala.Id, commissionAccount.Id, commissionCurrencyId, hawala.CommissionAmount.Value, 0,
                        $"حواله ارسالی {hawala.Id}: درآمد کارمزد");
                }
            }

            private async Task ProcessHawalaReceiveLedgerAsync(Hawala hawala, long? fromAccountId)
            {
                await ProcessHawalaReceivePendingLedgerAsync(hawala);

                if (hawala.Status == "Paid")
                {
                    await ProcessHawalaReceivePaymentAsync(hawala, fromAccountId);
                }
            }
            private async Task ProcessHawalaReceivePendingLedgerAsync(Hawala hawala)
            {
                if (!hawala.CorrespondentId.HasValue)
                    throw new InvalidOperationException("برای حواله دریافتی، انتخاب نماینده فرستنده الزامی است.");

                var correspondentAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a =>
                        a.ReferenceType == "Correspondent" &&
                        a.ReferenceId == hawala.CorrespondentId);

                if (correspondentAccount == null)
                    throw new InvalidOperationException("حساب نماینده فرستنده یافت نشد.");

                var pendingHawalaAccount = await GetOrCreatePendingHawalaAccountAsync();

                var payableAmount = hawala.ToAmount ?? hawala.FromAmount;

                // 1. Agent / Correspondent becomes BadehKar
                await CreateLedgerEntry(
                    hawala.Id,
                    correspondentAccount.Id,
                    hawala.FromCurrencyId,
                    talabKar: 0,
                    badehKar: hawala.FromAmount,
                    description: $"حواله دریافتی {hawala.Id}: ثبت حواله در انتظار - بدهکار شدن نماینده فرستنده");

                // 2. Pending Hawala account becomes TalabKar
                await CreateLedgerEntry(
                    hawala.Id,
                    pendingHawalaAccount.Id,
                    hawala.ToCurrencyId,
                    talabKar: payableAmount,
                    badehKar: 0,
                    description: $"حواله دریافتی {hawala.Id}: ثبت در حساب حواله‌های اجرا نشده");

                if (hawala.CommissionAmount > 0)
                {
                    var commissionAccount = await GetOrCreateCommissionAccountAsync();
                    var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;

                    await CreateLedgerEntry(
                        hawala.Id,
                        correspondentAccount.Id,
                        commissionCurrencyId,
                        talabKar: 0,
                        badehKar: hawala.CommissionAmount.Value,
                        description: $"حواله دریافتی {hawala.Id}: بدهکار شدن نماینده بابت کمیشن");

                    await CreateLedgerEntry(
                        hawala.Id,
                        commissionAccount.Id,
                        commissionCurrencyId,
                        talabKar: hawala.CommissionAmount.Value,
                        badehKar: 0,
                        description: $"حواله دریافتی {hawala.Id}: درآمد کمیشن");
                }
            }
            private async Task ProcessHawalaReceivePaymentAsync(Hawala hawala, long? paidFromAccountId)
            {
                if (!paidFromAccountId.HasValue)
                    throw new InvalidOperationException("برای پرداخت حواله دریافتی، انتخاب حساب پرداخت‌کننده الزامی است.");

                var paidFromAccount = await _context.Accounts
                    .FindAsync(paidFromAccountId.Value);

                if (paidFromAccount == null)
                    throw new InvalidOperationException("حساب پرداخت‌کننده انتخاب شده معتبر نیست.");

                if (paidFromAccount.IsArchived)
                    throw new InvalidOperationException("حساب پرداخت‌کننده انتخاب شده آرشیف شده است.");

                hawala.PaidFromAccountId = paidFromAccount.Id;

                var ledgerHawalaId = hawala.Id;
                if (string.Equals(paidFromAccount.AccountType, "Correspondent", StringComparison.OrdinalIgnoreCase))
                {
                    var generatedSendHawala = await CreateGeneratedSendHawalaAsync(hawala, paidFromAccount);
                    ledgerHawalaId = generatedSendHawala.Id;
                }

                await ProcessHawalaReceivePaymentLedgerAsync(hawala, paidFromAccount, ledgerHawalaId);
            }

            private async Task<Hawala> CreateGeneratedSendHawalaAsync(Hawala receivedHawala, Account paidFromAccount)
            {
                if (!string.Equals(paidFromAccount.ReferenceType, "Correspondent", StringComparison.OrdinalIgnoreCase) ||
                    !paidFromAccount.ReferenceId.HasValue)
                {
                    throw new InvalidOperationException("حساب پرداخت‌کننده به نمایندگی معتبری مرتبط نیست.");
                }

                var existing = await _context.Hawalas
                    .FirstOrDefaultAsync(x => x.SourceHawalaId == receivedHawala.Id);
                if (existing != null)
                    return existing;

                var destinationCorrespondentId = paidFromAccount.ReferenceId.Value;
                var lastNumber = await _context.Hawalas
                    .Where(x => x.CorrespondentId == destinationCorrespondentId &&
                                x.HawalaType == "HawalaSend")
                    .OrderByDescending(x => x.Number)
                    .Select(x => (long?)x.Number)
                    .FirstOrDefaultAsync();

                var now = DateTime.UtcNow;
                var generatedHawala = new Hawala
                {
                    Number = (lastNumber ?? 0) + 1,
                    HawalaType = "HawalaSend",
                    Status = "Paid",
                    CorrespondentId = destinationCorrespondentId,
                    SourceHawalaId = receivedHawala.Id,
                    IsSystemGenerated = true,
                    PaidFromAccountId = paidFromAccount.Id,
                    PaymentLocationId = receivedHawala.PaymentLocationId,
                    SenderName = receivedHawala.SenderName,
                    SenderFatherName = receivedHawala.SenderFatherName,
                    SenderPhone = receivedHawala.SenderPhone,
                    SenderTazkiraNumber = receivedHawala.SenderTazkiraNumber,
                    SenderTazkiraImagePath = receivedHawala.SenderTazkiraImagePath,
                    SenderAddress = receivedHawala.SenderAddress,
                    ReceiverName = receivedHawala.ReceiverName,
                    ReceiverFatherName = receivedHawala.ReceiverFatherName,
                    ReceiverPhone = receivedHawala.ReceiverPhone,
                    ReceiverTazkiraNumber = receivedHawala.ReceiverTazkiraNumber,
                    ReceiverTazkiraImagePath = receivedHawala.ReceiverTazkiraImagePath,
                    ReceiverAddress = receivedHawala.ReceiverAddress,
                    FromCurrencyId = receivedHawala.FromCurrencyId,
                    FromAmount = receivedHawala.FromAmount,
                    ToCurrencyId = receivedHawala.ToCurrencyId,
                    ToAmount = receivedHawala.ToAmount,
                    ExchangeRate = receivedHawala.ExchangeRate,
                    CommissionAmount = receivedHawala.CommissionAmount,
                    CommissionCurrencyId = receivedHawala.CommissionCurrencyId,
                    AgentCommissionAmount = receivedHawala.AgentCommissionAmount,
                    AgentCommissionCurrencyId = receivedHawala.AgentCommissionCurrencyId,
                    ReferenceNumber = $"AUTO-RCV-{receivedHawala.Id}",
                    Notes = $"حواله ارسالی خودکار بابت پرداخت حواله دریافتی شماره {receivedHawala.Number}",
                    CreatedAt = now,
                    CreatedBy = GetCurrentUserId(),
                    PaidAt = now,
                    PaidBy = GetCurrentUserId()
                };

                await _context.Hawalas.AddAsync(generatedHawala);
                await _context.SaveChangesAsync();

                await _auditLogService.LogAsync(
                    "CREATE",
                    "Hawalas",
                    generatedHawala.Id,
                    null,
                    $"حواله ارسالی خودکار شماره {generatedHawala.Number} از حواله دریافتی {receivedHawala.Id} ایجاد شد",
                    GetCurrentUserId());

                return generatedHawala;
            }

            private async Task ProcessHawalaReceivePaymentLedgerAsync(
                Hawala hawala,
                Account paidFromAccount,
                long ledgerHawalaId)
            {

                var pendingHawalaAccount = await GetOrCreatePendingHawalaAccountAsync();

                var payableAmount = hawala.ToAmount ?? hawala.FromAmount;

                // 1. Pending Hawala account becomes BadehKar
                await CreateLedgerEntry(
                    ledgerHawalaId,
                    pendingHawalaAccount.Id,
                    hawala.ToCurrencyId,
                    talabKar: 0,
                    badehKar: payableAmount,
                    description: $"حواله دریافتی {hawala.Id}: خروج از حساب حواله‌های اجرا نشده");

                // 2. Selected payment account becomes TalabKar
                await CreateLedgerEntry(
                    ledgerHawalaId,
                    paidFromAccount.Id,
                    hawala.ToCurrencyId,
                    talabKar: payableAmount,
                    badehKar: 0,
                    description: $"حواله دریافتی {hawala.Id}: پرداخت حواله از حساب انتخاب‌شده");
                if (hawala.AgentCommissionAmount > 0)
                {
                    var commissionAccount = await GetOrCreateCommissionAccountAsync();
                    var agentCommissionCurrencyId =
                        hawala.AgentCommissionCurrencyId ??
                        hawala.CommissionCurrencyId ??
                        hawala.ToCurrencyId;

                    await CreateLedgerEntry(
                        ledgerHawalaId,
                        commissionAccount.Id,
                        agentCommissionCurrencyId,
                        talabKar: 0,
                        badehKar: hawala.AgentCommissionAmount.Value,
                        description: $"حواله دریافتی {hawala.Id}: سهم حساب پرداخت‌کننده از کمیشن");

                    await CreateLedgerEntry(
                        ledgerHawalaId,
                        paidFromAccount.Id,
                        agentCommissionCurrencyId,
                        talabKar: hawala.AgentCommissionAmount.Value,
                        badehKar: 0,
                        description: $"حواله دریافتی {hawala.Id}: کمیشن قابل پرداخت به حساب انتخاب‌شده");
                }
            }
            private async Task ProcessHawalaOtherLedgerAsync(Hawala hawala)
            {
                var defaultAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountType == "Cash" && a.ReferenceId == null);
                if (defaultAccount == null)
                    throw new InvalidOperationException("حساب پیش‌فرض برای حواله متفرقه یافت نشد.");

                await CreateLedgerEntry(hawala.Id, defaultAccount.Id, hawala.FromCurrencyId, hawala.FromAmount, 0,
                    $"حواله متفرقه {hawala.Id}: مبلغ {hawala.FromAmount} {hawala.FromCurrency?.Code}");
            }

            private async Task CreateLedgerEntry(
        long hawalaId,
        long accountId,
        long currencyId,
        decimal talabKar,
        decimal badehKar,
        string description)
            {
                var ledgerEntry = new LedgerEntry
                {
                    TransactionId = null,
                    HawalaId = hawalaId,
                    AccountId = accountId,
                    CurrencyId = currencyId,
                    TalabKar = talabKar,
                    BadehKar = badehKar,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                };

                await _context.LedgerEntries.AddAsync(ledgerEntry);
            }

            private async Task<Account> GetOrCreateCommissionAccountAsync()
            {
                const string accountCode = "3001";
                var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
                if (account != null) return account;

                var newAccount = new Account
                {
                    AccountCode = accountCode,
                    AccountName = "کارمزد حواله",
                    AccountType = "Income",
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.Accounts.AddAsync(newAccount);
                await _context.SaveChangesAsync();
                return newAccount;
            }

            private void ValidateCreateHawala(CreateHawalaDto dto)
            {
                if (string.IsNullOrEmpty(dto.HawalaType))
                    throw new InvalidOperationException("نوع حواله الزامی است.");
                if (dto.FromCurrencyId == 0)
                    throw new InvalidOperationException("ارز مبدأ الزامی است.");
                if (dto.FromAmount <= 0)
                    throw new InvalidOperationException("مبلغ باید بزرگتر از صفر باشد.");
                if (dto.ToCurrencyId == 0)
                    throw new InvalidOperationException("ارز مقصد الزامی است.");
                if (dto.HawalaType == "HawalaSend" && dto.CorrespondentId == null)
                    throw new InvalidOperationException("برای حواله ارسال، نمایندگی مقصد الزامی است.");
                if (dto.HawalaType == "HawalaReceive" && dto.CorrespondentId == null)
                    throw new InvalidOperationException("برای حواله دریافت، نمایندگی فرستنده الزامی است.");
                if (dto.HawalaType == "HawalaReceive" && (dto.Number <= 0))
                    throw new InvalidOperationException("برای حواله آمد، شماره (نمبر) الزامی است.");

                if (dto.HawalaType == "HawalaReceive" &&
                    dto.Status == "Paid" &&
                    dto.FromAccountId == null)
                {
                    throw new InvalidOperationException("برای پرداخت حواله دریافتی، انتخاب حساب پرداخت‌کننده الزامی است.");
                }
            }

            private long GetCurrentUserId() => 1;
            private async Task<Account> GetOrCreatePendingHawalaAccountAsync()
            {
                const string accountCode = "2101";

                var account = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountCode == accountCode);

                if (account != null)
                    return account;

                var newAccount = new Account
                {
                    AccountCode = accountCode,
                    AccountName = "حواله‌های اجرا نشده",
                    AccountType = "PendingHawala",
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _context.Accounts.AddAsync(newAccount);
                await _context.SaveChangesAsync();

                return newAccount;
            }

        }

    }
