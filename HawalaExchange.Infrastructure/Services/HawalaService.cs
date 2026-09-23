    using AutoMapper;
    using HawalaExchange.Application.DTOs;
    using HawalaExchange.Application.Interfaces.Services;
    using HawalaExchange.Domain.Entities;
    using HawalaExchange.Infrastructure.Data;
    using Microsoft.Data.SqlClient;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage;
    using System.Data;
    using System.Linq.Expressions;

    namespace HawalaExchange.Application.Services
    {
        public class HawalaService : IHawalaService
        {
            private const int BulkCopyBatchSize = 10_000;
            private readonly ApplicationDbContext _context;
            private readonly IMapper _mapper;
            private readonly ILedgerService _ledgerService;
            private readonly IAccountService _accountService;
            private readonly IAuditLogService _auditLogService;
            private readonly IFileService _fileService;
            private readonly ICorrespondentSettlementService _settlementService;


            public HawalaService(
                ApplicationDbContext context,
                IMapper mapper,
                ILedgerService ledgerService,
                IAccountService accountService,
                IAuditLogService auditLogService,
                IFileService fileService,
                ICorrespondentSettlementService? settlementService = null)
            {
                _context = context;
                _mapper = mapper;
                _ledgerService = ledgerService;
                _accountService = accountService;
                _auditLogService = auditLogService;
                _fileService = fileService;
                _settlementService = settlementService ??
                    new HawalaExchange.Infrastructure.Services.CorrespondentSettlementService(context);
            }

            public async Task<HawalaDto> CreateHawalaAsync(CreateHawalaDto dto)
            {
                await using var transaction = _context.Database.CurrentTransaction == null
                    ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                    : null;

                try
                {
                    ValidateCreateHawala(dto);
                    await EnforceCorrespondentCommissionMethodAsync(
                        dto.HawalaType, dto.CorrespondentId, dto.CommissionAmount);
                    await ValidatePaymentLocationAsync(dto.PaymentLocationId);

                    var hawala = _mapper.Map<Hawala>(dto);
                    await NormalizeHawalaConversionAsync(hawala);
                    hawala.CreatedAt = DateTime.UtcNow;
                    hawala.CreatedBy = GetCurrentUserId();
                    hawala.Status = dto.Status ?? "Pending";
                    var currentPeriodStart = await GetCurrentPeriodStartAsync(hawala.CorrespondentId);
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
                            .Where(h => h.CorrespondentId == hawala.CorrespondentId && h.HawalaType == hawala.HawalaType &&
                                        h.CreatedAt >= currentPeriodStart)
                            .OrderByDescending(h => h.Number)
                            .Select(h => (long?)h.Number)
                            .FirstOrDefaultAsync();

                        hawala.Number = (lastNumber ?? 0) + 1;
                    }

                    // بررسی یکتا بودن شماره
                    var exists = await _context.Hawalas
                        .AnyAsync(h => h.CorrespondentId == hawala.CorrespondentId &&
                                       h.HawalaType == hawala.HawalaType &&
                                       h.CreatedAt >= currentPeriodStart &&
                                       h.Number == hawala.Number);
                    if (exists)
                        throw new InvalidOperationException($"شماره {hawala.Number} برای حواله {hawala.HawalaType} این نمایندگی قبلاً ثبت شده است.");

                    await _context.Hawalas.AddAsync(hawala);
                    await _context.SaveChangesAsync();

                    // ثبت ورودی‌های دفتر کل
                    await ProcessLedgerEntries(
                        hawala,
                        dto.FromAccountId,
                        dto.GeneratedSendHawalaNumber,
                        dto.GeneratedSendAgentCommissionAmount,
                        dto.GeneratedSendAgentCommissionCurrencyId,
                        dto.GeneratedSendReferenceNumber);
                    await _context.SaveChangesAsync();

                    var hawalaTypeName = hawala.HawalaType switch
                    {
                        "HawalaSend" => "ارسالی",
                        "HawalaReceive" => "دریافتی",
                        _ => "متفرقه"
                    };
                    await _auditLogService.LogAsync("CREATE", "Hawalas", hawala.Id, null, $"حواله {hawalaTypeName} شماره {hawala.Number} ثبت شد.", GetCurrentUserId());

                    if (transaction != null)
                        await transaction.CommitAsync();

                    return _mapper.Map<HawalaDto>(hawala);
                }
                catch (Exception)
                {
                    if (transaction != null)
                        await transaction.RollbackAsync();
                    throw;
                }
            }

            public async Task<IReadOnlyList<HawalaDto>> CreateHawalasAsync(
                IReadOnlyCollection<CreateHawalaDto> items)
            {
                if (items.Count == 0)
                    return [];

                if (items.All(x => x.HawalaType == "HawalaReceive" &&
                                   x.CommissionAmount is null &&
                                   x.AgentCommissionAmount is null))
                {
                    return await CreateImportedReceiveHawalasAsync(items);
                }

                await using var transaction = _context.Database.CurrentTransaction == null
                    ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                    : null;
                try
                {
                    var results = new List<HawalaDto>(items.Count);
                    foreach (var item in items)
                        results.Add(await CreateHawalaAsync(item));

                    if (transaction != null)
                        await transaction.CommitAsync();
                    return results;
                }
                catch
                {
                    if (transaction != null)
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

            private async Task EnsureNotSettlementConvertedAsync(IReadOnlyCollection<long> hawalaIds)
            {
                if (await _context.CorrespondentSettlementConversionHawalas
                    .AnyAsync(x => hawalaIds.Contains(x.HawalaId)))
                {
                    throw new InvalidOperationException(
                        "حواله‌ای که به ارز توافقی تبدیل شده قابل ویرایش، حذف یا لغو نیست. برای اصلاح، سند تبدیل باید با عملیات برگشتی اصلاح شود.");
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
                var currentPeriodStart = await GetCurrentPeriodStartAsync(correspondentId);
                var lastNumber = await _context.Hawalas
                    .Where(h => h.CorrespondentId == correspondentId &&
                                h.HawalaType == hawalaType &&
                                h.CreatedAt >= currentPeriodStart)
                    .OrderByDescending(h => h.Number)
                    .Select(h => (long?)h.Number)
                    .FirstOrDefaultAsync();

                return (lastNumber ?? 0) + 1;
            }

            public async Task<HawalaDto?> GetHawalaByIdAsync(long id)
            {
                var hawala = await _context.Hawalas
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(h => h.Correspondent).ThenInclude(c => c!.SettlementCurrency)
                    .Include(h => h.SettlementConversionLinks).ThenInclude(x => x.Conversion)
                    .Include(h => h.SettlementConversionItems).ThenInclude(x => x.SourceCurrency)
                    .Include(h => h.SettlementConversionItems).ThenInclude(x => x.Conversion).ThenInclude(x => x.TargetCurrency)
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
                result.IsBulkImportGeneratedSend = hawala.IsSystemGenerated &&
                    await _context.HawalaImportRows.AsNoTracking()
                        .AnyAsync(x => x.GeneratedSendHawalaId == hawala.Id);
                result.HasPeriodicCommissionHistory = await _context.CorrespondentCommissionBatchItems
                    .AsNoTracking()
                    .AnyAsync(x => x.HawalaId == hawala.Id);
                result.PeriodicCommissionAfn = await _context.CorrespondentCommissionBatchItems
                    .AsNoTracking()
                    .Where(x => x.HawalaId == hawala.Id && x.IsActive && x.Batch.Status == "Posted")
                    .Select(x => (decimal?)x.CommissionAfn)
                    .FirstOrDefaultAsync();

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

            public async Task<HawalaListResultDto> GetHawalasAsync(
                HawalaFilterDto filter,
                CancellationToken cancellationToken = default)
            {
                var query = _context.Hawalas
                    .AsNoTracking()
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
                        (h.Correspondent != null && h.Correspondent.Name.Contains(term)) ||
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

                var totalCount = await query.CountAsync(cancellationToken);
                var totalFromAmount = filter.IncludeTotalAmount && totalCount > 0
                    ? await query.SumAsync(h => h.FromAmount, cancellationToken)
                    : 0;
                var pageNumber = Math.Max(1, filter.PageNumber);

                query = filter.SortDirection == "asc"
                    ? query.OrderBy(GetSortExpression(filter.SortColumn))
                    : query.OrderByDescending(GetSortExpression(filter.SortColumn));

                if (filter.PageSize > 0)
                {
                    query = query.Skip((pageNumber - 1) * filter.PageSize)
                                 .Take(filter.PageSize);
                }

                // Project in SQL so list pages load only the fields used by HawalaDto.
                // This avoids tracking and materializing the large navigation graph
                // that was previously loaded by ten Include calls.
                var items = await query
                    .Select(GetListProjection())
                    .ToListAsync(cancellationToken);
                var systemGeneratedIds = items
                    .Where(x => x.IsSystemGenerated)
                    .Select(x => x.Id)
                    .ToList();
                if (systemGeneratedIds.Count > 0)
                {
                    var importedGeneratedIds = await _context.HawalaImportRows
                        .AsNoTracking()
                        .Where(x => x.GeneratedSendHawalaId.HasValue &&
                                    systemGeneratedIds.Contains(x.GeneratedSendHawalaId.Value))
                        .Select(x => x.GeneratedSendHawalaId!.Value)
                        .ToHashSetAsync(cancellationToken);
                    foreach (var item in items)
                        item.IsBulkImportGeneratedSend = importedGeneratedIds.Contains(item.Id);
                }
                return new HawalaListResultDto
                {
                    Items = items,
                    TotalCount = totalCount,
                    TotalFromAmount = totalFromAmount,
                    TotalPages = filter.PageSize > 0
                        ? (int)Math.Ceiling((double)totalCount / filter.PageSize)
                        : totalCount > 0 ? 1 : 0
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

            private Expression<Func<Hawala, HawalaDto>> GetListProjection() => h => new HawalaDto
            {
                Id = h.Id,
                Number = h.Number,
                HawalaType = h.HawalaType,
                CorrespondentId = h.CorrespondentId,
                CorrespondentName = h.Correspondent != null ? h.Correspondent.Name : null,
                PaymentLocationId = h.PaymentLocationId,
                PaymentLocationName = h.PaymentLocation != null ? h.PaymentLocation.Name : null,
                PaymentLocationAddress = h.PaymentLocation != null ? h.PaymentLocation.Address : null,
                SenderName = h.SenderName,
                SenderFatherName = h.SenderFatherName,
                SenderPhone = h.SenderPhone,
                SenderTazkiraNumber = h.SenderTazkiraNumber,
                SenderTazkiraImagePath = h.SenderTazkiraImagePath,
                SenderAddress = h.SenderAddress,
                ReceiverName = h.ReceiverName,
                ReceiverFatherName = h.ReceiverFatherName,
                ReceiverPhone = h.ReceiverPhone,
                ReceiverTazkiraNumber = h.ReceiverTazkiraNumber,
                ReceiverTazkiraImagePath = h.ReceiverTazkiraImagePath,
                ReceiverAddress = h.ReceiverAddress,
                FromCurrencyId = h.FromCurrencyId,
                FromCurrencyCode = h.FromCurrency != null ? h.FromCurrency.Code : string.Empty,
                FromCurrencyName = h.FromCurrency != null ? h.FromCurrency.Name : string.Empty,
                FromAmount = h.FromAmount,
                ToCurrencyId = h.ToCurrencyId,
                ToCurrencyCode = h.ToCurrency != null ? h.ToCurrency.Code : string.Empty,
                ToCurrencyName = h.ToCurrency != null ? h.ToCurrency.Name : string.Empty,
                ToAmount = h.ToAmount,
                ExchangeRate = h.ExchangeRate,
                CommissionAmount = h.CommissionAmount,
                CommissionCurrencyId = h.CommissionCurrencyId,
                CommissionCurrencyCode = h.CommissionCurrency != null ? h.CommissionCurrency.Code : null,
                CommissionCurrencyName = h.CommissionCurrency != null ? h.CommissionCurrency.Name : null,
                AgentCommissionAmount = h.AgentCommissionAmount,
                AgentCommissionCurrencyId = h.AgentCommissionCurrencyId,
                AgentCommissionCurrencyCode = h.AgentCommissionCurrency != null ? h.AgentCommissionCurrency.Code : null,
                AgentCommissionCurrencyName = h.AgentCommissionCurrency != null ? h.AgentCommissionCurrency.Name : null,
                ReferenceNumber = h.ReferenceNumber,
                Notes = h.Notes,
                Status = h.Status,
                CreatedAt = h.CreatedAt,
                CreatedBy = h.CreatedBy,
                CreatedByName = h.CreatedByUser != null ? h.CreatedByUser.FullName : string.Empty,
                PaidAt = h.PaidAt,
                PaidBy = h.PaidBy,
                PaidFromAccountId = h.PaidFromAccountId,
                PaidFromAccountName = h.PaidFromAccount != null ? h.PaidFromAccount.AccountName : null,
                PaidFromAccountType = h.PaidFromAccount != null ? h.PaidFromAccount.AccountType : null,
                SourceHawalaId = h.SourceHawalaId,
                IsSystemGenerated = h.IsSystemGenerated,
                HasPeriodicCommissionHistory = _context.CorrespondentCommissionBatchItems
                    .Any(x => x.HawalaId == h.Id),
                PeriodicCommissionAfn = _context.CorrespondentCommissionBatchItems
                    .Where(x => x.HawalaId == h.Id && x.IsActive && x.Batch.Status == "Posted")
                    .Select(x => (decimal?)x.CommissionAfn)
                    .FirstOrDefault(),
                CancelledAt = h.CancelledAt,
                CancelledBy = h.CancelledBy,
                CancelReason = h.CancelReason,
                SettlementCurrencyId = h.Correspondent != null ? h.Correspondent.SettlementCurrencyId : null,
                SettlementCurrencyCode = h.Correspondent != null && h.Correspondent.SettlementCurrency != null
                    ? h.Correspondent.SettlementCurrency.Code
                    : null,
                IsSettlementConverted = h.SettlementConversionLinks.Any(),
                SettlementRates = h.SettlementConversionItems.Select(x => new HawalaSettlementRateInfoDto
                {
                    SourceCurrencyId = x.SourceCurrencyId,
                    SourceCurrencyCode = x.SourceCurrency.Code,
                    SourceAmount = x.SourceTalabKar > 0 ? x.SourceTalabKar : x.SourceBadehKar,
                    ExchangeRate = x.ExchangeRate,
                    TargetAmount = x.TargetTalabKar > 0 ? x.TargetTalabKar : x.TargetBadehKar,
                    TargetCurrencyCode = x.Conversion.TargetCurrency.Code
                }).ToList(),
                CanEditSettlementRate = h.SettlementConversionItems.Any(x => x.Conversion.SourceMode == "Hawalas")
            };

            public async Task<HawalaStatisticsDto> GetStatisticsAsync(
                CancellationToken cancellationToken = default)
            {
                var connection = (SqlConnection)_context.Database.GetDbConnection();
                var shouldClose = connection.State != ConnectionState.Open;
                if (shouldClose)
                    await connection.OpenAsync(cancellationToken);

                try
                {
                    var transaction = _context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
                    await using var command = new SqlCommand(
                        "[dbo].[usp_GetHawalaStatistics_v1]", connection, transaction)
                    {
                        CommandType = CommandType.StoredProcedure,
                        CommandTimeout = 30
                    };
                    command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;

                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                        return new HawalaStatisticsDto();

                    return new HawalaStatisticsDto
                    {
                        HawalaSendCount = checked((int)reader.GetInt64(0)),
                        HawalaReceiveCount = checked((int)reader.GetInt64(1)),
                        HawalaOtherCount = checked((int)reader.GetInt64(2)),
                        PendingCount = checked((int)reader.GetInt64(3)),
                        PaidCount = checked((int)reader.GetInt64(4))
                    };
                }
                finally
                {
                    if (shouldClose)
                        await connection.CloseAsync();
                }
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
                    if (dto.Status is not null and not ("Pending" or "Paid"))
                        throw new InvalidOperationException("وضعیت حواله برای ویرایش معتبر نیست.");

                    var activePeriodicCommissionItem = await _context.CorrespondentCommissionBatchItems
                        .Include(x => x.Batch)
                        .ThenInclude(x => x.Items)
                        .FirstOrDefaultAsync(x => x.HawalaId == hawala.Id && x.IsActive && x.Batch.Status == "Posted");
                    var commissionAffectingChange = activePeriodicCommissionItem != null &&
                        ((dto.CorrespondentId ?? hawala.CorrespondentId) != hawala.CorrespondentId ||
                         (hawala.HawalaType == "HawalaSend"
                             ? (dto.ToCurrencyId ?? hawala.ToCurrencyId) != hawala.ToCurrencyId ||
                               (dto.ToAmount ?? hawala.ToAmount) != hawala.ToAmount
                             : (dto.FromCurrencyId ?? hawala.FromCurrencyId) != hawala.FromCurrencyId ||
                               (dto.FromAmount ?? hawala.FromAmount) != hawala.FromAmount));
                    if (commissionAffectingChange && dto.PeriodicCommissionHandling is not ("Recalculate" or "KeepPrevious"))
                    {
                        throw new InvalidOperationException(
                            "کمیشن این حواله قبلاً محاسبه شده است. محاسبه مجدد یا حفظ کمیشن قبلی را انتخاب کنید.");
                    }
                    if (commissionAffectingChange && dto.PeriodicCommissionHandling == "Recalculate" &&
                        dto.CorrespondentId.HasValue && dto.CorrespondentId != hawala.CorrespondentId)
                    {
                        throw new InvalidOperationException(
                            "با تغییر نمایندگی، کمیشن قبلی را نمی‌توان داخل سند نمایندگی قبلی محاسبه مجدد کرد؛ گزینه حفظ کمیشن قبلی را انتخاب کنید.");
                    }

                    var settlementReconversion = await DetachSettlementConversionAsync(hawala.Id);

                    if (hawala.IsSystemGenerated && await _context.HawalaImportRows
                            .AsNoTracking()
                            .AnyAsync(x => x.GeneratedSendHawalaId == hawala.Id))
                    {
                        throw new InvalidOperationException(
                            "حواله ارسالی ایجادشده توسط آپلود گروهی قابل ویرایش نیست.");
                    }

                    var oldSenderTazkiraImagePath = hawala.SenderTazkiraImagePath;
                    var oldReceiverTazkiraImagePath = hawala.ReceiverTazkiraImagePath;

                    var updatedStatus = dto.Status ?? hawala.Status;
                    var paymentLocationChanged = dto.PaymentLocationId != hawala.PaymentLocationId;
                    var fromAccountId = (paymentLocationChanged || hawala.Status != "Paid") &&
                                        hawala.HawalaType == "HawalaReceive" &&
                                        updatedStatus == "Paid"
                        ? await ResolvePaymentLocationAccountAsync(dto.PaymentLocationId)
                        : dto.FromAccountId ?? await ResolveExistingFromAccountIdAsync(hawala);

                    var generatedHawala = await _context.Hawalas
                        .FirstOrDefaultAsync(x => x.SourceHawalaId == hawala.Id);
                    var generatedSettlementReconversion = generatedHawala == null
                        ? null
                        : await DetachSettlementConversionAsync(generatedHawala.Id);
                    var generatedHawalaNumber =
                        dto.GeneratedSendHawalaNumber ??
                        generatedHawala?.Number;
                    var generatedAgentCommissionAmount = generatedHawala?.AgentCommissionAmount;
                    var generatedAgentCommissionCurrencyId = generatedHawala?.AgentCommissionCurrencyId;
                    var generatedReferenceNumber = generatedHawala?.ReferenceNumber;
                    List<HawalaImportRow> linkedImportRows = await _context.HawalaImportRows
                        .Where(x => x.HawalaId == hawala.Id)
                        .ToListAsync();

                    if (generatedHawala != null)
                    {
                        if (linkedImportRows.Count > 0)
                        {
                            foreach (var importRow in linkedImportRows)
                                importRow.GeneratedSendHawalaId = null;
                            await _context.SaveChangesAsync();
                        }
                        await DeleteHawalaLedgerEntriesAsync(generatedHawala.Id);
                        _context.Hawalas.Remove(generatedHawala);
                    }

                    await DeleteHawalaLedgerEntriesAsync(hawala.Id);
                    await _context.SaveChangesAsync();

                    ApplyUpdate(hawala, dto);
                    await NormalizeHawalaConversionAsync(hawala);
                    await ValidateUpdatedHawalaAsync(hawala);

                    if (commissionAffectingChange && dto.PeriodicCommissionHandling == "Recalculate")
                        await RecalculatePeriodicCommissionInPlaceAsync(hawala, activePeriodicCommissionItem!);

                    await ProcessLedgerEntries(
                        hawala,
                        fromAccountId,
                        generatedHawalaNumber,
                        generatedAgentCommissionAmount,
                        generatedAgentCommissionCurrencyId,
                        generatedReferenceNumber);

                    await _context.SaveChangesAsync();

                    if (settlementReconversion != null)
                        await ReconvertSettlementAsync(hawala, settlementReconversion);

                    if (generatedSettlementReconversion != null)
                    {
                        var replacementGenerated = await _context.Hawalas
                            .SingleAsync(x => x.SourceHawalaId == hawala.Id);
                        await ReconvertSettlementAsync(replacementGenerated, generatedSettlementReconversion);
                    }

                    if (linkedImportRows.Count > 0)
                    {
                        var replacementGeneratedId = await _context.Hawalas
                            .Where(x => x.SourceHawalaId == hawala.Id)
                            .Select(x => (long?)x.Id)
                            .FirstOrDefaultAsync();
                        var replacementGenerated = replacementGeneratedId.HasValue
                            ? await _context.Hawalas.AsNoTracking()
                                .SingleAsync(x => x.Id == replacementGeneratedId.Value)
                            : null;
                        foreach (var importRow in linkedImportRows)
                        {
                            importRow.GeneratedSendHawalaId = replacementGeneratedId;
                            importRow.PaymentLocationId = hawala.PaymentLocationId;
                            importRow.DestinationCorrespondentId = replacementGenerated?.CorrespondentId;
                            importRow.Amount = hawala.FromAmount;
                            importRow.CurrencyId = hawala.FromCurrencyId;
                            importRow.AgentCommissionAmount = replacementGenerated?.AgentCommissionAmount;
                            importRow.AgentCommissionCurrencyId = replacementGenerated?.AgentCommissionCurrencyId;
                        }
                        await _context.SaveChangesAsync();
                    }

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

            private void ApplyUpdate(Hawala hawala, UpdateHawalaDto dto)
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
                if (!string.IsNullOrWhiteSpace(dto.Status) && dto.Status != hawala.Status)
                {
                    hawala.Status = dto.Status;
                    if (dto.Status == "Paid")
                    {
                        hawala.PaidAt = DateTime.UtcNow;
                        hawala.PaidBy = GetCurrentUserId();
                    }
                    else
                    {
                        hawala.PaidAt = null;
                        hawala.PaidBy = null;
                        hawala.PaidFromAccountId = null;
                    }
                }
            }

            private async Task<long> ResolvePaymentLocationAccountAsync(long? paymentLocationId)
            {
                if (!paymentLocationId.HasValue)
                    throw new InvalidOperationException("برای حواله پرداخت‌شده، محل پرداخت الزامی است.");

                var location = await _context.PaymentLocations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == paymentLocationId.Value)
                    ?? throw new InvalidOperationException("محل پرداخت انتخاب‌شده معتبر نیست.");

                var correspondents = await _context.Correspondents
                    .AsNoTracking()
                    .Where(x => !x.IsArchived)
                    .Select(x => new { x.Id, x.Name })
                    .ToListAsync();
                var correspondent = correspondents.FirstOrDefault(x =>
                    PaymentLocationNameNormalizer.Normalize(x.Name) == location.NormalizedName);
                if (correspondent is null)
                    throw new InvalidOperationException(
                        $"نمایندگی مربوط به محل پرداخت «{location.Name}» پیدا نشد.");

                var accountId = await _context.Accounts
                    .AsNoTracking()
                    .Where(x => !x.IsArchived && x.CorrespondentId == correspondent.Id)
                    .OrderBy(x => x.Id)
                    .Select(x => (long?)x.Id)
                    .FirstOrDefaultAsync();
                return accountId ?? throw new InvalidOperationException(
                    $"حساب فعال نمایندگی «{correspondent.Name}» پیدا نشد.");
            }

            private async Task RecalculatePeriodicCommissionInPlaceAsync(
                Hawala hawala,
                CorrespondentCommissionBatchItem item)
            {
                var batch = item.Batch;
                var isOutgoingCommission = hawala.HawalaType == "HawalaSend";
                decimal sourceToAfnRate;
                decimal commissionBase;
                if (!isOutgoingCommission)
                {
                    var currencyCode = await _context.Currencies
                        .Where(x => x.Id == hawala.FromCurrencyId)
                        .Select(x => x.Code)
                        .SingleAsync();
                    if (string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceToAfnRate = 1m;
                        commissionBase = hawala.FromAmount;
                        hawala.CommissionUsdToAfnRate = null;
                    }
                    else if (string.Equals(currencyCode, "AFN", StringComparison.OrdinalIgnoreCase))
                    {
                        var valuationDate = hawala.CreatedAt.ToLocalTime().Date;
                        sourceToAfnRate = await _context.DailyCommissionRates
                            .Where(x => x.RateDate == valuationDate)
                            .Select(x => (decimal?)x.UsdToAfnRate)
                            .SingleOrDefaultAsync()
                            ?? throw new InvalidOperationException(
                                $"نرخ پایان روز {valuationDate:yyyy-MM-dd} ثبت نشده است؛ ابتدا نرخ را در روزنامچه ثبت کنید.");
                        commissionBase = hawala.FromAmount / sourceToAfnRate;
                        hawala.CommissionUsdToAfnRate = sourceToAfnRate;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "کمیشن دوره‌ای حواله دریافتی فقط برای ارزهای USD و AFN قابل محاسبه است.");
                    }

                    commissionBase = decimal.Round(
                        commissionBase, 8, MidpointRounding.AwayFromZero);
                    hawala.CommissionBaseUsdAmount = commissionBase;
                    hawala.CommissionValuationDate = hawala.CreatedAt.ToLocalTime().Date;
                    hawala.CommissionValuedAt = DateTime.UtcNow;
                }
                else
                {
                    var currencyCode = await _context.Currencies
                        .Where(x => x.Id == hawala.ToCurrencyId)
                        .Select(x => x.Code)
                        .SingleAsync();
                    var settlementAmount = hawala.ToAmount ?? hawala.FromAmount;
                    var valuationDate = hawala.CreatedAt.ToLocalTime().Date;
                    if (string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceToAfnRate = 1m;
                        hawala.CommissionUsdToAfnRate = null;
                    }
                    else if (string.Equals(currencyCode, "AFN", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceToAfnRate = await _context.DailyCommissionRates
                            .Where(x => x.RateDate == valuationDate)
                            .Select(x => (decimal?)x.UsdToAfnRate)
                            .SingleOrDefaultAsync()
                            ?? throw new InvalidOperationException(
                                $"نرخ پایان روز {valuationDate:yyyy-MM-dd} ثبت نشده است؛ ابتدا نرخ را در روزنامچه ثبت کنید.");
                        hawala.CommissionUsdToAfnRate = sourceToAfnRate;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "کمیشن دوره‌ای حواله ارسالی فقط برای ارزهای USD و AFN قابل محاسبه است.");
                    }

                    var originalCommission = decimal.Round(
                        settlementAmount / 100000m * batch.CommissionPerLakhAfn,
                        0, MidpointRounding.AwayFromZero);
                    commissionBase = string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase)
                        ? originalCommission
                        : decimal.Round(originalCommission / sourceToAfnRate, 0,
                            MidpointRounding.AwayFromZero);
                    hawala.CommissionBaseUsdAmount = decimal.Round(
                        settlementAmount / (sourceToAfnRate == 1m ? 1m : sourceToAfnRate),
                        8, MidpointRounding.AwayFromZero);
                    hawala.CommissionValuationDate = valuationDate;
                    hawala.CommissionValuedAt = DateTime.UtcNow;

                    item.SourceCurrencyId = hawala.ToCurrencyId;
                    item.SourceAmount = settlementAmount;
                    item.SourceToAfnRate = sourceToAfnRate;
                    item.AfnEquivalent = commissionBase;
                    item.CommissionAfn = originalCommission;
                }

                if (!isOutgoingCommission)
                {
                    item.SourceCurrencyId = hawala.FromCurrencyId;
                    item.SourceAmount = hawala.FromAmount;
                    item.SourceToAfnRate = sourceToAfnRate;
                    item.AfnEquivalent = decimal.Round(commissionBase, 4, MidpointRounding.AwayFromZero);
                    item.CommissionAfn = decimal.Round(
                        item.AfnEquivalent / 100000m * batch.CommissionPerLakhAfn,
                        4, MidpointRounding.AwayFromZero);
                }

                batch.TotalBaseAfn = decimal.Round(
                    batch.Items.Where(x => x.IsActive).Sum(x => x.AfnEquivalent),
                    isOutgoingCommission ? 0 : 4, MidpointRounding.AwayFromZero);
                decimal postingAmount;
                if (isOutgoingCommission)
                {
                    var currencyCodes = await _context.Currencies.AsNoTracking()
                        .Where(x => x.Code == "AFN" || x.Code == "USD")
                        .ToDictionaryAsync(x => x.Id, x => x.Code);
                    batch.TotalCommissionAfn = decimal.Round(batch.Items
                        .Where(x => x.IsActive && currencyCodes.GetValueOrDefault(x.SourceCurrencyId) == "AFN")
                        .Sum(x => x.CommissionAfn), 0, MidpointRounding.AwayFromZero);
                    batch.TotalCommissionUsd = decimal.Round(batch.Items
                        .Where(x => x.IsActive && currencyCodes.GetValueOrDefault(x.SourceCurrencyId) == "USD")
                        .Sum(x => x.CommissionAfn), 0, MidpointRounding.AwayFromZero);
                    postingAmount = batch.TotalBaseAfn;
                    await RebuildOutgoingPeriodicCommissionLedgerAsync(batch);
                }
                else
                {
                    var calculatedCommission = decimal.Round(
                        batch.TotalBaseAfn / 100000m * batch.CommissionPerLakhAfn,
                        0, MidpointRounding.AwayFromZero);
                    batch.TotalCommissionAfn = 0;
                    batch.TotalCommissionUsd = calculatedCommission;
                    postingAmount = batch.TotalCommissionUsd;

                    var ledgerEntries = await _context.LedgerEntries
                        .Where(x => x.TransactionId == batch.PostingTransactionId)
                        .ToListAsync();
                    if (ledgerEntries.Count != 2)
                        throw new InvalidOperationException("سند حسابداری کمیشن دوره‌ای برای به‌روزرسانی معتبر نیست.");

                    foreach (var entry in ledgerEntries)
                    {
                        if (entry.TalabKar > 0)
                            entry.TalabKar = postingAmount;
                        if (entry.BadehKar > 0)
                            entry.BadehKar = postingAmount;
                    }
                }

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = GetCurrentUserId(),
                    Action = "RECALCULATE_PERIODIC_COMMISSION",
                    TableName = "CorrespondentCommissionBatchItems",
                    RecordId = item.Id,
                    NewValue = $"کمیشن حواله {hawala.Id} در همان سند قبلی به‌روزرسانی شد.",
                    CreatedAt = DateTime.UtcNow
                });
            }

            private async Task RebuildOutgoingPeriodicCommissionLedgerAsync(
                CorrespondentCommissionBatch batch)
            {
                var currencies = await _context.Currencies.AsNoTracking()
                    .Where(x => x.Code == "AFN" || x.Code == "USD")
                    .ToDictionaryAsync(x => x.Code, x => x.Id);
                if (!currencies.TryGetValue("AFN", out var afnId) ||
                    !currencies.TryGetValue("USD", out var usdId))
                    throw new InvalidOperationException("ارزهای فعال USD و AFN در سیستم یافت نشد.");

                var destinationAccountId = await _context.Accounts.AsNoTracking()
                    .Where(x => x.CorrespondentId == batch.CorrespondentId && !x.IsArchived)
                    .OrderBy(x => x.Id).Select(x => (long?)x.Id).FirstOrDefaultAsync()
                    ?? throw new InvalidOperationException("حساب فعال نمایندگی مقصد یافت نشد.");
                var clearing = await _context.Accounts
                    .SingleOrDefaultAsync(x => x.AccountCode == "SYS-SETTLEMENT-CLEARING");
                if (clearing == null)
                {
                    clearing = new Account
                    {
                        AccountCode = "SYS-SETTLEMENT-CLEARING",
                        AccountName = "حساب واسط تبدیل ارز نمایندگی‌ها",
                        AccountType = "CurrencyConversionClearing",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Accounts.Add(clearing);
                    await _context.SaveChangesAsync();
                }
                else if (clearing.IsArchived || clearing.AccountType != "CurrencyConversionClearing")
                    throw new InvalidOperationException("حساب واسط تبدیل ارز فعال و معتبر نیست.");

                var activeBatchItems = batch.Items.Where(x => x.IsActive).ToList();
                var hawalaIds = activeBatchItems.Select(x => x.HawalaId).ToArray();
                var sourceLinks = await _context.Hawalas.AsNoTracking()
                    .Where(x => hawalaIds.Contains(x.Id))
                    .Select(x => new { x.Id, SourceCorrespondentId = x.SourceHawala!.CorrespondentId })
                    .ToDictionaryAsync(x => x.Id, x => x.SourceCorrespondentId);
                var items = activeBatchItems.Select(x => new
                {
                    x.SourceCurrencyId,
                    x.AfnEquivalent,
                    SourceCorrespondentId = sourceLinks.GetValueOrDefault(x.HawalaId)
                }).ToList();
                var sourceIds = items.Where(x => x.SourceCorrespondentId.HasValue)
                    .Select(x => x.SourceCorrespondentId!.Value).Distinct().ToArray();
                var sourceAccounts = await _context.Accounts.AsNoTracking()
                    .Where(x => x.CorrespondentId.HasValue && sourceIds.Contains(x.CorrespondentId.Value) && !x.IsArchived)
                    .GroupBy(x => x.CorrespondentId!.Value)
                    .Select(x => new { CorrespondentId = x.Key, AccountId = x.Min(a => a.Id) })
                    .ToDictionaryAsync(x => x.CorrespondentId, x => x.AccountId);
                if (sourceIds.Any(id => !sourceAccounts.ContainsKey(id)))
                    throw new InvalidOperationException("حساب فعال نمایندگی فرستنده یک یا چند حواله یافت نشد.");
                Account? expenseAccount = null;
                if (items.Any(x => !x.SourceCorrespondentId.HasValue))
                {
                    expenseAccount = await _context.Accounts.SingleOrDefaultAsync(x => x.AccountCode == "5002");
                    if (expenseAccount == null)
                    {
                        expenseAccount = new Account
                        {
                            AccountCode = "5002", AccountName = "هزینه کمیشن حواله‌های ارسالی",
                            AccountType = "Expense", CreatedAt = DateTime.UtcNow
                        };
                        _context.Accounts.Add(expenseAccount);
                        await _context.SaveChangesAsync();
                    }
                    else if (expenseAccount.IsArchived || expenseAccount.AccountType != "Expense")
                        throw new InvalidOperationException("حساب 5002 باید یک حساب هزینه فعال باشد.");
                }

                var oldEntries = await _context.LedgerEntries
                    .Where(x => x.TransactionId == batch.PostingTransactionId).ToListAsync();
                _context.LedgerEntries.RemoveRange(oldEntries);
                var description = $"کمیشن حواله‌های ارسالی نمایندگی، {items.Count} حواله";
                void Add(long accountId, long currencyId, decimal talabKar, decimal badehKar) =>
                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        TransactionId = batch.PostingTransactionId,
                        AccountId = accountId,
                        CurrencyId = currencyId,
                        TalabKar = talabKar,
                        BadehKar = badehKar,
                        Description = description,
                        CreatedAt = DateTime.UtcNow
                    });

                if (batch.TotalCommissionUsd > 0)
                    Add(destinationAccountId, usdId, batch.TotalCommissionUsd, 0);
                if (batch.TotalCommissionAfn > 0)
                {
                    Add(destinationAccountId, afnId, batch.TotalCommissionAfn, 0);
                    Add(clearing.Id, afnId, 0, batch.TotalCommissionAfn);
                    var afnUsd = decimal.Round(items
                        .Where(x => x.SourceCurrencyId == afnId).Sum(x => x.AfnEquivalent),
                        0, MidpointRounding.AwayFromZero);
                    if (afnUsd > 0)
                        Add(clearing.Id, usdId, afnUsd, 0);
                }
                foreach (var group in items.Where(x => x.SourceCorrespondentId.HasValue)
                             .GroupBy(x => x.SourceCorrespondentId!.Value))
                {
                    var amount = decimal.Round(group.Sum(x => x.AfnEquivalent), 0,
                        MidpointRounding.AwayFromZero);
                    if (amount > 0)
                        Add(sourceAccounts[group.Key], usdId, 0, amount);
                }
                var ownOfficeAmount = decimal.Round(items
                    .Where(x => !x.SourceCorrespondentId.HasValue).Sum(x => x.AfnEquivalent),
                    0, MidpointRounding.AwayFromZero);
                if (ownOfficeAmount > 0 && expenseAccount != null)
                    Add(expenseAccount.Id, usdId, 0, ownOfficeAmount);
            }

            private sealed record SettlementReconversionState(
                long CorrespondentId,
                long TargetCurrencyId,
                IReadOnlyDictionary<long, decimal> Rates);

            private async Task<SettlementReconversionState?> DetachSettlementConversionAsync(long hawalaId)
            {
                var link = await _context.CorrespondentSettlementConversionHawalas
                    .Include(x => x.Conversion)
                    .ThenInclude(x => x.HawalaItems)
                    .ThenInclude(x => x.LedgerEntries)
                    .SingleOrDefaultAsync(x => x.HawalaId == hawalaId);
                if (link == null)
                    return null;

                if (link.Conversion.SourceMode != "Hawalas")
                {
                    throw new InvalidOperationException(
                        "این حواله از طریق تبدیل کلی حساب به ارز توافقی تبدیل شده است و سهم آن جداگانه قابل بازسازی نیست.");
                }

                var items = link.Conversion.HawalaItems
                    .Where(x => x.HawalaId == hawalaId)
                    .ToList();
                var rates = items
                    .GroupBy(x => x.SourceCurrencyId)
                    .ToDictionary(x => x.Key, x => x.Last().ExchangeRate);

                _context.LedgerEntries.RemoveRange(items.SelectMany(x => x.LedgerEntries));
                _context.CorrespondentSettlementConversionHawalaItems.RemoveRange(items);
                _context.CorrespondentSettlementConversionHawalas.Remove(link);

                return new SettlementReconversionState(
                    link.Conversion.CorrespondentId,
                    link.Conversion.TargetCurrencyId,
                    rates);
            }

            private async Task ReconvertSettlementAsync(
                Hawala hawala,
                SettlementReconversionState previous)
            {
                if (!hawala.CorrespondentId.HasValue)
                    throw new InvalidOperationException("برای تبدیل مجدد، نمایندگی حواله الزامی است.");

                var settlement = await _settlementService.GetPreviewAsync(hawala.CorrespondentId.Value);
                var settlementSourceCurrencyId = hawala.HawalaType == "HawalaSend"
                    ? hawala.ToCurrencyId
                    : hawala.FromCurrencyId;
                if (settlementSourceCurrencyId == settlement.TargetCurrencyId)
                    return;

                var balances = await _settlementService.GetHawalaPreviewAsync(
                    hawala.CorrespondentId.Value, [hawala.Id]);
                var rates = new List<HawalaSettlementRateDto>();
                foreach (var balance in balances)
                {
                    decimal rate = 0;
                    if (previous.CorrespondentId == hawala.CorrespondentId.Value &&
                        previous.TargetCurrencyId == settlement.TargetCurrencyId)
                    {
                        previous.Rates.TryGetValue(balance.SourceCurrencyId, out rate);
                    }

                    if (rate <= 0)
                    {
                        rate = await _context.CorrespondentSettlementConversionHawalaItems
                            .AsNoTracking()
                            .Where(x => x.SourceCurrencyId == balance.SourceCurrencyId &&
                                        x.ExchangeRate > 0 &&
                                        x.Conversion.CorrespondentId == hawala.CorrespondentId.Value &&
                                        x.Conversion.TargetCurrencyId == settlement.TargetCurrencyId)
                            .OrderByDescending(x => x.Conversion.CreatedAt)
                            .Select(x => x.ExchangeRate)
                            .FirstOrDefaultAsync();
                    }

                    if (rate <= 0)
                    {
                        throw new InvalidOperationException(
                            $"برای تبدیل مجدد ارز {balance.SourceCurrencyCode} به {settlement.TargetCurrencyCode} نرخ قبلی پیدا نشد.");
                    }

                    rates.Add(new HawalaSettlementRateDto
                    {
                        HawalaId = hawala.Id,
                        SourceCurrencyId = balance.SourceCurrencyId,
                        Rate = rate
                    });
                }

                await _settlementService.ConvertHawalasAsync(new ConvertHawalasToSettlementDto
                {
                    CorrespondentId = hawala.CorrespondentId.Value,
                    HawalaIds = [hawala.Id],
                    HawalaRates = rates,
                    Note = $"تبدیل مجدد خودکار پس از ویرایش حواله شماره {hawala.Number}"
                });
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

                await EnforceCorrespondentCommissionMethodAsync(
                    hawala.HawalaType, hawala.CorrespondentId, hawala.CommissionAmount);

                await ValidatePaymentLocationAsync(hawala.PaymentLocationId, requireActive: false);

                var currentPeriodStart = await GetCurrentPeriodStartAsync(hawala.CorrespondentId);
                var duplicateNumber = await _context.Hawalas.AnyAsync(x =>
                    x.Id != hawala.Id &&
                    x.CorrespondentId == hawala.CorrespondentId &&
                    x.HawalaType == hawala.HawalaType &&
                    x.CreatedAt >= currentPeriodStart &&
                    x.Number == hawala.Number);

                if (duplicateNumber)
                {
                    throw new InvalidOperationException(
                        $"شماره {hawala.Number} برای این نوع حواله و نمایندگی قبلاً ثبت شده است.");
                }
            }

            private async Task ValidatePaymentLocationAsync(
                long? paymentLocationId,
                bool requireActive = true)
            {
                if (!paymentLocationId.HasValue)
                    return;

                var exists = await _context.PaymentLocations
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.Id == paymentLocationId.Value &&
                        (!requireActive || x.IsActive));

                if (!exists)
                {
                    throw new InvalidOperationException(
                        "محل پرداخت انتخاب‌شده معتبر یا فعال نیست.");
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
                    var deleteCandidateIds = generatedHawala == null
                        ? new[] { hawala.Id }
                        : new[] { hawala.Id, generatedHawala.Id };
                    if (await _context.CorrespondentCommissionBatchItems
                        .AnyAsync(x => deleteCandidateIds.Contains(x.HawalaId)))
                    {
                        throw new InvalidOperationException(
                            "حواله‌ای که کمیشن دوره‌ای آن محاسبه شده قابل حذف نیست؛ در صورت نیاز آن را لغو کنید.");
                    }
                    await EnsureNotSettlementConvertedAsync(generatedHawala == null
                        ? [hawala.Id]
                        : [hawala.Id, generatedHawala.Id]);
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
                    AgentCommissionAmount = hawala.AgentCommissionAmount,
                    AgentCommissionCurrencyId = hawala.AgentCommissionCurrencyId,
                    ReceiverName = hawala.ReceiverName ?? string.Empty,
                    ReceiverFatherName = hawala.ReceiverFatherName,
                    ReceiverPhone = hawala.ReceiverPhone,
                    ReceiverTazkiraNumber = hawala.ReceiverTazkiraNumber,
                    ReceiverTazkiraImagePath = hawala.ReceiverTazkiraImagePath,
                    ReceiverAddress = hawala.ReceiverAddress
                });
            }

            public async Task<HawalaDto> AddAgentCommissionAsync(
                long id,
                AddHawalaAgentCommissionDto commission)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                try
                {
                    var hawala = await _context.Hawalas.FirstOrDefaultAsync(x => x.Id == id)
                        ?? throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");
                    if (hawala.HawalaType != "HawalaSend")
                        throw new InvalidOperationException("کمیشن نمایندگی فقط به حواله ارسالی اضافه می‌شود.");
                    if (hawala.Status == "Cancel")
                        throw new InvalidOperationException("برای حواله لغوشده نمی‌توان کمیشن اضافه کرد.");
                    if (hawala.AgentCommissionAmount is > 0)
                        throw new InvalidOperationException("کمیشن نمایندگی قبلاً ثبت شده است.");
                    if (commission.Amount <= 0)
                        throw new InvalidOperationException("مقدار کمیشن باید بزرگتر از صفر باشد.");
                    if (!await _context.Currencies.AnyAsync(x => x.Id == commission.CurrencyId && x.IsActive))
                        throw new InvalidOperationException("ارز کمیشن معتبر یا فعال نیست.");
                    if (!hawala.CorrespondentId.HasValue)
                        throw new InvalidOperationException("نمایندگی مقصد حواله مشخص نیست.");

                    var correspondentAccount = await _context.Accounts
                        .FirstOrDefaultAsync(x => x.CorrespondentId == hawala.CorrespondentId && !x.IsArchived)
                        ?? throw new InvalidOperationException("حساب نمایندگی مقصد پیدا نشد.");
                    var expenseAccount = await GetOrCreatePayoutAgentCommissionExpenseAccountAsync();

                    hawala.AgentCommissionAmount = commission.Amount;
                    hawala.AgentCommissionCurrencyId = commission.CurrencyId;
                    if (hawala.Status == "Pending")
                    {
                        hawala.Status = "Paid";
                        hawala.PaidAt = DateTime.UtcNow;
                        hawala.PaidBy = GetCurrentUserId();
                    }

                    await CreateLedgerEntry(
                        hawala.Id,
                        expenseAccount.Id,
                        commission.CurrencyId,
                        talabKar: 0,
                        badehKar: commission.Amount,
                        description: $"حواله ارسالی {hawala.Id}: هزینه کمیشن عامل پرداخت");
                    await CreateLedgerEntry(
                        hawala.Id,
                        correspondentAccount.Id,
                        commission.CurrencyId,
                        talabKar: commission.Amount,
                        badehKar: 0,
                        description: $"حواله ارسالی {hawala.Id}: کمیشن قابل پرداخت به نمایندگی");

                    await _context.SaveChangesAsync();
                    await _auditLogService.LogAsync(
                        "UPDATE",
                        "Hawalas",
                        hawala.Id,
                        null,
                        $"کمیشن نمایندگی به مقدار {commission.Amount} برای حواله ارسالی {hawala.Number} ثبت شد",
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

            private async Task<IReadOnlyList<HawalaDto>> CreateImportedReceiveHawalasAsync(
                IReadOnlyCollection<CreateHawalaDto> items)
            {
                await using var transaction = _context.Database.CurrentTransaction == null
                    ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                    : null;
                try
                {
                    var result = await CreateImportedReceiveHawalasCoreAsync(items);
                    if (transaction != null)
                        await transaction.CommitAsync();
                    return result;
                }
                catch
                {
                    if (transaction != null)
                        await transaction.RollbackAsync();
                    throw;
                }
            }

            private async Task<IReadOnlyList<HawalaDto>> CreateImportedReceiveHawalasCoreAsync(
                IReadOnlyCollection<CreateHawalaDto> items)
            {
                var currentUserId = GetCurrentUserId();
                var now = DateTime.UtcNow;
                var correspondentIds = items.Select(x => x.CorrespondentId!.Value).Distinct().ToList();
                var paymentLocationIds = items.Where(x => x.PaymentLocationId.HasValue)
                    .Select(x => x.PaymentLocationId!.Value).Distinct().ToList();
                var accountIds = items.Where(x => x.FromAccountId.HasValue)
                    .Select(x => x.FromAccountId!.Value).Distinct().ToList();
                var currencyIds = items.SelectMany(x => new long?[]
                    {
                        x.FromCurrencyId,
                        x.ToCurrencyId,
                        x.GeneratedSendAgentCommissionCurrencyId
                    })
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .ToList();

                if (correspondentIds.Count == 0 || correspondentIds.Count !=
                    await _context.Correspondents.CountAsync(x => correspondentIds.Contains(x.Id) && !x.IsArchived))
                    throw new InvalidOperationException("یکی از نمایندگی‌های فرستنده معتبر یا فعال نیست.");
                if (paymentLocationIds.Count != await _context.PaymentLocations
                    .CountAsync(x => paymentLocationIds.Contains(x.Id) && x.IsActive))
                    throw new InvalidOperationException("یکی از محل‌های پرداخت معتبر یا فعال نیست.");
                if (currencyIds.Count != await _context.Currencies
                    .CountAsync(x => currencyIds.Contains(x.Id) && x.IsActive))
                    throw new InvalidOperationException("یکی از ارزهای حواله یا کمیشن معتبر یا فعال نیست.");

                var accounts = await _context.Accounts
                    .Where(x => (accountIds.Contains(x.Id) ||
                                 (x.CorrespondentId.HasValue && correspondentIds.Contains(x.CorrespondentId.Value))) &&
                                !x.IsArchived)
                    .ToListAsync();
                var sourceAccounts = accounts.Where(x => x.CorrespondentId.HasValue)
                    .GroupBy(x => x.CorrespondentId!.Value)
                    .ToDictionary(x => x.Key, x => x.First());
                var accountsById = accounts.ToDictionary(x => x.Id);
                foreach (var correspondentId in correspondentIds)
                    if (!sourceAccounts.ContainsKey(correspondentId))
                        throw new InvalidOperationException("حساب یکی از نمایندگی‌های فرستنده پیدا نشد.");
                foreach (var accountId in accountIds)
                    if (!accountsById.TryGetValue(accountId, out var account) ||
                        !string.Equals(account.AccountType, "Correspondent", StringComparison.OrdinalIgnoreCase) ||
                        !account.CorrespondentId.HasValue)
                        throw new InvalidOperationException("یکی از حساب‌های نمایندگی مقصد معتبر نیست.");

                var periodStarts = await _context.CorrespondentAccountPeriods.AsNoTracking()
                    .Where(x => correspondentIds.Contains(x.CorrespondentId))
                    .GroupBy(x => x.CorrespondentId)
                    .Select(x => new { CorrespondentId = x.Key, Start = x.Max(p => p.PeriodTo) })
                    .ToDictionaryAsync(x => x.CorrespondentId, x => x.Start);
                var requestedNumbers = items.Select(x => x.Number).ToHashSet();
                if (items.GroupBy(x => new { x.CorrespondentId, x.Number }).Any(x => x.Count() > 1))
                    throw new InvalidOperationException("شماره حواله دریافتی در فایل گروهی برای یک نمایندگی تکراری است.");
                var existingNumbers = await _context.Hawalas.AsNoTracking()
                    .Where(x => correspondentIds.Contains(x.CorrespondentId!.Value) &&
                                x.HawalaType == "HawalaReceive" && requestedNumbers.Contains(x.Number))
                    .Select(x => new { x.CorrespondentId, x.Number, x.CreatedAt })
                    .ToListAsync();
                if (existingNumbers.Any(x => x.CorrespondentId.HasValue &&
                    x.CreatedAt >= periodStarts.GetValueOrDefault(x.CorrespondentId.Value, DateTime.MinValue)))
                    throw new InvalidOperationException("شماره یکی از حواله‌های دریافتی قبلاً برای نمایندگی فرستنده ثبت شده است.");

                var outgoingItems = items.Where(x => x.Status == "Paid" && x.FromAccountId.HasValue).ToList();
                var outgoingCorrespondentIds = outgoingItems
                    .Select(x => accountsById[x.FromAccountId!.Value].CorrespondentId!.Value)
                    .Distinct().ToList();
                var outgoingNumbers = outgoingItems.Select(x => x.GeneratedSendHawalaNumber ?? x.Number).ToHashSet();
                if (outgoingItems.GroupBy(x => new
                    {
                        CorrespondentId = accountsById[x.FromAccountId!.Value].CorrespondentId!.Value,
                        Number = x.GeneratedSendHawalaNumber ?? x.Number
                    }).Any(x => x.Count() > 1))
                    throw new InvalidOperationException("شماره حواله ارسالی خودکار در فایل گروهی برای یک نمایندگی تکراری است.");
                var outgoingPeriodStarts = await _context.CorrespondentAccountPeriods.AsNoTracking()
                    .Where(x => outgoingCorrespondentIds.Contains(x.CorrespondentId))
                    .GroupBy(x => x.CorrespondentId)
                    .Select(x => new { CorrespondentId = x.Key, Start = x.Max(p => p.PeriodTo) })
                    .ToDictionaryAsync(x => x.CorrespondentId, x => x.Start);
                var existingOutgoingNumbers = outgoingItems.Count == 0
                    ? []
                    : await _context.Hawalas.AsNoTracking()
                        .Where(x => x.CorrespondentId.HasValue && outgoingCorrespondentIds.Contains(x.CorrespondentId.Value) &&
                                    x.HawalaType == "HawalaSend" && outgoingNumbers.Contains(x.Number))
                        .Select(x => new { CorrespondentId = x.CorrespondentId!.Value, x.Number, x.CreatedAt })
                        .ToListAsync();
                if (outgoingItems.Any(item => existingOutgoingNumbers.Any(existing =>
                        existing.CorrespondentId == accountsById[item.FromAccountId!.Value].CorrespondentId &&
                        existing.CreatedAt >= outgoingPeriodStarts.GetValueOrDefault(existing.CorrespondentId, DateTime.MinValue) &&
                        existing.Number == (item.GeneratedSendHawalaNumber ?? item.Number))))
                    throw new InvalidOperationException("شماره یکی از حواله‌های ارسالی قبلاً برای نمایندگی مقصد ثبت شده است.");

                var pendingAccount = await GetOrCreatePendingHawalaAccountAsync();
                var commissionExpenseAccount = items.Any(x => x.GeneratedSendAgentCommissionAmount is > 0)
                    ? await GetOrCreatePayoutAgentCommissionExpenseAccountAsync()
                    : null;
                var receivedHawalas = new List<Hawala>(items.Count);
                var generatedHawalas = new List<Hawala>(outgoingItems.Count);
                var ledgerEntries = new List<LedgerEntry>(items.Count * 5);

                foreach (var dto in items)
                {
                    ValidateCreateHawala(dto);
                    var received = _mapper.Map<Hawala>(dto);
                    received.CreatedAt = now;
                    received.CreatedBy = currentUserId;
                    received.Status = dto.Status ?? "Pending";
                    if (received.Status == "Paid")
                    {
                        received.PaidAt = now;
                        received.PaidBy = currentUserId;
                    }
                    receivedHawalas.Add(received);

                    var sourceAccount = sourceAccounts[dto.CorrespondentId!.Value];
                    var payableAmount = received.ToAmount ?? received.FromAmount;
                    ledgerEntries.Add(NewLedger(received, sourceAccount.Id, received.FromCurrencyId, 0, received.FromAmount,
                        $"حواله دریافتی {received.Number}: ثبت حواله در انتظار - بدهکار شدن نماینده فرستنده", now));
                    ledgerEntries.Add(NewLedger(received, pendingAccount.Id, received.ToCurrencyId, payableAmount, 0,
                        $"حواله دریافتی {received.Number}: ثبت در حساب حواله‌های اجرا نشده", now));

                    if (received.Status != "Paid" || !dto.FromAccountId.HasValue)
                        continue;

                    var destinationAccount = accountsById[dto.FromAccountId.Value];
                    received.PaidFromAccountId = destinationAccount.Id;
                    var hasCommission = dto.GeneratedSendAgentCommissionAmount is > 0;
                    var generated = new Hawala
                    {
                        Number = dto.GeneratedSendHawalaNumber ?? dto.Number,
                        HawalaType = "HawalaSend",
                        Status = hasCommission ? "Paid" : "Pending",
                        CorrespondentId = destinationAccount.CorrespondentId,
                        SourceHawala = received,
                        IsSystemGenerated = true,
                        PaidFromAccountId = destinationAccount.Id,
                        PaymentLocationId = received.PaymentLocationId,
                        SenderName = received.SenderName,
                        ReceiverName = received.ReceiverName,
                        FromCurrencyId = received.FromCurrencyId,
                        FromAmount = received.FromAmount,
                        ToCurrencyId = received.ToCurrencyId,
                        ToAmount = received.ToAmount,
                        ExchangeRate = received.ExchangeRate,
                        AgentCommissionAmount = hasCommission ? dto.GeneratedSendAgentCommissionAmount : null,
                        AgentCommissionCurrencyId = hasCommission ? dto.GeneratedSendAgentCommissionCurrencyId : null,
                        ReferenceNumber = string.IsNullOrWhiteSpace(dto.GeneratedSendReferenceNumber)
                            ? $"AUTO-RCV-{received.Number}"
                            : dto.GeneratedSendReferenceNumber.Trim(),
                        Notes = received.Notes,
                        CreatedAt = now,
                        CreatedBy = currentUserId,
                        PaidAt = hasCommission ? now : null,
                        PaidBy = hasCommission ? currentUserId : null
                    };
                    generatedHawalas.Add(generated);
                    ledgerEntries.Add(NewLedger(generated, pendingAccount.Id, received.ToCurrencyId, 0, payableAmount,
                        $"حواله دریافتی {received.Number}: خروج از حساب حواله‌های اجرا نشده", now));
                    ledgerEntries.Add(NewLedger(generated, destinationAccount.Id, received.ToCurrencyId, payableAmount, 0,
                        $"حواله دریافتی {received.Number}: پرداخت حواله از حساب انتخاب‌شده", now));
                    if (hasCommission)
                    {
                        var commissionCurrencyId = dto.GeneratedSendAgentCommissionCurrencyId!.Value;
                        ledgerEntries.Add(NewLedger(generated, commissionExpenseAccount!.Id, commissionCurrencyId, 0,
                            dto.GeneratedSendAgentCommissionAmount!.Value,
                            $"حواله دریافتی {received.Number}: هزینه کمیشن عامل پرداخت", now));
                        ledgerEntries.Add(NewLedger(generated, destinationAccount.Id, commissionCurrencyId,
                            dto.GeneratedSendAgentCommissionAmount.Value, 0,
                            $"حواله دریافتی {received.Number}: کمیشن قابل پرداخت به عامل پرداخت", now));
                    }
                }

                await BulkCopyHawalasAsync(receivedHawalas);
                var insertedReceived = await _context.Hawalas.AsNoTracking()
                    .Where(x => x.HawalaType == "HawalaReceive" && x.CreatedAt == now && x.CreatedBy == currentUserId)
                    .Select(x => new { x.Id, x.CorrespondentId, x.Number })
                    .ToListAsync();
                var receivedIdByKey = insertedReceived.ToDictionary(x => (x.CorrespondentId, x.Number), x => x.Id);
                foreach (var received in receivedHawalas)
                {
                    if (!receivedIdByKey.TryGetValue((received.CorrespondentId, received.Number), out var id))
                        throw new InvalidOperationException("شناسه یکی از حواله‌های دریافتی پس از ثبت گروهی پیدا نشد.");
                    received.Id = id;
                }

                foreach (var generated in generatedHawalas)
                {
                    generated.SourceHawalaId = generated.SourceHawala!.Id;
                    generated.SourceHawala = null;
                }
                if (generatedHawalas.Count > 0)
                {
                    await BulkCopyHawalasAsync(generatedHawalas);
                    var insertedGenerated = await _context.Hawalas.AsNoTracking()
                        .Where(x => x.IsSystemGenerated && x.CreatedAt == now && x.CreatedBy == currentUserId &&
                                    x.SourceHawalaId.HasValue)
                        .Select(x => new { x.Id, SourceId = x.SourceHawalaId!.Value })
                        .ToListAsync();
                    var generatedIdBySource = insertedGenerated.ToDictionary(x => x.SourceId, x => x.Id);
                    foreach (var generated in generatedHawalas)
                    {
                        if (!generatedIdBySource.TryGetValue(generated.SourceHawalaId!.Value, out var id))
                            throw new InvalidOperationException("شناسه یکی از حواله‌های ارسالی پس از ثبت گروهی پیدا نشد.");
                        generated.Id = id;
                    }
                }

                foreach (var ledger in ledgerEntries)
                {
                    ledger.HawalaId = ledger.Hawala!.Id;
                    ledger.Hawala = null;
                }
                await BulkCopyLedgerEntriesAsync(ledgerEntries);
                return receivedHawalas.Select(x => _mapper.Map<HawalaDto>(x)).ToList();
            }

            private async Task BulkCopyHawalasAsync(IReadOnlyCollection<Hawala> hawalas)
            {
                if (hawalas.Count == 0) return;
                var table = new DataTable();
                AddColumns(table,
                    ("TenantId", typeof(long)), ("Number", typeof(long)), ("HawalaType", typeof(string)),
                    ("CorrespondentId", typeof(long)), ("PaymentLocationId", typeof(long)),
                    ("SenderName", typeof(string)), ("SenderFatherName", typeof(string)),
                    ("SenderTazkiraImagePath", typeof(string)), ("SenderPhone", typeof(string)),
                    ("SenderTazkiraNumber", typeof(string)), ("ReceiverName", typeof(string)),
                    ("ReceiverFatherName", typeof(string)), ("ReceiverTazkiraImagePath", typeof(string)),
                    ("ReceiverPhone", typeof(string)), ("ReceiverTazkiraNumber", typeof(string)),
                    ("FromCurrencyId", typeof(long)), ("FromAmount", typeof(decimal)),
                    ("ToCurrencyId", typeof(long)), ("ToAmount", typeof(decimal)),
                    ("ExchangeRate", typeof(decimal)), ("CommissionAmount", typeof(decimal)),
                    ("CommissionCurrencyId", typeof(long)), ("AgentCommissionAmount", typeof(decimal)),
                    ("AgentCommissionCurrencyId", typeof(long)), ("ReferenceNumber", typeof(string)),
                    ("SenderAddress", typeof(string)), ("ReceiverAddress", typeof(string)),
                    ("Notes", typeof(string)), ("Status", typeof(string)), ("CreatedAt", typeof(DateTime)),
                    ("CreatedBy", typeof(long)), ("PaidAt", typeof(DateTime)), ("PaidBy", typeof(long)),
                    ("PaidFromAccountId", typeof(long)), ("SourceHawalaId", typeof(long)),
                    ("IsSystemGenerated", typeof(bool)), ("CancelledAt", typeof(DateTime)),
                    ("CancelledBy", typeof(long)), ("CancelReason", typeof(string)),
                    ("ReversedTransactionId", typeof(long)));

                foreach (var h in hawalas)
                    table.Rows.Add(_context.CurrentTenantId, h.Number, h.HawalaType, Db(h.CorrespondentId),
                        Db(h.PaymentLocationId), Db(h.SenderName), Db(h.SenderFatherName), Db(h.SenderTazkiraImagePath),
                        Db(h.SenderPhone), Db(h.SenderTazkiraNumber), Db(h.ReceiverName), Db(h.ReceiverFatherName),
                        Db(h.ReceiverTazkiraImagePath), Db(h.ReceiverPhone), Db(h.ReceiverTazkiraNumber),
                        h.FromCurrencyId, h.FromAmount, h.ToCurrencyId, Db(h.ToAmount), Db(h.ExchangeRate),
                        Db(h.CommissionAmount), Db(h.CommissionCurrencyId), Db(h.AgentCommissionAmount),
                        Db(h.AgentCommissionCurrencyId), Db(h.ReferenceNumber), Db(h.SenderAddress),
                        Db(h.ReceiverAddress), Db(h.Notes), h.Status, h.CreatedAt, h.CreatedBy,
                        Db(h.PaidAt), Db(h.PaidBy), Db(h.PaidFromAccountId), Db(h.SourceHawalaId),
                        h.IsSystemGenerated, Db(h.CancelledAt), Db(h.CancelledBy), Db(h.CancelReason),
                        Db(h.ReversedTransactionId));
                await WriteBulkAsync("[dbo].[Hawalas]", table);
            }

            private async Task BulkCopyLedgerEntriesAsync(IReadOnlyCollection<LedgerEntry> entries)
            {
                if (entries.Count == 0) return;
                var table = new DataTable();
                AddColumns(table,
                    ("TenantId", typeof(long)), ("TransferId", typeof(long)), ("HawalaId", typeof(long)),
                    ("SettlementHawalaItemId", typeof(long)), ("TransactionId", typeof(long)),
                    ("CapitalInvestmentId", typeof(long)), ("ExpenseId", typeof(long)),
                    ("AccountMoneyOperationId", typeof(long)), ("MoneyExchangeOperationId", typeof(long)),
                    ("AccountId", typeof(long)), ("CurrencyId", typeof(long)),
                    ("TalabKar", typeof(decimal)), ("BadehKar", typeof(decimal)),
                    ("Description", typeof(string)), ("CreatedAt", typeof(DateTime)));
                foreach (var e in entries)
                    table.Rows.Add(_context.CurrentTenantId, Db(e.TransferId), Db(e.HawalaId),
                        Db(e.SettlementHawalaItemId), Db(e.TransactionId), Db(e.CapitalInvestmentId),
                        Db(e.ExpenseId), Db(e.AccountMoneyOperationId), Db(e.MoneyExchangeOperationId),
                        e.AccountId, e.CurrencyId, e.TalabKar, e.BadehKar, Db(e.Description), e.CreatedAt);
                await WriteBulkAsync("[dbo].[LedgerEntries]", table);
            }

            private async Task WriteBulkAsync(string destinationTable, DataTable table)
            {
                var connection = (SqlConnection)_context.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();
                var dbTransaction = _context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction
                    ?? throw new InvalidOperationException("ثبت گروهی باید داخل تراکنش SQL انجام شود.");
                using var bulk = new SqlBulkCopy(connection,
                    SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers,
                    dbTransaction)
                {
                    DestinationTableName = destinationTable,
                    BatchSize = BulkCopyBatchSize,
                    BulkCopyTimeout = 120,
                    EnableStreaming = true
                };
                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(table);
            }

            private static void AddColumns(DataTable table, params (string Name, Type Type)[] columns)
            {
                foreach (var column in columns)
                    table.Columns.Add(column.Name, column.Type);
            }

            private static object Db(object? value) => value ?? DBNull.Value;

            private static LedgerEntry NewLedger(
                Hawala hawala,
                long accountId,
                long currencyId,
                decimal talabKar,
                decimal badehKar,
                string description,
                DateTime createdAt) => new()
            {
                Hawala = hawala,
                AccountId = accountId,
                CurrencyId = currencyId,
                TalabKar = talabKar,
                BadehKar = badehKar,
                Description = description,
                CreatedAt = createdAt
            };

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
                    .Include(h => h.Correspondent).ThenInclude(c => c!.SettlementCurrency)
                    .Include(h => h.SettlementConversionLinks).ThenInclude(x => x.Conversion)
                    .Include(h => h.SettlementConversionItems).ThenInclude(x => x.SourceCurrency)
                    .Include(h => h.SettlementConversionItems).ThenInclude(x => x.Conversion).ThenInclude(x => x.TargetCurrency)
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
                    .Where(a => a.CorrespondentId == filter.CorrespondentId)
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

                    if (payment.AgentCommissionAmount < 0)
                        throw new InvalidOperationException("کمیشن عامل پرداخت نمی‌تواند منفی باشد.");
                    if (payment.AgentCommissionAmount > 0)
                    {
                        if (!payment.AgentCommissionCurrencyId.HasValue || payment.AgentCommissionCurrencyId <= 0)
                            throw new InvalidOperationException("برای کمیشن عامل پرداخت، انتخاب ارز الزامی است.");

                        var commissionCurrencyIsActive = await _context.Currencies
                            .AnyAsync(x => x.Id == payment.AgentCommissionCurrencyId && x.IsActive);
                        if (!commissionCurrencyIsActive)
                            throw new InvalidOperationException("ارز کمیشن عامل پرداخت معتبر یا فعال نیست.");
                    }

                    hawala.ReceiverName = payment.ReceiverName.Trim();
                    hawala.ReceiverFatherName = payment.ReceiverFatherName?.Trim();
                    hawala.ReceiverPhone = payment.ReceiverPhone?.Trim();
                    hawala.ReceiverTazkiraNumber = payment.ReceiverTazkiraNumber?.Trim();
                    hawala.ReceiverTazkiraImagePath = payment.ReceiverTazkiraImagePath;
                    hawala.ReceiverAddress = payment.ReceiverAddress?.Trim();
                    hawala.AgentCommissionAmount = payment.AgentCommissionAmount > 0
                        ? payment.AgentCommissionAmount
                        : null;
                    hawala.AgentCommissionCurrencyId = payment.AgentCommissionAmount > 0
                        ? payment.AgentCommissionCurrencyId
                        : null;

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

                    await EnsureNotSettlementConvertedAsync(generatedHawala == null
                        ? [hawala.Id]
                        : [hawala.Id, generatedHawala.Id]);

                    var affectedHawalaIds = new List<long> { hawala.Id };
                    if (generatedHawala != null)
                        affectedHawalaIds.Add(generatedHawala.Id);

                    var periodicCommissionItems = await _context.CorrespondentCommissionBatchItems
                        .Where(x => affectedHawalaIds.Contains(x.HawalaId) && x.IsActive &&
                                    x.Batch.Status == "Posted")
                        .ToListAsync();
                    foreach (var commissionItem in periodicCommissionItems)
                        commissionItem.IsActive = false;

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
                long? generatedSendHawalaNumber = null,
                decimal? generatedSendAgentCommissionAmount = null,
                long? generatedSendAgentCommissionCurrencyId = null,
                string? generatedSendReferenceNumber = null)
            {
                if (hawala.HawalaType == "HawalaSend")
                    await ProcessHawalaSendLedgerAsync(hawala, fromAccountId);
                else if (hawala.HawalaType == "HawalaReceive")
                    await ProcessHawalaReceiveLedgerAsync(
                        hawala,
                        fromAccountId,
                        generatedSendHawalaNumber,
                        generatedSendAgentCommissionAmount,
                        generatedSendAgentCommissionCurrencyId,
                        generatedSendReferenceNumber);
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
                    .FirstOrDefaultAsync(a => a.CorrespondentId == hawala.CorrespondentId);
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
                long? generatedSendHawalaNumber = null,
                decimal? generatedSendAgentCommissionAmount = null,
                long? generatedSendAgentCommissionCurrencyId = null,
                string? generatedSendReferenceNumber = null)
            {
                await ProcessHawalaReceivePendingLedgerAsync(hawala);

                if (hawala.Status == "Paid")
                {
                    await ProcessHawalaReceivePaymentAsync(
                        hawala,
                        fromAccountId,
                        generatedSendHawalaNumber,
                        generatedSendAgentCommissionAmount,
                        generatedSendAgentCommissionCurrencyId,
                        generatedSendReferenceNumber);
                }
            }
            private async Task ProcessHawalaReceivePendingLedgerAsync(Hawala hawala)
            {
                if (!hawala.CorrespondentId.HasValue)
                    throw new InvalidOperationException("برای حواله دریافتی، انتخاب نماینده فرستنده الزامی است.");

                var correspondentAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a =>
                        a.CorrespondentId == hawala.CorrespondentId);

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
                long? generatedSendHawalaNumber = null,
                decimal? generatedSendAgentCommissionAmount = null,
                long? generatedSendAgentCommissionCurrencyId = null,
                string? generatedSendReferenceNumber = null)
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
                var paymentCommissionAmount = hawala.AgentCommissionAmount;
                var paymentCommissionCurrencyId = hawala.AgentCommissionCurrencyId;
                if (string.Equals(paidFromAccount.AccountType, "Correspondent", StringComparison.OrdinalIgnoreCase))
                {
                    var outgoingCommissionAmount = generatedSendAgentCommissionAmount ?? hawala.AgentCommissionAmount;
                    var outgoingCommissionCurrencyId = generatedSendAgentCommissionCurrencyId ?? hawala.AgentCommissionCurrencyId;
                    var generatedSendHawala = await CreateGeneratedSendHawalaAsync(
                        hawala,
                        paidFromAccount,
                        generatedSendHawalaNumber,
                        outgoingCommissionAmount,
                        outgoingCommissionCurrencyId,
                        generatedSendReferenceNumber);
                    ledgerHawalaId = generatedSendHawala.Id;
                    paymentCommissionAmount = generatedSendHawala.AgentCommissionAmount;
                    paymentCommissionCurrencyId = generatedSendHawala.AgentCommissionCurrencyId;
                }

                await ProcessHawalaReceivePaymentLedgerAsync(
                    hawala,
                    paidFromAccount,
                    ledgerHawalaId,
                    paymentCommissionAmount,
                    paymentCommissionCurrencyId);
            }

            private async Task<Hawala> CreateGeneratedSendHawalaAsync(
                Hawala receivedHawala,
                Account paidFromAccount,
                long? requestedNumber = null,
                decimal? agentCommissionAmount = null,
                long? agentCommissionCurrencyId = null,
                string? referenceNumber = null)
            {
                if (!paidFromAccount.CorrespondentId.HasValue)
                {
                    throw new InvalidOperationException("حساب پرداخت‌کننده به نمایندگی معتبری مرتبط نیست.");
                }

                var existing = await _context.Hawalas
                    .FirstOrDefaultAsync(x => x.SourceHawalaId == receivedHawala.Id);
                if (existing != null)
                    return existing;

                var destinationCorrespondentId = paidFromAccount.CorrespondentId.Value;
                var currentPeriodStart = await GetCurrentPeriodStartAsync(destinationCorrespondentId);
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
                                    x.HawalaType == "HawalaSend" && x.CreatedAt >= currentPeriodStart)
                        .OrderByDescending(x => x.Number)
                        .Select(x => (long?)x.Number)
                        .FirstOrDefaultAsync();

                    generatedNumber = (lastNumber ?? 0) + 1;
                }

                var numberExists = await _context.Hawalas.AnyAsync(x =>
                    x.CorrespondentId == destinationCorrespondentId &&
                    x.HawalaType == "HawalaSend" &&
                    x.CreatedAt >= currentPeriodStart &&
                    x.Number == generatedNumber);
                if (numberExists)
                {
                    throw new InvalidOperationException(
                        $"نمبر {generatedNumber} برای حواله ارسالی این نمایندگی قبلاً ثبت شده است.");
                }

                var now = DateTime.UtcNow;
                var hasCommission = agentCommissionAmount is > 0;
                if (hasCommission && (!agentCommissionCurrencyId.HasValue ||
                    !await _context.Currencies.AnyAsync(x => x.Id == agentCommissionCurrencyId.Value && x.IsActive)))
                    throw new InvalidOperationException("ارز کمیشن نمایندگی معتبر یا فعال نیست.");
                var generatedHawala = new Hawala
                {
                    Number = generatedNumber,
                    HawalaType = "HawalaSend",
                    Status = hasCommission ? "Paid" : "Pending",
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
                    CommissionAmount = null,
                    CommissionCurrencyId = null,
                    AgentCommissionAmount = hasCommission ? agentCommissionAmount : null,
                    AgentCommissionCurrencyId = hasCommission ? agentCommissionCurrencyId : null,
                    ReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber)
                        ? $"AUTO-RCV-{receivedHawala.Id}"
                        : referenceNumber.Trim(),
                    Notes = receivedHawala.Notes,
                    CreatedAt = now,
                    CreatedBy = GetCurrentUserId(),
                    PaidAt = hasCommission ? now : null,
                    PaidBy = hasCommission ? GetCurrentUserId() : null
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
                long ledgerHawalaId,
                decimal? agentCommissionAmount = null,
                long? agentCommissionCurrencyId = null)
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
                if (agentCommissionAmount > 0)
                {
                    var expenseAccount = await GetOrCreatePayoutAgentCommissionExpenseAccountAsync();
                    var effectiveCommissionCurrencyId =
                        agentCommissionCurrencyId ??
                        hawala.ToCurrencyId;

                    await CreateLedgerEntry(
                        ledgerHawalaId,
                        expenseAccount.Id,
                        effectiveCommissionCurrencyId,
                        talabKar: 0,
                        badehKar: agentCommissionAmount.Value,
                        description: $"حواله دریافتی {hawala.Id}: هزینه کمیشن عامل پرداخت");

                    await CreateLedgerEntry(
                        ledgerHawalaId,
                        paidFromAccount.Id,
                        effectiveCommissionCurrencyId,
                        talabKar: agentCommissionAmount.Value,
                        badehKar: 0,
                        description: $"حواله دریافتی {hawala.Id}: کمیشن قابل پرداخت به عامل پرداخت");
                }
            }

            private async Task<Account> GetOrCreatePayoutAgentCommissionExpenseAccountAsync()
            {
                const string accountCode = "4002";
                var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
                if (account != null)
                {
                    if (!string.Equals(account.AccountType, "Expense", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("حساب 4002 باید از نوع Expense باشد تا کمیشن عامل پرداخت ثبت شود.");
                    if (account.IsArchived)
                        throw new InvalidOperationException("حساب هزینه کمیشن عامل پرداخت آرشیف شده است.");
                    return account;
                }

                var newAccount = new Account
                {
                    AccountCode = accountCode,
                    AccountName = "کمیشن عامل پرداخت",
                    AccountType = "Expense",
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.Accounts.AddAsync(newAccount);
                await _context.SaveChangesAsync();
                return newAccount;
            }
            private async Task ProcessHawalaOtherLedgerAsync(Hawala hawala)
            {
                var defaultAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountType == "Cash" &&
                                              a.CustomerId == null && a.CorrespondentId == null);
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
                if (dto.HawalaType == "HawalaReceive" &&
                    dto.Status != "Paid" &&
                    dto.AgentCommissionAmount > 0)
                {
                    throw new InvalidOperationException("کمیشن عامل پرداخت برای حواله دریافتی باید هنگام اجرای حواله ثبت شود.");
                }
                if (dto.AgentCommissionAmount > 0 &&
                    (!dto.AgentCommissionCurrencyId.HasValue ||
                     dto.AgentCommissionCurrencyId.Value <= 0))
                {
                    throw new InvalidOperationException("برای کارمزد نمایندگی، انتخاب ارز الزامی است.");
                }
                if (dto.GeneratedSendAgentCommissionAmount < 0)
                    throw new InvalidOperationException("کمیشن حواله ارسالی نمی‌تواند منفی باشد.");
                if (dto.GeneratedSendAgentCommissionAmount > 0 &&
                    (!dto.GeneratedSendAgentCommissionCurrencyId.HasValue || dto.GeneratedSendAgentCommissionCurrencyId <= 0))
                    throw new InvalidOperationException("برای کمیشن حواله ارسالی، انتخاب ارز الزامی است.");

                if (dto.HawalaType == "HawalaReceive" &&
                    dto.Status == "Paid" &&
                    dto.FromAccountId == null)
                {
                    throw new InvalidOperationException("برای پرداخت حواله دریافتی، انتخاب حساب پرداخت‌کننده الزامی است.");
                }
            }

            private long GetCurrentUserId() => _context.RequireCurrentUserId();
            private async Task<DateTime> GetCurrentPeriodStartAsync(long? correspondentId)
            {
                if (!correspondentId.HasValue) return DateTime.MinValue;
                return await _context.CorrespondentAccountPeriods
                    .Where(x => x.CorrespondentId == correspondentId.Value)
                    .MaxAsync(x => (DateTime?)x.PeriodTo) ?? DateTime.MinValue;
            }

            private async Task EnforceCorrespondentCommissionMethodAsync(
                string hawalaType, long? correspondentId, decimal? commissionAmount)
            {
                if (hawalaType != "HawalaReceive" || !correspondentId.HasValue || commissionAmount is not > 0)
                    return;

                var method = await _context.Correspondents
                    .Where(x => x.Id == correspondentId.Value)
                    .Select(x => x.CommissionMethod)
                    .SingleOrDefaultAsync();
                if (method == "PeriodicPerLakh")
                    throw new InvalidOperationException("برای این نمایندگی کمیشن به‌صورت دوره‌ای محاسبه می‌شود و در هر حواله قابل ثبت نیست.");
            }
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
