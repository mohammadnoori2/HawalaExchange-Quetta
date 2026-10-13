using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HawalaExchange.Infrastructure.Services;

public sealed class HawalaImportService : IHawalaImportService, IHawalaImportJobExecutor
{
    private const int MaximumRows = 10_000;
    private const long MaximumFileSize = 10 * 1024 * 1024;
    private static readonly TimeSpan MaximumBatchDeleteDuration = TimeSpan.FromSeconds(30);
    private readonly ApplicationDbContext _context;
    private readonly IHawalaService _hawalaService;
    private readonly IPaymentLocationService _paymentLocationService;
    private readonly ICorrespondentService _correspondentService;
    private readonly IAuditLogService _auditLogService;
    private readonly IDbContextFactory<ApplicationDbContext>? _contextFactory;

    public HawalaImportService(
        ApplicationDbContext context,
        IHawalaService hawalaService,
        IPaymentLocationService paymentLocationService,
        ICorrespondentService correspondentService,
        IAuditLogService auditLogService,
        IDbContextFactory<ApplicationDbContext>? contextFactory = null)
    {
        _context = context;
        _hawalaService = hawalaService;
        _paymentLocationService = paymentLocationService;
        _correspondentService = correspondentService;
        _auditLogService = auditLogService;
        _contextFactory = contextFactory;
    }

    public async Task<HawalaImportPreviewDto> PreviewAsync(
        Stream file,
        string fileName,
        long correspondentId,
        IProgress<HawalaImportProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Report(progress, 5, "بررسی فایل و نمایندگی...");
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("تنها فایل Excel با پسوند .xlsx قابل قبول است.");

        var correspondent = await _context.Correspondents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == correspondentId && !x.IsArchived, cancellationToken)
            ?? throw new InvalidOperationException("نمایندگی انتخاب‌شده یافت نشد یا غیرفعال است.");
        var ownLocation = await _context.CompanySettings
            .AsNoTracking()
            .Where(x => x.OwnPaymentLocationId.HasValue)
            .Select(x => new { Id = x.OwnPaymentLocationId!.Value, Name = x.OwnPaymentLocation!.Name })
            .FirstOrDefaultAsync(cancellationToken);

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        Report(progress, 20, "فایل دریافت شد؛ در حال خواندن ردیف‌ها...");
        if (memory.Length == 0)
            throw new InvalidOperationException("فایل انتخاب‌شده خالی است.");
        if (memory.Length > MaximumFileSize)
            throw new InvalidOperationException("حجم فایل نباید بیشتر از 10 مگابایت باشد.");

        var fileHash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
        var alreadyImported = await _context.HawalaImportBatches
            .AsNoTracking()
            .AnyAsync(x => x.FileHash == fileHash && x.Status == "Posted", cancellationToken);
        if (alreadyImported)
            throw new InvalidOperationException("این فایل قبلاً به‌طور کامل ثبت شده است.");

        memory.Position = 0;
        var rows = await ParseRowsAsync(memory, correspondentId, ownLocation?.Id, progress, cancellationToken);
        if (rows.Count == 0)
            throw new InvalidOperationException("هیچ ردیف قابل خواندن در فایل پیدا نشد.");
        if (rows.Count > MaximumRows)
            throw new InvalidOperationException($"حداکثر {MaximumRows:N0} ردیف در هر فایل قابل واردکردن است.");

        var batch = new HawalaImportBatch
        {
            CorrespondentId = correspondentId,
            OwnPaymentLocationId = ownLocation?.Id,
            FileName = Path.GetFileName(fileName),
            FileHash = fileHash,
            Status = "Preview",
            RowCount = rows.Count,
            CreatedBy = _context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow
        };
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.HawalaImportBatches.Add(batch);
            await _context.SaveChangesAsync(cancellationToken);
            foreach (var row in rows)
                row.BatchId = batch.Id;

            Report(progress, 76, "انتقال سریع ردیف‌ها به جدول آماده‌سازی...");
            await WriteStagingRowsAsync(rows, progress, cancellationToken);
            Report(progress, 88, "اعتبارسنجی مجموعه‌ای ردیف‌ها در دیتابیس...");
            await ValidateStagingRowsAsync(batch.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }

        _context.ChangeTracker.Clear();
        batch.Rows = await _context.HawalaImportRows
            .AsNoTracking()
            .Where(x => x.BatchId == batch.Id)
            .Include(x => x.PaymentLocation)
            .Include(x => x.DestinationCorrespondent)
            .OrderBy(x => x.ExcelRowNumber)
            .ToListAsync(cancellationToken);

        Report(progress, 100, "پیش‌نمایش آماده شد.");
        var locationIds = batch.Rows.Where(x => x.PaymentLocationId.HasValue)
            .Select(x => x.PaymentLocationId!.Value).Distinct().ToList();
        var currentAssignments = await _context.PaymentLocationCorrespondentAssignments.AsNoTracking()
            .Where(x => locationIds.Contains(x.PaymentLocationId) && x.EffectiveFrom <= DateTime.Today &&
                        (x.EffectiveTo == null || DateTime.Today < x.EffectiveTo))
            .Include(x => x.Correspondent)
            .ToDictionaryAsync(x => x.PaymentLocationId, cancellationToken);
        return BuildPreview(batch, correspondent.Name, ownLocation?.Id, ownLocation?.Name ?? string.Empty, currentAssignments);
    }

    public async Task<HawalaImportPreviewDto?> GetPreviewAsync(
        long batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _context.HawalaImportBatches.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Correspondent)
            .Include(x => x.Rows).ThenInclude(x => x.PaymentLocation)
            .Include(x => x.Rows).ThenInclude(x => x.DestinationCorrespondent)
            .SingleOrDefaultAsync(x => x.Id == batchId && x.Status == "Preview" &&
                (x.Job == null || x.Job.Status == "Failed"), cancellationToken);
        if (batch is null) return null;
        var ownLocationName = batch.OwnPaymentLocationId.HasValue
            ? await _context.PaymentLocations.AsNoTracking()
                .Where(x => x.Id == batch.OwnPaymentLocationId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty
            : string.Empty;
        var locationIds = batch.Rows.Where(x => x.PaymentLocationId.HasValue)
            .Select(x => x.PaymentLocationId!.Value).Distinct().ToList();
        var currentAssignments = await _context.PaymentLocationCorrespondentAssignments.AsNoTracking()
            .Where(x => locationIds.Contains(x.PaymentLocationId) && x.EffectiveFrom <= DateTime.Today &&
                        (x.EffectiveTo == null || DateTime.Today < x.EffectiveTo))
            .Include(x => x.Correspondent)
            .ToDictionaryAsync(x => x.PaymentLocationId, cancellationToken);
        return BuildPreview(batch, batch.Correspondent?.Name ?? string.Empty,
            batch.OwnPaymentLocationId, ownLocationName, currentAssignments);
    }

    public async Task<IReadOnlyList<HawalaImportHistoryDto>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var readContext = _contextFactory is null ? null : await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await (readContext ?? _context).HawalaImportBatches.AsNoTracking()
            .Where(x => x.Status != "Deleted")
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new HawalaImportHistoryDto
            {
                BatchId = x.Id,
                FileName = x.FileName,
                SourceCorrespondentName = x.Correspondent!.Name,
                SourceCorrespondentId = x.CorrespondentId,
                Status = x.Status == "Posted" ? "Posted" : x.Job != null ? x.Job.Status : x.Status,
                ProgressPercent = x.Job != null ? x.Job.ProgressPercent : 0,
                ProgressMessage = x.Job != null ? x.Job.ProgressMessage : null,
                ErrorMessage = x.Job != null ? x.Job.ErrorMessage : null,
                RowCount = x.RowCount,
                CreatedAt = x.CreatedAt,
                ConfirmedAt = x.ConfirmedAt,
                UploadedBy = x.ConfirmedByUser != null
                    ? x.ConfirmedByUser.FullName
                    : x.CreatedByUser!.FullName
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<HawalaImportDetailsDto?> GetDetailsAsync(
        long batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _context.HawalaImportBatches.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Correspondent)
            .Include(x => x.CreatedByUser)
            .Include(x => x.ConfirmedByUser)
            .Include(x => x.Rows).ThenInclude(x => x.PaymentLocation)
            .Include(x => x.Rows).ThenInclude(x => x.DestinationCorrespondent)
            .Include(x => x.Rows).ThenInclude(x => x.Hawala)
            .Include(x => x.Rows).ThenInclude(x => x.GeneratedSendHawala)
            .SingleOrDefaultAsync(x => x.Id == batchId && x.Status != "Deleted", cancellationToken);
        if (batch is null) return null;

        var job = await _context.HawalaImportJobs.AsNoTracking().Where(x => x.BatchId == batchId)
            .Select(x => new { x.Status, x.ProgressPercent, x.ProgressMessage, x.ErrorMessage })
            .SingleOrDefaultAsync(cancellationToken);
        var rows = batch.Rows.OrderBy(x => x.ExcelRowNumber).ToList();
        var groups = rows.GroupBy(row =>
        {
            var isOwn = row.PaymentLocationId == batch.OwnPaymentLocationId;
            return new
            {
                IsOwn = isOwn,
                CorrespondentId = isOwn ? null : row.DestinationCorrespondentId,
                Name = isOwn
                    ? "دفتر خود ما"
                    : row.DestinationCorrespondent?.Name ?? "نمایندگی تعیین نشده"
            };
        }).Select(group => new HawalaImportGroupDetailsDto
        {
            CorrespondentId = group.Key.CorrespondentId,
            CorrespondentName = group.Key.Name,
            IsOwnOffice = group.Key.IsOwn,
            HawalaCount = group.Count(),
            Totals = BuildTotals(group),
            Hawalas = group.Select(row => new HawalaImportHawalaDetailsDto
            {
                RowId = row.Id,
                ExcelRowNumber = row.ExcelRowNumber,
                HawalaId = row.HawalaId,
                GeneratedSendHawalaId = row.GeneratedSendHawalaId,
                GeneratedSendHawalaNumber = row.GeneratedSendHawala?.Number,
                HawalaNumber = row.HawalaNumber,
                ReferenceNumber = row.ReferenceNumber,
                SenderName = row.SenderName,
                ReceiverName = row.ReceiverName,
                PaymentLocationName = row.PaymentLocation?.Name ?? row.PaymentLocationText ?? string.Empty,
                PaymentLocationId = row.PaymentLocationId,
                Amount = row.Amount ?? 0,
                CurrencyCode = row.CurrencyCode ?? string.Empty,
                // Forwarding is a display status; the source remains Paid for accounting.
                Status = row.Hawala is { HawalaType: "HawalaReceive", Status: "Paid" } source &&
                    row.GeneratedSendHawala is { IsSystemGenerated: true, HawalaType: "HawalaSend", Status: "Paid" or "Pending" } sent &&
                    sent.SourceHawalaId == source.Id
                    ? "Sent"
                    : row.Hawala?.Status ?? (batch.Status == "Posted" ? "ثبت‌شده" : "پیش‌نمایش")
            }).ToList()
        }).OrderByDescending(x => x.IsOwnOffice).ThenBy(x => x.CorrespondentName).ToList();

        return new HawalaImportDetailsDto
        {
            BatchId = batch.Id,
            FileName = batch.FileName,
            SourceCorrespondentName = batch.Correspondent?.Name ?? string.Empty,
            SourceCorrespondentId = batch.CorrespondentId,
            Status = batch.Status == "Posted" ? "Posted" : job?.Status ?? batch.Status,
            ProgressPercent = job?.ProgressPercent ?? 0,
            ProgressMessage = job?.ProgressMessage,
            ErrorMessage = job?.ErrorMessage,
            RowCount = batch.RowCount,
            CreatedAt = batch.CreatedAt,
            ConfirmedAt = batch.ConfirmedAt,
            UploadedBy = batch.ConfirmedByUser?.FullName ?? batch.CreatedByUser?.FullName ?? string.Empty,
            ImportedCount = rows.Count(x => x.HawalaId.HasValue),
            GeneratedSendCount = rows.Count(x => x.GeneratedSendHawalaId.HasValue),
            OwnOfficeCount = rows.Count(x => x.PaymentLocationId == batch.OwnPaymentLocationId),
            ReceivedTotals = BuildTotals(rows),
            SentTotals = BuildTotals(rows.Where(x => x.GeneratedSendHawalaId.HasValue)),
            Groups = groups
        };
    }

    public async Task<HawalaImportDeletionPreviewDto> GetDeletionPreviewAsync(long batchId, CancellationToken cancellationToken = default)
    {
        var details = await GetDetailsAsync(batchId, cancellationToken) ?? throw new InvalidOperationException("فایل یافت نشد.");
        var ids = details.Groups.SelectMany(x => x.Hawalas).SelectMany(x => new[] { x.HawalaId, x.GeneratedSendHawalaId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var currencies = await _context.Currencies.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var batchIds = await _context.CorrespondentCommissionBatchItems.AsNoTracking().Where(x => ids.Contains(x.HawalaId) && x.IsActive && x.Batch.Status == "Posted")
            .Select(x => x.BatchId).Distinct().ToArrayAsync(cancellationToken);
        var batches = await _context.CorrespondentCommissionBatches.AsNoTracking().AsSplitQuery().Include(x => x.Correspondent).Include(x => x.Items).ThenInclude(x => x.Hawala)
            .Where(x => batchIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var preview = new HawalaImportDeletionPreviewDto { Totals = details.ReceivedTotals,
            HasClosedPeriod = await _context.CorrespondentAccountPeriodHawalas.AnyAsync(x => ids.Contains(x.HawalaId), cancellationToken),
            HasSettlementConversion = await _context.CorrespondentSettlementConversionHawalas.AnyAsync(x => ids.Contains(x.HawalaId), cancellationToken) };
        foreach (var batch in batches)
        {
            var remaining = ImportCommissionCorrectionService.Totals(batch, batch.Items.Where(x => x.IsActive && !ids.Contains(x.HawalaId)).ToList(), currencies);
            preview.Commissions.Add(new() { CorrespondentId = batch.CorrespondentId, CorrespondentName = batch.Correspondent.Name, Scope = batch.CommissionScope,
                PreviousAfn = batch.TotalCommissionAfn, PreviousUsd = batch.TotalCommissionUsd, RemainingAfn = remaining.Afn, RemainingUsd = remaining.Usd });
        }
        return preview;
    }

    public async Task DeleteBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default)
    {
        if (await _context.HawalaImportJobs.AsNoTracking().AnyAsync(x => x.BatchId == batchId &&
                (x.Status == "Queued" || x.Status == "Running"), cancellationToken))
            throw new InvalidOperationException("فایل در صف یا در حال ثبت است و قابل حذف نیست.");
        if (await _context.HawalaImportBatches.AsNoTracking().AnyAsync(x => x.Id == batchId && (x.Status == "Posted" || x.Status == "Deleted"), cancellationToken))
        {
            await CancelPostedBatchAsync(batchId, cancellationToken);
            return;
        }
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(MaximumBatchDeleteDuration);
        var operationToken = timeoutSource.Token;
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(operationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "[dbo].[usp_DeleteHawalaImportBatch_v1]";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 30;
            command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
            command.Parameters.Add("@BatchId", SqlDbType.BigInt).Value = batchId;
            var fileNameParameter = command.Parameters.Add("@FileName", SqlDbType.NVarChar, 260);
            fileNameParameter.Direction = ParameterDirection.Output;
            var rowCountParameter = command.Parameters.Add("@RowCount", SqlDbType.Int);
            rowCountParameter.Direction = ParameterDirection.Output;
            var deletedCountParameter = command.Parameters.Add("@DeletedHawalaCount", SqlDbType.Int);
            deletedCountParameter.Direction = ParameterDirection.Output;

            await command.ExecuteNonQueryAsync(operationToken);
            _context.ChangeTracker.Clear();

            var fileName = Convert.ToString(fileNameParameter.Value) ?? string.Empty;
            var rowCount = Convert.ToInt32(rowCountParameter.Value, CultureInfo.InvariantCulture);
            var deletedCount = Convert.ToInt32(deletedCountParameter.Value, CultureInfo.InvariantCulture);
            await _auditLogService.LogAsync(
                "DELETE",
                "HawalaImportBatches",
                batchId,
                null,
                $"فایل آپلود گروهی '{fileName}' با {rowCount} ردیف و {deletedCount} حواله وابسته توسط Stored Procedure حذف شد.",
                _context.RequireCurrentUserId());
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<HawalaImportResultDto> ConfirmAsync(
        ConfirmHawalaImportDto request,
        IProgress<HawalaImportProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
        => await ConfirmCoreAsync(request, false, progress, cancellationToken);

    private async Task CancelPostedBatchAsync(long batchId, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(MaximumBatchDeleteDuration);
        var token = budget.Token;
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await _context.EnsureCurrentTenantWriteAccessAsync(token);
            await _context.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sp_getapplock @Resource={"HawalaImportPrepare:" + _context.CurrentTenantId},@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=5000; IF @r<0 THROW 50001,N'عملیات دیگری در جریان است؛ دوباره تلاش کنید.',1;", token);
            var batch = await _context.HawalaImportBatches.FromSqlInterpolated($"SELECT * FROM dbo.HawalaImportBatches WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={_context.CurrentTenantId} AND Id={batchId}")
                .SingleAsync(token);
            if (batch.Status == "Deleted") { await transaction.CommitAsync(token); return; }
            if (batch.Status != "Posted") throw new InvalidOperationException("وضعیت فایل تغییر کرده است؛ تاریخچه را تازه‌سازی کنید.");
            var links = await _context.HawalaImportRows.AsNoTracking().Where(x => x.BatchId == batchId).Select(x => new { x.HawalaId, x.GeneratedSendHawalaId }).ToListAsync(token);
            var ids = links.SelectMany(x => new[] { x.HawalaId, x.GeneratedSendHawalaId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
            if (await _context.CorrespondentSettlementConversionHawalas.AnyAsync(x => ids.Contains(x.HawalaId), token))
                throw new InvalidOperationException("ابتدا تبدیل ارز توافقی مرتبط با حواله‌های فایل را برگشت دهید؛ سپس حذف را انجام دهید.");
            var correction = new ImportCommissionCorrectionService(_context);
            var originals = await correction.ReverseAsync(ids, token);
            await correction.RepostAsync(originals, ids, true, token);
            await correction.ReverseHawalaLedgerAsync(ids, token);
            var user = _context.RequireCurrentUserId();
            var now = DateTime.UtcNow;
            await _context.Hawalas.Where(x => ids.Contains(x.Id) && x.Status != "Cancel").ExecuteUpdateAsync(x => x
                .SetProperty(h => h.Status, "Cancel").SetProperty(h => h.CancelledBy, user).SetProperty(h => h.CancelledAt, now)
                .SetProperty(h => h.CancelReason, "حذف آپلود گروهی از تاریخچه؛ حسابداری برگشت داده شد."), token);
            batch.Status = "Deleted";
            _context.AuditLogs.Add(new AuditLog { UserId = user, Action = "DELETE_IMPORT", TableName = "HawalaImportBatches", RecordId = batchId,
                NewValue = $"حذف عملیاتی فایل {batch.FileName}، ابطال {ids.Length} حواله و اصلاح کمیشن‌های مرتبط؛ سوابق حسابداری حفظ شدند." });
            await _context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        finally { _context.ChangeTracker.Clear(); }
    }

    public async Task<HawalaImportPreviewDto> PrepareLocationsAsync(
        ConfirmHawalaImportDto request,
        IProgress<HawalaImportProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await ConfirmCoreAsync(request, false, progress, cancellationToken, prepareOnly: true);
        return await GetPreviewAsync(request.BatchId, cancellationToken)
            ?? throw new InvalidOperationException("پیش‌نمایش آماده‌شده پیدا نشد.");
    }

    public async Task SaveDraftAsync(ConfirmHawalaImportDto request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await _context.EnsureCurrentTenantWriteAccessAsync(cancellationToken);
        var json = System.Text.Json.JsonSerializer.Serialize(request.Commissions.GroupBy(x => x.RowId).Select(x => x.Last()));
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @Tenant bigint={_context.CurrentTenantId}, @Batch bigint={request.BatchId}, @Json nvarchar(max)={json};
            IF NOT EXISTS (SELECT 1 FROM dbo.HawalaImportBatches WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@Tenant AND Id=@Batch AND Status=N'Preview' AND LocationsPreparedAt IS NOT NULL)
                THROW 50001,N'ابتدا محل‌های پرداخت را آماده کنید؛ فقط پیش‌نویس قابل ذخیره است.',1;
            IF EXISTS (SELECT 1 FROM dbo.HawalaImportJobs WHERE TenantId=@Tenant AND BatchId=@Batch AND Status IN (N'Queued',N'Running'))
                THROW 50002,N'فایل در صف ثبت است و قابل تغییر نیست.',1;
            IF EXISTS (SELECT 1 FROM OPENJSON(@Json) WITH (RowId bigint,Amount decimal(18,4),CurrencyId bigint) v
                LEFT JOIN dbo.HawalaImportRows r ON r.TenantId=@Tenant AND r.BatchId=@Batch AND r.Id=v.RowId
                LEFT JOIN dbo.Currencies c ON c.TenantId=@Tenant AND c.Id=v.CurrencyId AND c.IsActive=1
                WHERE r.Id IS NULL OR v.Amount<=0 OR (v.Amount IS NOT NULL AND c.Id IS NULL))
                THROW 50003,N'ردیف، مبلغ یا ارز کمیشن پیش‌نویس معتبر نیست.',1;
            UPDATE r SET AgentCommissionAmount=v.Amount,AgentCommissionCurrencyId=CASE WHEN v.Amount IS NULL THEN NULL ELSE v.CurrencyId END
                FROM dbo.HawalaImportRows r JOIN OPENJSON(@Json) WITH (RowId bigint,Amount decimal(18,4),CurrencyId bigint) v ON r.Id=v.RowId
                WHERE r.TenantId=@Tenant AND r.BatchId=@Batch;
            """, cancellationToken);
        _context.AuditLogs.Add(new AuditLog { UserId = _context.RequireCurrentUserId(), Action = "SAVE_IMPORT_DRAFT", TableName = "HawalaImportBatches", RecordId = request.BatchId,
            NewValue = "کمیشن‌های پیش‌نویس برای ادامه ثبت در آینده ذخیره شدند؛ حسابداری ثبت نشد." });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    Task<HawalaImportResultDto> IHawalaImportJobExecutor.ConfirmQueuedAsync(
        ConfirmHawalaImportDto request,
        IProgress<HawalaImportProgressDto>? progress,
        CancellationToken cancellationToken)
        => ConfirmCoreAsync(request, true, progress, cancellationToken);

    private async Task<HawalaImportResultDto> ConfirmCoreAsync(
        ConfirmHawalaImportDto request, bool queued,
        IProgress<HawalaImportProgressDto>? progress,
        CancellationToken cancellationToken, bool prepareOnly = false)
    {
        Report(progress, 5, "در حال بارگذاری و اعتبارسنجی پیش‌نمایش...");
        // Do not hold a shared job-row lock while the independent progress writer updates it.
        var jobStatus = await _context.HawalaImportJobs.AsNoTracking()
            .Where(x => x.BatchId == request.BatchId).Select(x => x.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (queued ? jobStatus != "Running" : jobStatus is "Queued" or "Running")
            throw new InvalidOperationException("این فایل در صف ثبت گروهی است.");
        await using var transaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await _context.EnsureCurrentTenantWriteAccessAsync(cancellationToken);
            // Take the tenant preparation lock before reading names or allocating codes.
            await _context.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={"HawalaImportPrepare:" + _context.CurrentTenantId}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=30000; IF @r < 0 THROW 50001, 'Import preparation is busy; retry shortly.', 1;", cancellationToken);
            // Serialize confirmations/enqueue/delete for this batch, even with RCSI enabled.
            await _context.HawalaImportBatches
                .FromSqlInterpolated($"SELECT * FROM dbo.HawalaImportBatches WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {_context.CurrentTenantId} AND Id = {request.BatchId}")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (!queued && await _context.HawalaImportJobs.AsNoTracking().AnyAsync(x => x.BatchId == request.BatchId &&
                    (x.Status == "Queued" || x.Status == "Running"), cancellationToken))
                throw new InvalidOperationException("این فایل در صف ثبت گروهی است.");
            var batch = await _context.HawalaImportBatches
                .AsNoTracking()
                .Include(x => x.Correspondent)
                .Include(x => x.Rows)
                .FirstOrDefaultAsync(x => x.Id == request.BatchId, cancellationToken)
                ?? throw new InvalidOperationException("پیش‌نمایش آپلود پیدا نشد.");

            if (batch.Status != "Preview")
                throw new InvalidOperationException("این پیش‌نمایش قبلاً ثبت شده یا دیگر قابل استفاده نیست.");
            if (batch.Rows.Any(x => !string.IsNullOrWhiteSpace(x.ValidationErrors)))
                throw new InvalidOperationException("تا رفع خطاهای پیش‌نمایش، ثبت فایل امکان‌پذیر نیست.");
            if (await _context.HawalaImportBatches.AsNoTracking().AnyAsync(
                    x => x.Id != batch.Id && x.FileHash == batch.FileHash && x.Status == "Posted",
                    cancellationToken))
                throw new InvalidOperationException("این فایل قبلاً به‌طور کامل ثبت شده است.");

            long? ownLocationId;
            Dictionary<long, Correspondent> activeCorrespondentsById;
            if (batch.LocationsPreparedAt.HasValue)
            {
                ownLocationId = batch.OwnPaymentLocationId;
                activeCorrespondentsById = await ValidatePreparedLocationsAsync(batch, cancellationToken);
            }
            else
            {
                if (request.UsePreparedLocations)
                    throw new InvalidOperationException("ابتدا مرحله ایجاد محل‌های پرداخت و تعیین مسئول را تکمیل کنید.");
                (ownLocationId, activeCorrespondentsById) = await PrepareLocationsCoreAsync(batch, request, progress, cancellationToken);
            }
            if (prepareOnly)
            {
                if (!batch.LocationsPreparedAt.HasValue)
                    await SavePreparedLocationsAsync(batch, ownLocationId!.Value, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                Report(progress, 100, "محل‌های پرداخت و مسئول‌های آن ذخیره شدند؛ هنوز هیچ حواله‌ای ثبت نشده است.");
                return new HawalaImportResultDto { BatchId = batch.Id };
            }

            var submittedCommissions = request.Commissions
                .GroupBy(x => x.RowId)
                .ToDictionary(x => x.Key, x => x.Last());
            var activeCurrencies = await _context.Currencies.Where(x => x.IsActive)
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            foreach (var row in batch.Rows.Where(x => x.PaymentLocationId != ownLocationId))
            {
                if (submittedCommissions.TryGetValue(row.Id, out var submitted))
                {
                    if (submitted.Amount.HasValue && submitted.Amount <= 0)
                        throw new InvalidOperationException($"کمیشن ردیف {row.ExcelRowNumber} باید بزرگتر از صفر باشد.");
                    row.AgentCommissionAmount = submitted.Amount;
                    row.AgentCommissionCurrencyId = submitted.Amount.HasValue
                        ? submitted.CurrencyId ?? row.CurrencyId
                        : null;
                    row.AgentCommissionCurrencyCode = row.AgentCommissionCurrencyId.HasValue &&
                        activeCurrencies.TryGetValue(row.AgentCommissionCurrencyId.Value, out var selectedCurrency)
                            ? selectedCurrency.Code
                            : null;
                }
                if (row.AgentCommissionAmount.HasValue &&
                    (!row.AgentCommissionCurrencyId.HasValue || !activeCurrencies.ContainsKey(row.AgentCommissionCurrencyId.Value)))
                    throw new InvalidOperationException($"ارز کمیشن ردیف {row.ExcelRowNumber} معتبر نیست.");
            }
            Report(progress, 55, "کمیشن‌ها و ارزها بررسی شدند.");

            await RevalidateBeforePostingAsync(batch, ownLocationId.Value, cancellationToken);
            Report(progress, 65, "در حال آماده‌سازی ثبت گروهی...");

            var orderedRows = batch.Rows.OrderBy(x => x.ExcelRowNumber).ToList();
            var destinationIds = orderedRows.Where(x => x.DestinationCorrespondentId.HasValue)
                .Select(x => x.DestinationCorrespondentId!.Value).Distinct().ToList();
            var destinationAccounts = await _context.Accounts
                .Where(x => x.CorrespondentId.HasValue && destinationIds.Contains(x.CorrespondentId.Value) && !x.IsArchived)
                .ToDictionaryAsync(x => x.CorrespondentId!.Value, x => x.Id, cancellationToken);
            var createItems = orderedRows.Select(row =>
            {
                var requiresOutgoing = row.PaymentLocationId != ownLocationId;
                long? destinationAccountId = null;
                if (requiresOutgoing && (!row.DestinationCorrespondentId.HasValue ||
                    !destinationAccounts.TryGetValue(row.DestinationCorrespondentId.Value, out var accountId)))
                    throw new InvalidOperationException($"حساب نمایندگی مقصد برای ردیف {row.ExcelRowNumber} پیدا نشد.");
                if (requiresOutgoing)
                    destinationAccountId = destinationAccounts[row.DestinationCorrespondentId!.Value];

                return new CreateHawalaDto
                {
                    Number = row.HawalaNumber!.Value,
                    HawalaType = "HawalaReceive",
                    CorrespondentId = batch.CorrespondentId,
                    PaymentLocationId = row.PaymentLocationId,
                    FromAccountId = destinationAccountId,
                    SenderName = row.SenderName,
                    ReceiverName = row.ReceiverName,
                    FromCurrencyId = row.CurrencyId!.Value,
                    FromAmount = row.Amount!.Value,
                    ToCurrencyId = row.CurrencyId.Value,
                    ToAmount = row.Amount.Value,
                    ExchangeRate = 1m,
                    CommissionAmount = null,
                    CommissionCurrencyId = null,
                    AgentCommissionAmount = null,
                    AgentCommissionCurrencyId = null,
                    ReferenceNumber = row.ReferenceNumber,
                    Status = requiresOutgoing ? "Paid" : "Pending",
                    GeneratedSendHawalaNumber = null, // Allocated from the destination's outgoing sequence at posting.
                    GeneratedSendAgentCommissionAmount = requiresOutgoing ? row.AgentCommissionAmount : null,
                    GeneratedSendAgentCommissionCurrencyId = requiresOutgoing ? row.AgentCommissionCurrencyId : null,
                    GeneratedSendReferenceNumber = requiresOutgoing ? row.ReferenceNumber : null,
                    Notes = null // File provenance belongs to HawalaImportBatches/Rows, not the customer's note.
                };
            }).ToList();

            Report(progress, 72, "ثبت سریع حواله‌ها و حسابداری با SqlBulkCopy...");
            var created = await _hawalaService.CreateHawalasAsync(createItems);
            Report(progress, 92, "حواله‌ها ثبت شدند؛ در حال نهایی‌سازی...");
            for (var index = 0; index < orderedRows.Count; index++)
                orderedRows[index].HawalaId = created[index].Id;
            var receivedIds = created.Select(x => x.Id).ToList();
            var generatedBySource = await _context.Hawalas
                .Where(x => x.SourceHawalaId.HasValue && receivedIds.Contains(x.SourceHawalaId.Value))
                .ToDictionaryAsync(x => x.SourceHawalaId!.Value, x => x.Id, cancellationToken);
            foreach (var row in orderedRows.Where(x => x.HawalaId.HasValue && generatedBySource.ContainsKey(x.HawalaId.Value)))
                row.GeneratedSendHawalaId = generatedBySource[row.HawalaId!.Value];

            var confirmedBy = _context.RequireCurrentUserId();
            var batchId = batch.Id;
            var batchFileName = batch.FileName;
            await FinalizeStagingAsync(batchId, confirmedBy, orderedRows, cancellationToken);
            await _context.HawalaImportBatches.Where(x => x.Id == batchId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.OwnPaymentLocationId, ownLocationId), cancellationToken);
            _context.ChangeTracker.Clear();
            await _auditLogService.LogAsync(
                "CREATE",
                "HawalaImportBatches",
                batchId,
                null,
                $"{created.Count} حواله از فایل '{batchFileName}' به‌صورت گروهی ثبت شد.",
                confirmedBy);
            if (queued)
                await _context.HawalaImportJobs.Where(x => x.BatchId == batchId && x.Status == "Running")
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "Completed")
                        .SetProperty(x => x.ProgressPercent, 100)
                        .SetProperty(x => x.ProgressMessage, "ثبت گروهی تکمیل شد.")
                        .SetProperty(x => x.CompletedAt, DateTime.UtcNow)
                        .SetProperty(x => x.ErrorMessage, (string?)null), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Report(progress, 100, "ثبت گروهی تکمیل شد.");

            return new HawalaImportResultDto
            {
                BatchId = batchId,
                ImportedCount = created.Count,
                GeneratedSendCount = generatedBySource.Count,
                OwnOfficeCount = orderedRows.Count(x => x.PaymentLocationId == ownLocationId),
                MissingCommissionCount = orderedRows.Count(x => x.GeneratedSendHawalaId.HasValue && !x.AgentCommissionAmount.HasValue),
                Totals = BuildTotals(orderedRows),
                ReceivedTotals = BuildTotals(orderedRows),
                SentTotals = BuildTotals(orderedRows.Where(x => x.GeneratedSendHawalaId.HasValue)),
                OwnOfficeTotals = BuildTotals(orderedRows.Where(x => x.PaymentLocationId == ownLocationId)),
                DestinationSummaries = orderedRows
                    .Where(x => x.GeneratedSendHawalaId.HasValue && x.DestinationCorrespondentId.HasValue)
                    .GroupBy(x => x.DestinationCorrespondentId!.Value)
                    .Select(group => new HawalaImportDestinationSummaryDto
                    {
                        CorrespondentId = group.Key,
                        CorrespondentName = activeCorrespondentsById.GetValueOrDefault(group.Key)?.Name
                            ?? "نمایندگی نامشخص",
                        HawalaCount = group.Count(),
                        Totals = BuildTotals(group)
                    })
                    .OrderBy(x => x.CorrespondentName)
                    .ToList()
            };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<(long? OwnLocationId, Dictionary<long, Correspondent> Correspondents)> PrepareLocationsCoreAsync(
        HawalaImportBatch batch, ConfirmHawalaImportDto request,
        IProgress<HawalaImportProgressDto>? progress, CancellationToken cancellationToken)
    {
        var selectedLocations = request.LocationsToCreate
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(PaymentLocationNameNormalizer.Normalize)
            .ToDictionary(x => x.Key, x => CleanText(x.First()), StringComparer.Ordinal);
        var unresolvedGroups = batch.Rows
            .Where(x => !x.PaymentLocationId.HasValue)
            .GroupBy(x => PaymentLocationNameNormalizer.Normalize(x.PaymentLocationText))
            .ToList();

        // Read names/aliases once on the import connection. A factory-created context
        // would wait on our own uncommitted inserts when RCSI is disabled on the host.
        var knownLocations = await _context.PaymentLocations.AsNoTracking()
            .Where(x => x.IsActive).Include(x => x.Aliases).ToListAsync(cancellationToken);
        var locationsByName = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var known in knownLocations)
        {
            locationsByName[known.NormalizedName] = known.Id;
            foreach (var alias in known.Aliases)
                locationsByName[alias.NormalizedName] = known.Id;
        }
        var newLocations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in unresolvedGroups)
        {
            if (!selectedLocations.TryGetValue(group.Key, out var displayName))
                throw new InvalidOperationException($"محل پرداخت «{group.First().PaymentLocationText}» باید برای ایجاد تأیید شود.");

            if (!locationsByName.TryGetValue(group.Key, out var locationId))
                newLocations[group.Key] = displayName;
        }
        foreach (var created in await BulkCreateImportEntitiesAsync("Locations", newLocations, cancellationToken))
            locationsByName[created.Key] = created.Value;
        foreach (var group in unresolvedGroups)
        {
            var locationId = locationsByName[group.Key];
            foreach (var row in group)
                row.PaymentLocationId = locationId;
        }
        Report(progress, 25, "محل‌های پرداخت آماده شدند.");

        var locationIds = batch.Rows.Where(x => x.PaymentLocationId.HasValue)
            .Select(x => x.PaymentLocationId!.Value).Distinct().ToList();
        var locations = await _context.PaymentLocations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var ownLocationId = batch.OwnPaymentLocationId;
        if (!string.IsNullOrWhiteSpace(request.OwnPaymentLocationName))
        {
            var ownKey = PaymentLocationNameNormalizer.Normalize(request.OwnPaymentLocationName);
            var matchesConfiguredOwn = batch.OwnPaymentLocationId.HasValue &&
                PaymentLocationNameNormalizer.Normalize(
                    await _context.PaymentLocations.AsNoTracking()
                        .Where(x => x.Id == batch.OwnPaymentLocationId.Value)
                        .Select(x => x.Name).SingleAsync(cancellationToken)) == ownKey;
            var ownRows = batch.Rows.Where(x =>
                PaymentLocationNameNormalizer.Normalize(x.PaymentLocationText) == ownKey ||
                (x.PaymentLocationId.HasValue && locations.TryGetValue(x.PaymentLocationId.Value, out var resolved) &&
                 PaymentLocationNameNormalizer.Normalize(resolved.Name) == ownKey)).ToList();
            if (ownRows.Count == 0 && matchesConfiguredOwn)
                ownLocationId = batch.OwnPaymentLocationId;
            else if (ownRows.Count == 0 || ownRows.Any(x => !x.PaymentLocationId.HasValue) ||
                ownRows.Select(x => x.PaymentLocationId).Distinct().Count() != 1)
                throw new InvalidOperationException("محل پرداخت دفتر خود صرافی در فایل پیدا نشد؛ آن را از فهرست محل‌های فایل انتخاب کنید.");
            else
                ownLocationId = ownRows[0].PaymentLocationId;
        }
        if (!ownLocationId.HasValue ||
            (ownLocationId != batch.OwnPaymentLocationId && !locations.ContainsKey(ownLocationId.Value)))
            throw new InvalidOperationException("محل پرداخت دفتر خود صرافی را پیش از ثبت از فهرست فایل انتخاب کنید.");

        var companySetting = await _context.CompanySettings.FirstOrDefaultAsync(cancellationToken);
        if (companySetting is null)
        {
            companySetting = new CompanySetting
            {
                CompanyName = "نام شرکت",
                OwnPaymentLocationId = ownLocationId.Value,
                CreatedAt = DateTime.UtcNow
            };
            _context.CompanySettings.Add(companySetting);
        }
        else if (companySetting.OwnPaymentLocationId != ownLocationId.Value)
        {
            companySetting.OwnPaymentLocationId = ownLocationId.Value;
            companySetting.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(cancellationToken);

        var activeCorrespondentList = await _context.Correspondents.AsNoTracking()
                .Where(x => !x.IsArchived)
                .ToListAsync(cancellationToken);
        var activeCorrespondentsById = activeCorrespondentList.ToDictionary(x => x.Id);
        var activeCorrespondents = activeCorrespondentList
            .GroupBy(x => PaymentLocationNameNormalizer.Normalize(x.Name))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var confirmedCorrespondents = request.CorrespondentsToCreate
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(PaymentLocationNameNormalizer.Normalize)
            .ToHashSet(StringComparer.Ordinal);
        var useExplicitMappings = request.LocationMappings.Count > 0;
        var mappings = new Dictionary<string, HawalaImportLocationMappingDto>(StringComparer.Ordinal);
        foreach (var mapping in request.LocationMappings)
        {
            var key = PaymentLocationNameNormalizer.Normalize(mapping.PaymentLocationName);
            if (string.IsNullOrWhiteSpace(key) || !mappings.TryAdd(key, mapping))
                throw new InvalidOperationException("برای هر محل پرداخت باید دقیقاً یک تعیین نمایندگی ثبت شود.");
        }
        var requestedNewCorrespondents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in mappings.Values)
        {
            var newName = CleanText(mapping.NewCorrespondentName);
            if (string.IsNullOrWhiteSpace(newName) && mapping.CreateCorrespondent)
                newName = CleanText(mapping.PaymentLocationName);
            var hasExisting = mapping.CorrespondentId.HasValue;
            var hasNew = !string.IsNullOrWhiteSpace(newName);
            if (hasExisting == hasNew)
                throw new InvalidOperationException(
                    $"برای محل پرداخت «{mapping.PaymentLocationName}» فقط یک نمایندگی موجود یا جدید انتخاب کنید.");
            if (hasNew)
                requestedNewCorrespondents.TryAdd(
                    PaymentLocationNameNormalizer.Normalize(newName), newName);
        }

        var assignmentHistory = await _context.PaymentLocationCorrespondentAssignments.AsNoTracking()
            .Where(x => locationIds.Contains(x.PaymentLocationId))
            .OrderByDescending(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var latestAssignments = assignmentHistory.GroupBy(x => x.PaymentLocationId)
            .ToDictionary(x => x.Key, x => x.First());
        if (!useExplicitMappings)
        {
            foreach (var group in batch.Rows.Where(x => x.PaymentLocationId != ownLocationId.Value).GroupBy(x => x.PaymentLocationId!.Value))
            {
                var name = locations[group.Key].Name;
                var key = PaymentLocationNameNormalizer.Normalize(name);
                if (latestAssignments.ContainsKey(group.Key) || activeCorrespondents.ContainsKey(key)) continue;
                var originalKey = PaymentLocationNameNormalizer.Normalize(group.First().PaymentLocationText);
                if (!confirmedCorrespondents.Contains(key) && !confirmedCorrespondents.Contains(originalKey))
                    throw new InvalidOperationException($"ایجاد نمایندگی «{name}» باید تأیید شود.");
                requestedNewCorrespondents.TryAdd(key, name);
            }
        }
        foreach (var requested in requestedNewCorrespondents)
            if (activeCorrespondents.ContainsKey(requested.Key))
                throw new InvalidOperationException($"نمایندگی «{requested.Value}» از قبل وجود دارد؛ آن را از فهرست نمایندگی‌های موجود انتخاب کنید.");
        var createdIds = await BulkCreateImportEntitiesAsync("Correspondents", requestedNewCorrespondents, cancellationToken);
        var createdIdList = createdIds.Values.ToList();
        var createdCorrespondents = (await _context.Correspondents.AsNoTracking()
            .Where(x => createdIdList.Contains(x.Id)).ToListAsync(cancellationToken))
            .ToDictionary(x => PaymentLocationNameNormalizer.Normalize(x.Name), StringComparer.Ordinal);
        foreach (var created in createdCorrespondents)
        {
            activeCorrespondentsById[created.Value.Id] = created.Value;
            activeCorrespondents[created.Key] = created.Value;
        }
        var newAssignments = new DataTable();
        AddColumns(newAssignments, ("PaymentLocationId", typeof(long)), ("CorrespondentId", typeof(long)));

        foreach (var group in batch.Rows
                     .Where(x => x.PaymentLocationId != ownLocationId.Value)
                     .GroupBy(x => x.PaymentLocationId!.Value))
        {
            var locationName = locations[group.Key].Name;
            var locationKey = PaymentLocationNameNormalizer.Normalize(locationName);
            var currentAssignment = latestAssignments.GetValueOrDefault(group.Key);
            if (currentAssignment is not null &&
                (currentAssignment.EffectiveFrom > DateTime.Today ||
                 currentAssignment.EffectiveTo <= DateTime.Today))
                throw new InvalidOperationException($"برای محل پرداخت «{locationName}» مسئولیت آینده ثبت شده است؛ ابتدا تاریخچه مسئولیت آن را بررسی کنید.");

            Correspondent destination;
            if (useExplicitMappings)
            {
                if (!mappings.TryGetValue(locationKey, out var mapping))
                    throw new InvalidOperationException($"برای محل پرداخت «{locationName}» یک نمایندگی مسئول انتخاب کنید.");
                var requestedNewName = CleanText(mapping.NewCorrespondentName);
                if (string.IsNullOrWhiteSpace(requestedNewName) && mapping.CreateCorrespondent)
                    requestedNewName = locationName;
                if (currentAssignment is not null)
                {
                    if (!mapping.CorrespondentId.HasValue ||
                        mapping.CorrespondentId != currentAssignment.CorrespondentId)
                        throw new InvalidOperationException($"مسئول محل پرداخت «{locationName}» قبلاً ثبت شده است؛ برای تغییر آن از صفحهٔ محل‌های پرداخت و تاریخ مؤثر استفاده کنید.");
                    destination = activeCorrespondentsById.GetValueOrDefault(currentAssignment.CorrespondentId)
                        ?? throw new InvalidOperationException($"نمایندگی مسئول محل پرداخت «{locationName}» غیرفعال است.");
                }
                else if (!string.IsNullOrWhiteSpace(requestedNewName))
                {
                    var newKey = PaymentLocationNameNormalizer.Normalize(requestedNewName);
                    destination = createdCorrespondents.GetValueOrDefault(newKey)
                        ?? throw new InvalidOperationException(
                            $"نمایندگی جدید «{requestedNewName}» برای محل پرداخت «{locationName}» ساخته نشد.");
                }
                else if (!activeCorrespondentsById.TryGetValue(mapping.CorrespondentId!.Value, out destination!))
                    throw new InvalidOperationException($"نمایندگی انتخاب‌شده برای «{locationName}» فعال یا معتبر نیست.");
            }
            else if (currentAssignment is not null)
            {
                destination = activeCorrespondentsById.GetValueOrDefault(currentAssignment.CorrespondentId)
                    ?? throw new InvalidOperationException($"نمایندگی مسئول محل پرداخت «{locationName}» غیرفعال است.");
            }
            else if (!activeCorrespondents.TryGetValue(locationKey, out destination!))
            {
                throw new InvalidOperationException($"نمایندگی مسئول «{locationName}» آماده نشد.");
            }

            foreach (var row in group)
                row.DestinationCorrespondentId = destination.Id;
            if (currentAssignment is null)
                newAssignments.Rows.Add(group.Key, destination.Id);
        }
        await BulkSaveImportAssignmentsAsync(newAssignments, cancellationToken);
        Report(progress, 45, "نمایندگی‌ها و حساب‌های مقصد آماده شدند.");
        return (ownLocationId, activeCorrespondentsById);
    }

    private async Task<Dictionary<string, long>> BulkCreateImportEntitiesAsync(
        string kind, IReadOnlyDictionary<string, string> names, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (names.Count == 0) return result;
        var table = new DataTable();
        AddColumns(table, ("NormalizedName", typeof(string)), ("Name", typeof(string)));
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name.Key) || name.Key.Length > 200 || string.IsNullOrWhiteSpace(name.Value) || name.Value.Length > 200)
                throw new InvalidOperationException("نام محل پرداخت یا نمایندگی باید بین 1 تا 200 حرف باشد.");
            table.Rows.Add(name.Key, name.Value);
        }
        await using var command = CreateImportBulkCommand($"dbo.usp_BulkCreateImport{kind}_v1");
        command.Parameters.Add(new SqlParameter("@Names", SqlDbType.Structured) { TypeName = "dbo.ImportEntityNamesTableType_v1", Value = table });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0), reader.GetInt64(1));
        return result;
    }

    private SqlCommand CreateImportBulkCommand(string procedure)
    {
        var command = ((SqlConnection)_context.Database.GetDbConnection()).CreateCommand();
        command.Transaction = (SqlTransaction)_context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = procedure;
        command.CommandTimeout = 120;
        command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
        command.Parameters.Add("@UserId", SqlDbType.BigInt).Value = _context.RequireCurrentUserId();
        return command;
    }

    private async Task BulkSaveImportAssignmentsAsync(DataTable assignments, CancellationToken cancellationToken)
    {
        if (assignments.Rows.Count == 0) return;
        await using var command = CreateImportBulkCommand("dbo.usp_BulkCreateImportAssignments_v1");
        command.Parameters.Add("@EffectiveFrom", SqlDbType.Date).Value = DateTime.Today;
        command.Parameters.Add(new SqlParameter("@Assignments", SqlDbType.Structured)
        { TypeName = "dbo.ImportAssignmentTableType_v1", Value = assignments });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Dictionary<long, Correspondent>> ValidatePreparedLocationsAsync(
        HawalaImportBatch batch, CancellationToken cancellationToken)
    {
        if (!batch.OwnPaymentLocationId.HasValue || batch.Rows.Any(x => !x.PaymentLocationId.HasValue))
            throw new InvalidOperationException("مرحله آماده‌سازی محل‌های پرداخت کامل نیست.");
        var locationIds = batch.Rows.Select(x => x.PaymentLocationId!.Value).Append(batch.OwnPaymentLocationId.Value).Distinct().ToList();
        if (await _context.PaymentLocations.CountAsync(x => locationIds.Contains(x.Id) && x.IsActive, cancellationToken) != locationIds.Count)
            throw new InvalidOperationException("یکی از محل‌های پرداخت آماده‌شده دیگر فعال نیست.");
        var assignments = await _context.PaymentLocationCorrespondentAssignments.AsNoTracking()
            .Where(x => locationIds.Contains(x.PaymentLocationId) && x.EffectiveFrom <= DateTime.Today &&
                        (x.EffectiveTo == null || x.EffectiveTo > DateTime.Today))
            .ToDictionaryAsync(x => x.PaymentLocationId, cancellationToken);
        var correspondents = await _context.Correspondents.Where(x => !x.IsArchived)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var row in batch.Rows.Where(x => x.PaymentLocationId != batch.OwnPaymentLocationId))
            if (!row.DestinationCorrespondentId.HasValue || !correspondents.ContainsKey(row.DestinationCorrespondentId.Value) ||
                !assignments.TryGetValue(row.PaymentLocationId!.Value, out var assignment) || assignment.CorrespondentId != row.DestinationCorrespondentId)
                throw new InvalidOperationException($"مسئول محل پرداخت ردیف {row.ExcelRowNumber} تغییر کرده یا غیرفعال است؛ پیش از ثبت آن را بررسی کنید.");
        return correspondents;
    }

    private async Task SavePreparedLocationsAsync(HawalaImportBatch batch, long ownLocationId, CancellationToken cancellationToken)
    {
        var mappings = new DataTable();
        AddColumns(mappings, ("RowId", typeof(long)), ("PaymentLocationId", typeof(long)), ("DestinationCorrespondentId", typeof(long)));
        foreach (var row in batch.Rows)
            mappings.Rows.Add(row.Id, row.PaymentLocationId!.Value,
                row.PaymentLocationId == ownLocationId ? DBNull.Value : Db(row.DestinationCorrespondentId));
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)_context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.usp_PrepareHawalaImportLocations_v1";
        command.CommandTimeout = 120;
        command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
        command.Parameters.Add("@BatchId", SqlDbType.BigInt).Value = batch.Id;
        command.Parameters.Add("@OwnPaymentLocationId", SqlDbType.BigInt).Value = ownLocationId;
        command.Parameters.Add(new SqlParameter("@Mappings", SqlDbType.Structured)
        { TypeName = "dbo.HawalaImportLocationTableType_v1", Value = mappings });
        await command.ExecuteNonQueryAsync(cancellationToken);
        await _auditLogService.LogAsync("UPDATE", "HawalaImportBatches", batch.Id, null,
            "محل‌های پرداخت و مسئول‌های فایل ذخیره شدند؛ حواله‌ها هنوز ثبت نشده‌اند.", _context.RequireCurrentUserId());
    }

    private async Task<List<HawalaImportRow>> ParseRowsAsync(
        Stream stream,
        long correspondentId,
        long? ownPaymentLocationId,
        IProgress<HawalaImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("فایل Excel هیچ صفحه‌ای ندارد.");
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow > MaximumRows)
            throw new InvalidOperationException($"حداکثر {MaximumRows:N0} ردیف در هر فایل قابل واردکردن است.");

        var currencies = await _context.Currencies.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var currencyByCode = currencies.ToDictionary(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase);
        var locations = await _context.PaymentLocations.AsNoTracking()
            .Where(x => x.IsActive)
            .Include(x => x.Aliases)
            .ToListAsync(cancellationToken);
        var locationByName = new Dictionary<string, PaymentLocation>(StringComparer.Ordinal);
        foreach (var location in locations)
        {
            locationByName.TryAdd(location.NormalizedName, location);
            foreach (var alias in location.Aliases)
                locationByName.TryAdd(alias.NormalizedName, location);
        }
        var correspondentByName = (await _context.Correspondents.AsNoTracking()
                .Where(x => !x.IsArchived)
                .ToListAsync(cancellationToken))
            .GroupBy(x => PaymentLocationNameNormalizer.Normalize(x.Name))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var parsed = new List<HawalaImportRow>();
        for (var rowNumber = 1; rowNumber <= lastRow; rowNumber++)
        {
            var excelRow = worksheet.Row(rowNumber);
            if (Enumerable.Range(1, 9).All(column => excelRow.Cell(column).IsEmpty()))
                continue;

            var errors = new List<string>();
            var number = ReadLong(excelRow.Cell(1));
            var reference = CleanText(ReadCellText(excelRow.Cell(2)));
            var sender = CleanText(ReadCellText(excelRow.Cell(3)));
            var receiver = CleanText(ReadCellText(excelRow.Cell(4)));
            var locationText = CleanText(ReadCellText(excelRow.Cell(5)));
            var amount = ReadDecimal(excelRow.Cell(6));
            var currencyCode = CleanText(ReadCellText(excelRow.Cell(7))).ToUpperInvariant();
            var commissionText = CleanText(ReadCellText(excelRow.Cell(8)));
            var commissionAmount = string.IsNullOrWhiteSpace(commissionText) ? null : ReadDecimal(excelRow.Cell(8));
            var commissionCurrencyCode = CleanText(ReadCellText(excelRow.Cell(9))).ToUpperInvariant();

            if (!number.HasValue || number <= 0) errors.Add("شماره حواله معتبر نیست.");
            if (string.IsNullOrEmpty(reference)) errors.Add("رفرنس الزامی است.");
            if (string.IsNullOrEmpty(sender)) errors.Add("نام فرستنده الزامی است.");
            if (string.IsNullOrEmpty(receiver)) errors.Add("نام گیرنده الزامی است.");
            if (string.IsNullOrEmpty(locationText)) errors.Add("محل پرداخت الزامی است.");
            if (!amount.HasValue || amount <= 0) errors.Add("مبلغ معتبر نیست.");
            if (!currencyByCode.TryGetValue(currencyCode, out var currency)) errors.Add($"ارز «{currencyCode}» تعریف یا فعال نیست.");
            if (!string.IsNullOrWhiteSpace(commissionText) && (!commissionAmount.HasValue || commissionAmount <= 0))
                errors.Add("کمیشن عامل پرداخت معتبر نیست.");
            Currency? commissionCurrency = null;
            if (!string.IsNullOrWhiteSpace(commissionCurrencyCode) &&
                !currencyByCode.TryGetValue(commissionCurrencyCode, out commissionCurrency))
                errors.Add($"ارز کمیشن «{commissionCurrencyCode}» تعریف یا فعال نیست.");

            var normalizedLocation = PaymentLocationNameNormalizer.Normalize(locationText);
            locationByName.TryGetValue(normalizedLocation, out var location);
            var destinationKey = location?.NormalizedName ?? normalizedLocation;
            correspondentByName.TryGetValue(destinationKey, out var destinationCorrespondent);
            var requiresOutgoing = location?.Id != ownPaymentLocationId;
            parsed.Add(new HawalaImportRow
            {
                ExcelRowNumber = rowNumber,
                HawalaNumber = number,
                ReferenceNumber = NullIfEmpty(reference),
                SenderName = NullIfEmpty(sender),
                ReceiverName = NullIfEmpty(receiver),
                PaymentLocationText = NullIfEmpty(locationText),
                PaymentLocationId = location?.Id,
                Amount = amount,
                CurrencyCode = NullIfEmpty(currencyCode),
                CurrencyId = currency?.Id,
                AgentCommissionAmount = requiresOutgoing && commissionAmount is > 0 ? commissionAmount : null,
                AgentCommissionCurrencyId = requiresOutgoing && commissionAmount is > 0
                    ? commissionCurrency?.Id ?? currency?.Id
                    : null,
                AgentCommissionCurrencyCode = requiresOutgoing && commissionAmount is > 0
                    ? commissionCurrency?.Code ?? currency?.Code
                    : null,
                DestinationCorrespondentId = requiresOutgoing ? destinationCorrespondent?.Id : null,
                ValidationErrors = JoinErrors(errors)
            });
            if (rowNumber == lastRow || rowNumber % 50 == 0)
                Report(progress, 20 + (int)Math.Round(rowNumber * 55d / Math.Max(1, lastRow)),
                    $"در حال خواندن ردیف {rowNumber:N0} از {lastRow:N0}...");
        }

        return parsed;
    }

    private async Task WriteStagingRowsAsync(
        IReadOnlyCollection<HawalaImportRow> rows,
        IProgress<HawalaImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        var table = new DataTable();
        AddColumns(table,
            ("TenantId", typeof(long)), ("BatchId", typeof(long)), ("ExcelRowNumber", typeof(int)),
            ("HawalaNumber", typeof(long)), ("ReferenceNumber", typeof(string)),
            ("SenderName", typeof(string)), ("ReceiverName", typeof(string)),
            ("PaymentLocationText", typeof(string)), ("PaymentLocationId", typeof(long)),
            ("Amount", typeof(decimal)), ("CurrencyCode", typeof(string)), ("CurrencyId", typeof(long)),
            ("AgentCommissionAmount", typeof(decimal)), ("AgentCommissionCurrencyId", typeof(long)),
            ("AgentCommissionCurrencyCode", typeof(string)), ("DestinationCorrespondentId", typeof(long)),
            ("ValidationErrors", typeof(string)), ("HawalaId", typeof(long)),
            ("GeneratedSendHawalaId", typeof(long)));
        foreach (var row in rows)
        {
            table.Rows.Add(_context.CurrentTenantId, row.BatchId, row.ExcelRowNumber,
                Db(row.HawalaNumber), Db(row.ReferenceNumber), Db(row.SenderName), Db(row.ReceiverName),
                Db(row.PaymentLocationText), Db(row.PaymentLocationId), Db(row.Amount), Db(row.CurrencyCode),
                Db(row.CurrencyId), Db(row.AgentCommissionAmount), Db(row.AgentCommissionCurrencyId),
                Db(row.AgentCommissionCurrencyCode), Db(row.DestinationCorrespondentId),
                Db(row.ValidationErrors), DBNull.Value, DBNull.Value);
        }

        var connection = (SqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        var sqlTransaction = _context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction
            ?? throw new InvalidOperationException("جدول آماده‌سازی باید داخل تراکنش SQL تکمیل شود.");
        using var bulk = new SqlBulkCopy(
            connection,
            SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers,
            sqlTransaction)
        {
            DestinationTableName = "[dbo].[HawalaImportRows]",
            BatchSize = 10_000,
            BulkCopyTimeout = 120,
            EnableStreaming = true,
            NotifyAfter = Math.Max(1, Math.Min(250, rows.Count / 20))
        };
        foreach (DataColumn column in table.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        bulk.SqlRowsCopied += (_, args) =>
        {
            var percent = 76 + (int)Math.Round(args.RowsCopied * 10d / Math.Max(1, rows.Count));
            Report(progress, percent,
                $"انتقال {args.RowsCopied:N0} از {rows.Count:N0} ردیف به جدول آماده‌سازی...");
        };
        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private async Task ValidateStagingRowsAsync(long batchId, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "[dbo].[usp_ValidateHawalaImportStaging_v1]";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 120;
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction
            ?? throw new InvalidOperationException("اعتبارسنجی جدول آماده‌سازی باید داخل تراکنش SQL انجام شود.");
        command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
        command.Parameters.Add("@BatchId", SqlDbType.BigInt).Value = batchId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task FinalizeStagingAsync(
        long batchId,
        long confirmedBy,
        IReadOnlyCollection<HawalaImportRow> rows,
        CancellationToken cancellationToken)
    {
        var mappings = new DataTable();
        AddColumns(mappings,
            ("RowId", typeof(long)), ("HawalaId", typeof(long)),
            ("GeneratedSendHawalaId", typeof(long)), ("PaymentLocationId", typeof(long)),
            ("DestinationCorrespondentId", typeof(long)), ("AgentCommissionAmount", typeof(decimal)),
            ("AgentCommissionCurrencyId", typeof(long)));
        foreach (var row in rows)
        {
            if (!row.HawalaId.HasValue || !row.PaymentLocationId.HasValue)
                throw new InvalidOperationException($"نتیجه ثبت ردیف {row.ExcelRowNumber} کامل نیست.");
            mappings.Rows.Add(row.Id, row.HawalaId.Value, Db(row.GeneratedSendHawalaId),
                row.PaymentLocationId.Value, Db(row.DestinationCorrespondentId),
                Db(row.AgentCommissionAmount), Db(row.AgentCommissionCurrencyId));
        }

        var connection = (SqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "[dbo].[usp_FinalizeHawalaImportStaging_v1]";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 120;
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction
            ?? throw new InvalidOperationException("نهایی‌سازی جدول آماده‌سازی باید داخل تراکنش SQL انجام شود.");
        command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
        command.Parameters.Add("@BatchId", SqlDbType.BigInt).Value = batchId;
        command.Parameters.Add("@ConfirmedBy", SqlDbType.BigInt).Value = confirmedBy;
        command.Parameters.Add(new SqlParameter("@Mappings", SqlDbType.Structured)
        {
            TypeName = "dbo.HawalaImportResultTableType_v1",
            Value = mappings
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddColumns(DataTable table, params (string Name, Type Type)[] columns)
    {
        foreach (var column in columns)
            table.Columns.Add(column.Name, column.Type);
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static void Report(IProgress<HawalaImportProgressDto>? progress, int percent, string message) =>
        progress?.Report(new HawalaImportProgressDto { Percent = Math.Clamp(percent, 0, 100), Message = message });

    private async Task RevalidateBeforePostingAsync(
        HawalaImportBatch batch,
        long ownLocationId,
        CancellationToken cancellationToken)
    {
        var numbers = batch.Rows.Select(x => x.HawalaNumber!.Value).ToList();
        var references = batch.Rows.Select(x => x.ReferenceNumber!).ToList();
        var sourcePeriodStart = await _context.CorrespondentAccountPeriods.AsNoTracking()
            .Where(x => x.CorrespondentId == batch.CorrespondentId)
            .Select(x => (DateTime?)x.PeriodTo)
            .MaxAsync(cancellationToken) ?? DateTime.MinValue;
        if (await _context.Hawalas.AsNoTracking().AnyAsync(
                x => x.CorrespondentId == batch.CorrespondentId && x.HawalaType == "HawalaReceive" &&
                     x.CreatedAt >= sourcePeriodStart &&
                     (numbers.Contains(x.Number) || (x.ReferenceNumber != null && references.Contains(x.ReferenceNumber))),
                cancellationToken))
            throw new InvalidOperationException("در فاصلهٔ پیش‌نمایش تا ثبت، شماره یا رفرنس یکی از حواله‌ها قبلاً ثبت شده است. فایل را دوباره پیش‌نمایش کنید.");

        var outgoingRows = batch.Rows.Where(x => x.PaymentLocationId != ownLocationId).ToList();
        var missingDestination = outgoingRows.FirstOrDefault(x => !x.DestinationCorrespondentId.HasValue);
        if (missingDestination != null)
            throw new InvalidOperationException($"نمایندگی مقصد ردیف {missingDestination.ExcelRowNumber} مشخص نیست.");

        // Incoming file numbers are not outgoing numbers. Destination sequences
        // are allocated atomically by HawalaService when the batch is posted.

        var currencyIds = batch.Rows.Select(x => x.CurrencyId!.Value).Distinct().ToList();
        var activeCurrencyCount = await _context.Currencies.AsNoTracking()
            .CountAsync(x => currencyIds.Contains(x.Id) && x.IsActive, cancellationToken);
        if (activeCurrencyCount != currencyIds.Count)
            throw new InvalidOperationException("یکی از ارزهای فایل غیرفعال یا حذف شده است. فایل را دوباره پیش‌نمایش کنید.");
        if (batch.Rows.Any(x => !x.PaymentLocationId.HasValue))
            throw new InvalidOperationException("برای همهٔ ردیف‌ها باید محل پرداخت مشخص باشد.");
    }

    private static HawalaImportPreviewDto BuildPreview(
        HawalaImportBatch batch,
        string correspondentName,
        long? ownLocationId,
        string ownLocationName,
        IReadOnlyDictionary<long, PaymentLocationCorrespondentAssignment> currentAssignments)
    {
        var rows = batch.Rows.OrderBy(x => x.ExcelRowNumber).ToList();
        return new HawalaImportPreviewDto
        {
            LocationsPrepared = batch.LocationsPreparedAt.HasValue,
            BatchId = batch.Id,
            FileName = batch.FileName,
            CorrespondentName = correspondentName,
            OwnPaymentLocationName = ownLocationName,
            OwnPaymentLocationId = ownLocationId,
            PaymentLocations = rows.Where(x => !string.IsNullOrWhiteSpace(x.PaymentLocationText))
                .GroupBy(x => x.PaymentLocation?.NormalizedName ??
                    PaymentLocationNameNormalizer.Normalize(x.PaymentLocationText))
                .Select(group =>
                {
                    var row = group.First();
                    var assignment = row.PaymentLocationId.HasValue &&
                        currentAssignments.TryGetValue(row.PaymentLocationId.Value, out var matched)
                            ? matched : null;
                    return new HawalaImportLocationOptionDto
                    {
                        Name = row.PaymentLocation?.Name ?? row.PaymentLocationText!,
                        PaymentLocationId = row.PaymentLocationId,
                        ResponsibleCorrespondentId = assignment?.CorrespondentId,
                        ResponsibleCorrespondentName = assignment?.Correspondent.Name
                    };
                })
                .OrderBy(x => x.Name).ToList(),
            RowCount = rows.Count,
            ValidRowCount = rows.Count(x => string.IsNullOrWhiteSpace(x.ValidationErrors)),
            InvalidRowCount = rows.Count(x => !string.IsNullOrWhiteSpace(x.ValidationErrors)),
            MissingLocations = rows.Where(x => !x.PaymentLocationId.HasValue && !string.IsNullOrWhiteSpace(x.PaymentLocationText))
                .Select(x => x.PaymentLocationText!)
                .GroupBy(PaymentLocationNameNormalizer.Normalize)
                .Select(x => x.First())
                .OrderBy(x => x)
                .ToList(),
            MissingCorrespondents = rows
                .Where(x => x.PaymentLocationId != ownLocationId &&
                            !x.DestinationCorrespondentId.HasValue &&
                            !string.IsNullOrWhiteSpace(x.PaymentLocationText))
                .Select(x => x.PaymentLocation?.Name ?? x.PaymentLocationText!)
                .GroupBy(PaymentLocationNameNormalizer.Normalize)
                .Select(x => x.First())
                .OrderBy(x => x)
                .ToList(),
            Totals = BuildTotals(rows),
            Rows = rows.Select(x => new HawalaImportRowDto
            {
                Id = x.Id,
                ExcelRowNumber = x.ExcelRowNumber,
                HawalaNumber = x.HawalaNumber,
                ReferenceNumber = x.ReferenceNumber,
                SenderName = x.SenderName,
                ReceiverName = x.ReceiverName,
                PaymentLocationText = x.PaymentLocationText,
                PaymentLocationId = x.PaymentLocationId,
                PaymentLocationName = x.PaymentLocation?.Name,
                Amount = x.Amount,
                CurrencyCode = x.CurrencyCode,
                CurrencyId = x.CurrencyId,
                RequiresOutgoingHawala = x.PaymentLocationId != ownLocationId,
                DestinationCorrespondentId = x.DestinationCorrespondentId,
                DestinationCorrespondentName = x.PaymentLocationId != ownLocationId ? x.PaymentLocationText : null,
                AgentCommissionAmount = x.AgentCommissionAmount,
                AgentCommissionCurrencyId = x.AgentCommissionCurrencyId,
                AgentCommissionCurrencyCode = x.AgentCommissionCurrencyCode,
                ValidationErrors = x.ValidationErrors
            }).ToList()
        };
    }

    private static List<HawalaImportTotalDto> BuildTotals(IEnumerable<HawalaImportRow> rows) => rows
        .Where(x => x.Amount.HasValue && !string.IsNullOrWhiteSpace(x.CurrencyCode))
        .GroupBy(x => x.CurrencyCode!, StringComparer.OrdinalIgnoreCase)
        .Select(x => new HawalaImportTotalDto { CurrencyCode = x.Key, RowCount = x.Count(), Amount = x.Sum(r => r.Amount!.Value) })
        .OrderBy(x => x.CurrencyCode)
        .ToList();

    private static long? ReadLong(IXLCell cell)
    {
        var text = ReadCellText(cell);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number == decimal.Truncate(number)) return (long)number;
        return null;
    }

    private static decimal? ReadDecimal(IXLCell cell)
    {
        var text = ReadCellText(cell).Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string ReadCellText(IXLCell cell)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        return value.ToString(CultureInfo.InvariantCulture).Trim();
    }

    private static string CleanText(string? value) => string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    private static string? JoinErrors(IEnumerable<string> errors) => errors.Any() ? string.Join(" | ", errors) : null;
}
