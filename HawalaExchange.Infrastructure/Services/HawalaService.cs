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
                    await NormalizeHawalaConversionAsync(hawala);
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
                    await ProcessLedgerEntries(
                        hawala,
                        dto.FromAccountId,
                        dto.GeneratedSendHawalaNumber);
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

            private async Task<long?> ResolveExistingFromAccountIdAsync(Hawala hawala)
            {
                if (hawala.HawalaType == "HawalaReceive" || hawala.PaidFromAccountId.HasValue)
                    return hawala.PaidFromAccountId;

                if (hawala.HawalaType != "HawalaSend")
                    return null;

                return await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(x =>
                        x.HawalaId == hawala.Id &&
                        x.CurrencyId == hawala.FromCurrencyId &&
                        x.BadehKar > 0 &&
                        x.Description != null &&
                        x.Description.Contains("مبلغ حواله"))
                    .OrderBy(x => x.Id)
                    .Select(x => (long?)x.AccountId)
                    .FirstOrDefaultAsync();
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

                if (hawala == null)
                    return null;

                var result = _mapper.Map<HawalaDto>(hawala);
                result.FromAccountId = await ResolveExistingFromAccountIdAsync(hawala);

                if (!hawala.IsSystemGenerated)
                {
                    result.GeneratedSendHawalaNumber = await _context.Hawalas
                        .AsNoTracking()
                        .Where(x => x.SourceHawalaId == hawala.Id)
                        .Select(x => (long?)x.Number)
                        .FirstOrDefaultAsync();
                }

                return result;
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

                if (filter.Number > 0 && string.IsNullOrWhiteSpace(filter.SearchTerm))
                    query = query.Where(h => h.Number == filter.Number);

                if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
                {
                    var term = filter.SearchTerm.Trim();
                    query = query.Where(h =>
                        (h.SenderName != null && h.SenderName.Contains(term)) ||
                        (h.ReceiverName != null && h.ReceiverName.Contains(term)) ||
                        (h.ReferenceNumber != null && h.ReferenceNumber.Contains(term)) ||
                        (filter.Number > 0 && h.Number == filter.Number) ||
                        (filter.SearchAmount.HasValue && h.FromAmount == filter.SearchAmount.Value)
                    );
                }

                if (!string.IsNullOrEmpty(filter.HawalaType))
                    query = query.Where(h => h.HawalaType == filter.HawalaType);

                if (!string.IsNullOrEmpty(filter.Status))
                    query = query.Where(h => h.Status == filter.Status);

                if (filter.CorrespondentId.HasValue)
                    query = query.Where(h => h.CorrespondentId == filter.CorrespondentId);

                if (filter.PaymentLocationId.HasValue)
                    query = query.Where(h => h.PaymentLocationId == filter.PaymentLocationId);

                if (filter.CurrencyId.HasValue)
                    query = query.Where(h => h.FromCurrencyId == filter.CurrencyId);

                if (filter.MinAmount.HasValue)
                    query = query.Where(h => h.FromAmount >= filter.MinAmount.Value);

                if (filter.MaxAmount.HasValue)
                    query = query.Where(h => h.FromAmount <= filter.MaxAmount.Value);

                if (filter.FromDate.HasValue)
                {
                    var fromDate = filter.FromDate.Value.Date;
                    query = query.Where(h => h.CreatedAt >= fromDate);
                }

                if (filter.ToDate.HasValue)
                {
                    var toDateExclusive = filter.ToDate.Value.Date.AddDays(1);
                    query = query.Where(h => h.CreatedAt < toDateExclusive);
                }

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

                    if (hawala.Status == "Cancel")
                        throw new InvalidOperationException("حواله لغو شده قابل ویرایش نیست.");

                    if (hawala.IsSystemGenerated)
                    {
                        throw new InvalidOperationException(
                            "حواله ارسالی خودکار باید از طریق حواله دریافتی اصلی ویرایش شود.");
                    }

                    var oldSenderTazkiraImagePath = hawala.SenderTazkiraImagePath;
                    var oldReceiverTazkiraImagePath = hawala.ReceiverTazkiraImagePath;

                    var fromAccountId =
                        dto.FromAccountId ??
                        await ResolveExistingFromAccountIdAsync(hawala);

                    var generatedHawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.SourceHawalaId == hawala.Id);
                    var generatedHawalaNumber =
                        dto.GeneratedSendHawalaNumber ??
                        generatedHawala?.Number;

                    if (generatedHawala != null)
                    {
                        await DeleteHawalaLedgerEntriesAsync(generatedHawala.Id);
                        _context.Hawalas.Remove(generatedHawala);
                    }

                    await DeleteHawalaLedgerEntriesAsync(hawala.Id);
                    await _context.SaveChangesAsync();

                    ApplyUpdate(hawala, dto);
                    await NormalizeHawalaConversionAsync(hawala);
                    await ValidateUpdatedHawalaAsync(hawala);

                    await ProcessLedgerEntries(
                        hawala,
                        fromAccountId,
                        generatedHawalaNumber);

                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync(
                        "UPDATE",
                        "Hawalas",
                        hawala.Id,
                        null,
                        $"حواله با شناسه {hawala.Id} ویرایش شد و لیجر آن دوباره ساخته شد",
                        GetCurrentUserId());

                    await transaction.CommitAsync();

                    await DeleteReplacedTazkiraImagesAsync(
                        oldSenderTazkiraImagePath,
                        oldReceiverTazkiraImagePath,
                        hawala.SenderTazkiraImagePath,
                        hawala.ReceiverTazkiraImagePath);

                    return _mapper.Map<HawalaDto>(hawala);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            private static void ApplyUpdate(Hawala hawala, UpdateHawalaDto dto)
            {
                if (dto.CorrespondentId.HasValue)
                    hawala.CorrespondentId = dto.CorrespondentId;
                if (dto.FromCurrencyId.HasValue)
                    hawala.FromCurrencyId = dto.FromCurrencyId.Value;
                if (dto.FromAmount.HasValue)
                    hawala.FromAmount = dto.FromAmount.Value;
                if (dto.ToCurrencyId.HasValue)
                    hawala.ToCurrencyId = dto.ToCurrencyId.Value;

                hawala.PaymentLocationId = dto.PaymentLocationId;
                hawala.SenderName = dto.SenderName;
                hawala.SenderFatherName = dto.SenderFatherName;
                hawala.SenderPhone = dto.SenderPhone;
                hawala.SenderTazkiraNumber = dto.SenderTazkiraNumber;
                hawala.SenderTazkiraImagePath = dto.SenderTazkiraImagePath;
                hawala.SenderAddress = dto.SenderAddress;
                hawala.ReceiverName = dto.ReceiverName;
                hawala.ReceiverFatherName = dto.ReceiverFatherName;
                hawala.ReceiverPhone = dto.ReceiverPhone;
                hawala.ReceiverTazkiraNumber = dto.ReceiverTazkiraNumber;
                hawala.ReceiverTazkiraImagePath = dto.ReceiverTazkiraImagePath;
                hawala.ReceiverAddress = dto.ReceiverAddress;
                hawala.ToAmount = dto.ToAmount;
                hawala.ExchangeRate = dto.ExchangeRate;
                hawala.CommissionAmount = dto.CommissionAmount;
                hawala.CommissionCurrencyId = dto.CommissionCurrencyId;
                hawala.AgentCommissionAmount = dto.AgentCommissionAmount;
                hawala.AgentCommissionCurrencyId = dto.AgentCommissionCurrencyId;
                hawala.ReferenceNumber = dto.ReferenceNumber;
                hawala.Notes = dto.Notes;
            }

            private async Task DeleteReplacedTazkiraImagesAsync(
                string? oldSenderPath,
                string? oldReceiverPath,
                string? currentSenderPath,
                string? currentReceiverPath)
            {
                var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(currentSenderPath))
                    currentPaths.Add(currentSenderPath);
                if (!string.IsNullOrWhiteSpace(currentReceiverPath))
                    currentPaths.Add(currentReceiverPath);

                var oldPaths = new[] { oldSenderPath, oldReceiverPath }
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var oldPath in oldPaths)
                {
                    if (!currentPaths.Contains(oldPath))
                        await _fileService.DeleteFileAsync(oldPath);
                }
            }

            private async Task NormalizeHawalaConversionAsync(Hawala hawala)
            {
                var currencies = await _context.Currencies
                    .Where(x => (x.Id == hawala.FromCurrencyId || x.Id == hawala.ToCurrencyId) && x.IsActive)
                    .ToDictionaryAsync(x => x.Id);

                if (!currencies.TryGetValue(hawala.FromCurrencyId, out var fromCurrency) ||
                    !currencies.TryGetValue(hawala.ToCurrencyId, out var toCurrency))
                {
                    throw new InvalidOperationException("ارز مبدأ یا مقصد معتبر و فعال نیست.");
                }

                if (fromCurrency.Id == toCurrency.Id)
                {
                    hawala.ExchangeRate = 1;
                    hawala.ToAmount = hawala.FromAmount;
                    return;
                }

                if (!hawala.ExchangeRate.HasValue || hawala.ExchangeRate.Value <= 0)
                    throw new InvalidOperationException("نرخ تبدیل باید بزرگتر از صفر باشد.");

                var conversion = CurrencyQuotationCalculator.ConvertFromAmount(
                    fromCurrency.Id,
                    fromCurrency.Code,
                    fromCurrency.QuotationPriority,
                    hawala.FromAmount,
                    toCurrency.Id,
                    toCurrency.Code,
                    toCurrency.QuotationPriority,
                    hawala.ExchangeRate.Value);

                hawala.ToAmount = AmountValueHelper.RoundConvertedAmount(conversion.ToAmount);
            }

            private async Task ValidateUpdatedHawalaAsync(Hawala hawala)
            {
                if (hawala.FromAmount <= 0)
                    throw new InvalidOperationException("مبلغ حواله باید بزرگتر از صفر باشد.");
                if (hawala.FromCurrencyId <= 0 || hawala.ToCurrencyId <= 0)
                    throw new InvalidOperationException("انتخاب ارز مبدأ و مقصد الزامی است.");
                if ((hawala.HawalaType == "HawalaSend" || hawala.HawalaType == "HawalaReceive") &&
                    !hawala.CorrespondentId.HasValue)
                {
                    throw new InvalidOperationException("انتخاب نمایندگی برای حواله الزامی است.");
                }
                if (hawala.CommissionAmount < 0 || hawala.AgentCommissionAmount < 0)
                    throw new InvalidOperationException("مقدار کارمزد نمی‌تواند منفی باشد.");
                if (hawala.CommissionAmount > 0 &&
                    (!hawala.CommissionCurrencyId.HasValue || hawala.CommissionCurrencyId.Value <= 0))
                    throw new InvalidOperationException("انتخاب ارز کارمزد الزامی است.");
                if (hawala.AgentCommissionAmount > 0 &&
                    (!hawala.AgentCommissionCurrencyId.HasValue || hawala.AgentCommissionCurrencyId.Value <= 0))
                    throw new InvalidOperationException("انتخاب ارز کارمزد نمایندگی الزامی است.");

                var duplicateNumber = await _context.Hawalas.AnyAsync(x =>
                    x.Id != hawala.Id &&
                    x.CorrespondentId == hawala.CorrespondentId &&
                    x.HawalaType == hawala.HawalaType &&
                    x.Number == hawala.Number);

                if (duplicateNumber)
                {
                    throw new InvalidOperationException(
                        $"شماره {hawala.Number} برای این نوع حواله و نمایندگی قبلاً ثبت شده است.");
                }
            }
            public async Task DeleteHawalaAsync(long id)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var requestedHawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.Id == id);

                    if (requestedHawala == null)
                        throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

                    var hawala = requestedHawala;
                    if (requestedHawala.IsSystemGenerated &&
                        requestedHawala.SourceHawalaId.HasValue)
                    {
                        hawala = await _context.Hawalas
                            .FirstOrDefaultAsync(x => x.Id == requestedHawala.SourceHawalaId.Value)
                            ?? requestedHawala;
                    }

                    var generatedHawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.SourceHawalaId == hawala.Id);
                    if (generatedHawala != null)
                    {
                        await DeleteHawalaLedgerEntriesAsync(generatedHawala.Id);
                        _context.Hawalas.Remove(generatedHawala);
                    }

                    await DeleteHawalaLedgerEntriesAsync(hawala.Id);

                    _context.Hawalas.Remove(hawala);
                    if (!string.IsNullOrWhiteSpace(hawala.SenderTazkiraImagePath))
                        await _fileService.DeleteFileAsync(hawala.SenderTazkiraImagePath);
                    if (!string.IsNullOrWhiteSpace(hawala.ReceiverTazkiraImagePath))
                        await _fileService.DeleteFileAsync(hawala.ReceiverTazkiraImagePath);
                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync(
                        "DELETE",
                        "Hawalas",
                        hawala.Id,
                        null,
                        generatedHawala == null
                            ? $"حواله با شناسه {hawala.Id} و لیجرهای مربوطه حذف شد"
                            : $"حواله با شناسه {hawala.Id}، حواله ارسالی خودکار و لیجرهای مربوطه حذف شدند",
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

            public async Task<CorrespondentHawalaRangeResultDto> GetCorrespondentRangeAsync(
                CorrespondentHawalaRangeFilterDto filter)
            {
                if (filter.CorrespondentId <= 0)
                    throw new InvalidOperationException("انتخاب نمایندگی الزامی است.");

                var validHawalaTypes = new[] { "HawalaSend", "HawalaReceive", "HawalaOther" };
                if (!validHawalaTypes.Contains(filter.HawalaType))
                    throw new InvalidOperationException("نوع حواله معتبر نیست.");

                var query = _context.Hawalas
                    .AsNoTracking()
                    .Include(h => h.Correspondent)
                    .Include(h => h.FromCurrency)
                    .Include(h => h.ToCurrency)
                    .Include(h => h.CommissionCurrency)
                    .Include(h => h.AgentCommissionCurrency)
                    .Include(h => h.PaymentLocation)
                    .Where(h => h.CorrespondentId == filter.CorrespondentId &&
                                h.HawalaType == filter.HawalaType);

                if (filter.RangeType == "Number")
                {
                    if (!filter.StartNumber.HasValue || !filter.EndNumber.HasValue ||
                        filter.StartNumber <= 0 || filter.EndNumber < filter.StartNumber)
                    {
                        throw new InvalidOperationException("محدوده شماره حواله معتبر نیست.");
                    }

                    query = query.Where(h => h.Number >= filter.StartNumber.Value &&
                                             h.Number <= filter.EndNumber.Value);
                }
                else if (filter.RangeType == "Date")
                {
                    if (!filter.StartDate.HasValue || !filter.EndDate.HasValue ||
                        filter.EndDate.Value.Date < filter.StartDate.Value.Date)
                    {
                        throw new InvalidOperationException("محدوده تاریخ معتبر نیست.");
                    }

                    var startDate = filter.StartDate.Value.Date;
                    var endDateExclusive = filter.EndDate.Value.Date.AddDays(1);
                    query = query.Where(h => h.CreatedAt >= startDate && h.CreatedAt < endDateExclusive);
                }
                else
                {
                    throw new InvalidOperationException("نوع محدوده معتبر نیست.");
                }

                var hawalas = await query
                    .OrderBy(h => h.Number)
                    .ThenBy(h => h.CreatedAt)
                    .ToListAsync();

                var result = new CorrespondentHawalaRangeResultDto
                {
                    Hawalas = _mapper.Map<List<HawalaDto>>(hawalas)
                };

                if (hawalas.Count == 0)
                    return result;

                var correspondentAccountId = await _context.Accounts
                    .AsNoTracking()
                    .Where(a => a.ReferenceType == "Correspondent" &&
                                a.ReferenceId == filter.CorrespondentId)
                    .Select(a => (long?)a.Id)
                    .FirstOrDefaultAsync();

                if (!correspondentAccountId.HasValue)
                    return result;

                var hawalaIds = hawalas.Select(h => h.Id).ToList();
                var ledgerEntries = await _context.LedgerEntries
                    .AsNoTracking()
                    .Include(e => e.Currency)
                    .Where(e => e.AccountId == correspondentAccountId.Value &&
                                e.HawalaId.HasValue && hawalaIds.Contains(e.HawalaId.Value))
                    .ToListAsync();

                result.Summaries = ledgerEntries
                    .GroupBy(e => new { e.CurrencyId, CurrencyCode = e.Currency != null ? e.Currency.Code : "N/A" })
                    .Select(group => new CorrespondentHawalaRangeSummaryDto
                    {
                        CurrencyId = group.Key.CurrencyId,
                        CurrencyCode = group.Key.CurrencyCode,
                        TotalDebit = group.Sum(e => e.TalabKar),
                        TotalCredit = group.Sum(e => e.BadehKar)
                    })
                    .OrderBy(summary => summary.CurrencyCode)
                    .ToList();

                return result;
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
            public async Task<HawalaDto> CancelHawalaAsync(long id, CancelHawalaDto cancellation)
            {
                using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                try
                {
                    var requestedHawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.Id == id);

                    if (requestedHawala == null)
                        throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

                    var hawala = requestedHawala;
                    if (requestedHawala.IsSystemGenerated && requestedHawala.SourceHawalaId.HasValue)
                    {
                        hawala = await _context.Hawalas
                            .FirstOrDefaultAsync(x => x.Id == requestedHawala.SourceHawalaId.Value)
                            ?? requestedHawala;
                    }

                    if (hawala.Status == "Cancel")
                        throw new InvalidOperationException("حواله قبلاً لغو شده است.");

                    if (string.IsNullOrWhiteSpace(cancellation.CancelReason))
                        throw new InvalidOperationException("دلیل لغو حواله الزامی است.");

                    var generatedHawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.SourceHawalaId == hawala.Id);

                    var affectedHawalaIds = new List<long> { hawala.Id };
                    if (generatedHawala != null)
                        affectedHawalaIds.Add(generatedHawala.Id);

                    await CreateCancellationLedgerEntriesAsync(
                        affectedHawalaIds,
                        cancellation.ReverseCommission);

                    var previousStatus = hawala.Status;
                    var now = DateTime.UtcNow;
                    var cancelledBy = GetCurrentUserId();
                    var cancellationMode = cancellation.ReverseCommission
                        ? "لغو با کارمزد"
                        : "لغو بدون کارمزد";

                    hawala.Status = "Cancel";
                    hawala.CancelledAt = now;
                    hawala.CancelledBy = cancelledBy;
                    hawala.CancelReason = $"{cancellationMode}: {cancellation.CancelReason.Trim()}";

                    if (generatedHawala != null)
                    {
                        generatedHawala.Status = "Cancel";
                        generatedHawala.CancelledAt = now;
                        generatedHawala.CancelledBy = cancelledBy;
                        generatedHawala.CancelReason = hawala.CancelReason;
                    }

                    await _context.SaveChangesAsync();

                    await _auditLogService.LogAsync(
                        "CANCEL",
                        "Hawalas",
                        hawala.Id,
                        previousStatus,
                        $"Cancel ({cancellationMode})",
                        cancelledBy);

                    await transaction.CommitAsync();

                    return _mapper.Map<HawalaDto>(hawala);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            private async Task CreateCancellationLedgerEntriesAsync(
                IReadOnlyCollection<long> hawalaIds,
                bool reverseCommission)
            {
                var originalEntries = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(entry => entry.HawalaId.HasValue && hawalaIds.Contains(entry.HawalaId.Value))
                    .OrderBy(entry => entry.Id)
                    .ToListAsync();

                if (!reverseCommission)
                {
                    originalEntries = originalEntries
                        .Where(entry => !IsCommissionLedgerEntry(entry))
                        .ToList();
                }

                var cancellationMode = reverseCommission ? "با کارمزد" : "بدون کارمزد";
                foreach (var entry in originalEntries)
                {
                    await CreateLedgerEntry(
                        entry.HawalaId!.Value,
                        entry.AccountId,
                        entry.CurrencyId,
                        talabKar: entry.BadehKar,
                        badehKar: entry.TalabKar,
                        description: $"لغو حواله {cancellationMode} - معکوس سند {entry.Id}: {entry.Description}");
                }
            }

            private static bool IsCommissionLedgerEntry(LedgerEntry entry)
            {
                var description = entry.Description ?? string.Empty;
                return description.Contains("کارمزد", StringComparison.OrdinalIgnoreCase) ||
                       description.Contains("کمیشن", StringComparison.OrdinalIgnoreCase) ||
                       description.Contains("commission", StringComparison.OrdinalIgnoreCase);
            }
            // ===== منطق دفتر کل =====

            private async Task ProcessLedgerEntries(
                Hawala hawala,
                long? fromAccountId,
                long? generatedSendHawalaNumber = null)
            {
                if (hawala.HawalaType == "HawalaSend")
                    await ProcessHawalaSendLedgerAsync(hawala, fromAccountId);
                else if (hawala.HawalaType == "HawalaReceive")
                    await ProcessHawalaReceiveLedgerAsync(
                        hawala,
                        fromAccountId,
                        generatedSendHawalaNumber);
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

            private async Task ProcessHawalaReceiveLedgerAsync(
                Hawala hawala,
                long? fromAccountId,
                long? generatedSendHawalaNumber = null)
            {
                await ProcessHawalaReceivePendingLedgerAsync(hawala);

                if (hawala.Status == "Paid")
                {
                    await ProcessHawalaReceivePaymentAsync(
                        hawala,
                        fromAccountId,
                        generatedSendHawalaNumber);
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
            private async Task ProcessHawalaReceivePaymentAsync(
                Hawala hawala,
                long? paidFromAccountId,
                long? generatedSendHawalaNumber = null)
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
                    var generatedSendHawala = await CreateGeneratedSendHawalaAsync(
                        hawala,
                        paidFromAccount,
                        generatedSendHawalaNumber);
                    ledgerHawalaId = generatedSendHawala.Id;
                }

                await ProcessHawalaReceivePaymentLedgerAsync(hawala, paidFromAccount, ledgerHawalaId);
            }

            private async Task<Hawala> CreateGeneratedSendHawalaAsync(
                Hawala receivedHawala,
                Account paidFromAccount,
                long? requestedNumber = null)
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
                if (requestedNumber.HasValue && requestedNumber.Value <= 0)
                    throw new InvalidOperationException("نمبر حواله ارسالی نمایندگی باید بزرگتر از صفر باشد.");

                long generatedNumber;
                if (requestedNumber.HasValue)
                {
                    generatedNumber = requestedNumber.Value;
                }
                else
                {
                    var lastNumber = await _context.Hawalas
                        .Where(x => x.CorrespondentId == destinationCorrespondentId &&
                                    x.HawalaType == "HawalaSend")
                        .OrderByDescending(x => x.Number)
                        .Select(x => (long?)x.Number)
                        .FirstOrDefaultAsync();

                    generatedNumber = (lastNumber ?? 0) + 1;
                }

                var numberExists = await _context.Hawalas.AnyAsync(x =>
                    x.CorrespondentId == destinationCorrespondentId &&
                    x.HawalaType == "HawalaSend" &&
                    x.Number == generatedNumber);
                if (numberExists)
                {
                    throw new InvalidOperationException(
                        $"نمبر {generatedNumber} برای حواله ارسالی این نمایندگی قبلاً ثبت شده است.");
                }

                var now = DateTime.UtcNow;
                var generatedHawala = new Hawala
                {
                    Number = generatedNumber,
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
                    $"حواله متفرقه {hawala.Id}: مبلغ {AmountValueHelper.Format(hawala.FromAmount)} {hawala.FromCurrency?.Code}");
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
                if (dto.AgentCommissionAmount > 0 &&
                    (!dto.AgentCommissionCurrencyId.HasValue ||
                     dto.AgentCommissionCurrencyId.Value <= 0))
                {
                    throw new InvalidOperationException("برای کارمزد نمایندگی، انتخاب ارز الزامی است.");
                }

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
                const string accountCode = ApplicationDbContext.PendingHawalaAccountCode;

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
