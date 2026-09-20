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

public sealed class HawalaImportService : IHawalaImportService
{
    private const int MaximumRows = 10_000;
    private const long MaximumFileSize = 10 * 1024 * 1024;
    private readonly ApplicationDbContext _context;
    private readonly IHawalaService _hawalaService;
    private readonly IPaymentLocationService _paymentLocationService;
    private readonly ICorrespondentService _correspondentService;
    private readonly IAuditLogService _auditLogService;

    public HawalaImportService(
        ApplicationDbContext context,
        IHawalaService hawalaService,
        IPaymentLocationService paymentLocationService,
        ICorrespondentService correspondentService,
        IAuditLogService auditLogService)
    {
        _context = context;
        _hawalaService = hawalaService;
        _paymentLocationService = paymentLocationService;
        _correspondentService = correspondentService;
        _auditLogService = auditLogService;
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
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("ابتدا در تنظیمات شرکت، محل پرداخت دفتر خود صرافی را تعیین کنید.");

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        Report(progress, 20, "فایل دریافت شد؛ در حال خواندن ردیف‌ها...");
        if (memory.Length == 0)
            throw new InvalidOperationException("فایل انتخاب‌شده خالی است.");
        if (memory.Length > MaximumFileSize)
            throw new InvalidOperationException("حجم فایل نباید بیشتر از ۱۰ مگابایت باشد.");

        await CleanupExpiredStagingAsync(cancellationToken);
        var fileHash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
        var alreadyImported = await _context.HawalaImportBatches
            .AsNoTracking()
            .AnyAsync(x => x.FileHash == fileHash && x.Status == "Posted", cancellationToken);
        if (alreadyImported)
            throw new InvalidOperationException("این فایل قبلاً به‌طور کامل ثبت شده است.");

        memory.Position = 0;
        var rows = await ParseRowsAsync(memory, correspondentId, ownLocation.Id, progress, cancellationToken);
        if (rows.Count == 0)
            throw new InvalidOperationException("هیچ ردیف قابل خواندن در فایل پیدا نشد.");
        if (rows.Count > MaximumRows)
            throw new InvalidOperationException($"حداکثر {MaximumRows:N0} ردیف در هر فایل قابل واردکردن است.");

        var batch = new HawalaImportBatch
        {
            CorrespondentId = correspondentId,
            OwnPaymentLocationId = ownLocation.Id,
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
        return BuildPreview(batch, correspondent.Name, ownLocation.Id, ownLocation.Name);
    }

    public async Task<HawalaImportResultDto> ConfirmAsync(
        ConfirmHawalaImportDto request,
        IProgress<HawalaImportProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Report(progress, 5, "در حال بارگذاری و اعتبارسنجی پیش‌نمایش...");
        await using var transaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        try
        {
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

            var selectedLocations = request.LocationsToCreate
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(PaymentLocationNameNormalizer.Normalize)
                .ToDictionary(x => x.Key, x => CleanText(x.First()), StringComparer.Ordinal);
            var unresolvedGroups = batch.Rows
                .Where(x => !x.PaymentLocationId.HasValue)
                .GroupBy(x => PaymentLocationNameNormalizer.Normalize(x.PaymentLocationText))
                .ToList();

            foreach (var group in unresolvedGroups)
            {
                if (!selectedLocations.TryGetValue(group.Key, out var displayName))
                    throw new InvalidOperationException($"محل پرداخت «{group.First().PaymentLocationText}» باید برای ایجاد تأیید شود.");

                var existing = await _paymentLocationService.FindByNameOrAliasAsync(displayName);
                var location = existing ?? await _paymentLocationService.CreateAsync(new CreatePaymentLocationDto
                {
                    Name = displayName,
                    Address = string.Empty,
                    IsActive = true
                });
                foreach (var row in group)
                    row.PaymentLocationId = location.Id;
            }
            Report(progress, 25, "محل‌های پرداخت آماده شدند.");

            var ownLocationId = batch.OwnPaymentLocationId
                ?? throw new InvalidOperationException("محل پرداخت دفتر خود صرافی در این پیش‌نمایش مشخص نیست؛ فایل را دوباره انتخاب کنید.");
            var locationIds = batch.Rows.Where(x => x.PaymentLocationId.HasValue)
                .Select(x => x.PaymentLocationId!.Value).Distinct().ToList();
            var locations = await _context.PaymentLocations
                .Where(x => locationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            var activeCorrespondents = (await _context.Correspondents
                    .Where(x => !x.IsArchived)
                    .ToListAsync(cancellationToken))
                .GroupBy(x => PaymentLocationNameNormalizer.Normalize(x.Name))
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var confirmedCorrespondents = request.CorrespondentsToCreate
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(PaymentLocationNameNormalizer.Normalize)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var group in batch.Rows
                         .Where(x => x.PaymentLocationId != ownLocationId)
                         .GroupBy(x => PaymentLocationNameNormalizer.Normalize(
                             locations[x.PaymentLocationId!.Value].Name)))
            {
                if (!activeCorrespondents.TryGetValue(group.Key, out var destination))
                {
                    var locationName = locations[group.First().PaymentLocationId!.Value].Name;
                    var originalKey = PaymentLocationNameNormalizer.Normalize(group.First().PaymentLocationText);
                    if (!confirmedCorrespondents.Contains(group.Key) && !confirmedCorrespondents.Contains(originalKey))
                        throw new InvalidOperationException($"ایجاد نمایندگی «{locationName}» باید تأیید شود.");

                    var createdCorrespondent = await _correspondentService.CreateAsync(new CreateCorrespondentDto
                    {
                        Name = locationName,
                        City = locationName,
                        CommissionMethod = "PerTransaction"
                    });
                    destination = await _context.Correspondents
                        .SingleAsync(x => x.Id == createdCorrespondent.Id, cancellationToken);
                    activeCorrespondents[group.Key] = destination;
                }

                foreach (var row in group)
                    row.DestinationCorrespondentId = destination.Id;
            }
            Report(progress, 45, "نمایندگی‌ها و حساب‌های مقصد آماده شدند.");

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

            await RevalidateBeforePostingAsync(batch, ownLocationId, cancellationToken);
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
                    GeneratedSendHawalaNumber = requiresOutgoing ? row.HawalaNumber : null,
                    GeneratedSendAgentCommissionAmount = requiresOutgoing ? row.AgentCommissionAmount : null,
                    GeneratedSendAgentCommissionCurrencyId = requiresOutgoing ? row.AgentCommissionCurrencyId : null,
                    GeneratedSendReferenceNumber = requiresOutgoing ? row.ReferenceNumber : null,
                    Notes = $"آپلود گروهی از فایل {batch.FileName}"
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
            _context.ChangeTracker.Clear();
            await _auditLogService.LogAsync(
                "CREATE",
                "HawalaImportBatches",
                batchId,
                null,
                $"{created.Count} حواله از فایل '{batchFileName}' به‌صورت گروهی ثبت شد.",
                confirmedBy);
            await transaction.CommitAsync(cancellationToken);
            Report(progress, 100, "ثبت گروهی تکمیل شد.");

            return new HawalaImportResultDto
            {
                BatchId = batchId,
                ImportedCount = created.Count,
                GeneratedSendCount = generatedBySource.Count,
                MissingCommissionCount = orderedRows.Count(x => x.GeneratedSendHawalaId.HasValue && !x.AgentCommissionAmount.HasValue),
                Totals = BuildTotals(orderedRows)
            };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<List<HawalaImportRow>> ParseRowsAsync(
        Stream stream,
        long correspondentId,
        long ownPaymentLocationId,
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

    private async Task CleanupExpiredStagingAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "[dbo].[usp_CleanupHawalaImportStaging_v1]";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 30;
            command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
            command.Parameters.Add("@CreatedBefore", SqlDbType.DateTime2).Value = DateTime.UtcNow.AddDays(-7);
            await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
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

        var destinationIds = outgoingRows.Select(x => x.DestinationCorrespondentId!.Value).Distinct().ToList();
        var outgoingNumbers = outgoingRows.Select(x => x.HawalaNumber!.Value).Distinct().ToList();
        var outgoingPeriodStarts = destinationIds.Count == 0
            ? new Dictionary<long, DateTime>()
            : await _context.CorrespondentAccountPeriods.AsNoTracking()
                .Where(x => destinationIds.Contains(x.CorrespondentId))
                .GroupBy(x => x.CorrespondentId)
                .Select(x => new { CorrespondentId = x.Key, Start = x.Max(p => p.PeriodTo) })
                .ToDictionaryAsync(x => x.CorrespondentId, x => x.Start, cancellationToken);
        var existingOutgoingKeys = destinationIds.Count == 0
            ? new HashSet<(long CorrespondentId, long Number)>()
            : (await _context.Hawalas.AsNoTracking()
                .Where(x => x.CorrespondentId.HasValue &&
                            destinationIds.Contains(x.CorrespondentId.Value) &&
                            x.HawalaType == "HawalaSend" &&
                            outgoingNumbers.Contains(x.Number))
                .Select(x => new { CorrespondentId = x.CorrespondentId!.Value, x.Number, x.CreatedAt })
                .ToListAsync(cancellationToken))
                .Where(x => x.CreatedAt >= outgoingPeriodStarts.GetValueOrDefault(
                    x.CorrespondentId, DateTime.MinValue))
                .Select(x => (x.CorrespondentId, x.Number))
                .ToHashSet();
        var duplicateOutgoing = outgoingRows.FirstOrDefault(x =>
            existingOutgoingKeys.Contains((x.DestinationCorrespondentId!.Value, x.HawalaNumber!.Value)));
        if (duplicateOutgoing != null)
            throw new InvalidOperationException($"نمبر حواله ارسالی ردیف {duplicateOutgoing.ExcelRowNumber} قبلاً برای نمایندگی مقصد ثبت شده است.");

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
        long ownLocationId,
        string ownLocationName)
    {
        var rows = batch.Rows.OrderBy(x => x.ExcelRowNumber).ToList();
        return new HawalaImportPreviewDto
        {
            BatchId = batch.Id,
            FileName = batch.FileName,
            CorrespondentName = correspondentName,
            OwnPaymentLocationName = ownLocationName,
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
