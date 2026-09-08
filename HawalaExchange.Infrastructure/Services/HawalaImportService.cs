using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class HawalaImportService : IHawalaImportService
{
    private const int MaximumRows = 10_000;
    private const long MaximumFileSize = 10 * 1024 * 1024;
    private readonly ApplicationDbContext _context;
    private readonly IHawalaService _hawalaService;
    private readonly IPaymentLocationService _paymentLocationService;
    private readonly IAuditLogService _auditLogService;

    public HawalaImportService(
        ApplicationDbContext context,
        IHawalaService hawalaService,
        IPaymentLocationService paymentLocationService,
        IAuditLogService auditLogService)
    {
        _context = context;
        _hawalaService = hawalaService;
        _paymentLocationService = paymentLocationService;
        _auditLogService = auditLogService;
    }

    public async Task<HawalaImportPreviewDto> PreviewAsync(
        Stream file,
        string fileName,
        long correspondentId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("تنها فایل Excel با پسوند .xlsx قابل قبول است.");

        var correspondent = await _context.Correspondents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == correspondentId && !x.IsArchived, cancellationToken)
            ?? throw new InvalidOperationException("نمایندگی انتخاب‌شده یافت نشد یا غیرفعال است.");

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        if (memory.Length == 0)
            throw new InvalidOperationException("فایل انتخاب‌شده خالی است.");
        if (memory.Length > MaximumFileSize)
            throw new InvalidOperationException("حجم فایل نباید بیشتر از ۱۰ مگابایت باشد.");

        var fileHash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
        var alreadyImported = await _context.HawalaImportBatches
            .AsNoTracking()
            .AnyAsync(x => x.FileHash == fileHash && x.Status == "Posted", cancellationToken);
        if (alreadyImported)
            throw new InvalidOperationException("این فایل قبلاً به‌طور کامل ثبت شده است.");

        memory.Position = 0;
        var rows = await ParseRowsAsync(memory, correspondentId, cancellationToken);
        if (rows.Count == 0)
            throw new InvalidOperationException("هیچ ردیف قابل خواندن در فایل پیدا نشد.");
        if (rows.Count > MaximumRows)
            throw new InvalidOperationException($"حداکثر {MaximumRows:N0} ردیف در هر فایل قابل واردکردن است.");

        var batch = new HawalaImportBatch
        {
            CorrespondentId = correspondentId,
            FileName = Path.GetFileName(fileName),
            FileHash = fileHash,
            Status = "Preview",
            RowCount = rows.Count,
            CreatedBy = _context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow,
            Rows = rows
        };
        _context.HawalaImportBatches.Add(batch);
        await _context.SaveChangesAsync(cancellationToken);

        return BuildPreview(batch, correspondent.Name);
    }

    public async Task<HawalaImportResultDto> ConfirmAsync(
        ConfirmHawalaImportDto request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var batch = await _context.HawalaImportBatches
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

            await RevalidateBeforePostingAsync(batch, cancellationToken);

            var orderedRows = batch.Rows.OrderBy(x => x.ExcelRowNumber).ToList();
            var createItems = orderedRows.Select(row => new CreateHawalaDto
            {
                Number = row.HawalaNumber!.Value,
                HawalaType = "HawalaReceive",
                CorrespondentId = batch.CorrespondentId,
                PaymentLocationId = row.PaymentLocationId,
                SenderName = row.SenderName,
                ReceiverName = row.ReceiverName,
                FromCurrencyId = row.CurrencyId!.Value,
                FromAmount = row.Amount!.Value,
                ToCurrencyId = row.CurrencyId.Value,
                ToAmount = row.Amount.Value,
                ExchangeRate = 1m,
                ReferenceNumber = row.ReferenceNumber,
                Status = "Pending",
                Notes = $"آپلود گروهی از فایل {batch.FileName}"
            }).ToList();

            var created = await _hawalaService.CreateHawalasAsync(createItems);
            for (var index = 0; index < orderedRows.Count; index++)
                orderedRows[index].HawalaId = created[index].Id;

            batch.Status = "Posted";
            batch.ConfirmedAt = DateTime.UtcNow;
            batch.ConfirmedBy = _context.RequireCurrentUserId();
            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogService.LogAsync(
                "CREATE",
                "HawalaImportBatches",
                batch.Id,
                null,
                $"{created.Count} حواله از فایل '{batch.FileName}' به‌صورت گروهی ثبت شد.",
                batch.ConfirmedBy.Value);
            await transaction.CommitAsync(cancellationToken);

            return new HawalaImportResultDto
            {
                BatchId = batch.Id,
                ImportedCount = created.Count,
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

        var parsed = new List<HawalaImportRow>();
        for (var rowNumber = 1; rowNumber <= lastRow; rowNumber++)
        {
            var excelRow = worksheet.Row(rowNumber);
            if (Enumerable.Range(1, 7).All(column => excelRow.Cell(column).IsEmpty()))
                continue;

            var errors = new List<string>();
            var number = ReadLong(excelRow.Cell(1));
            var reference = CleanText(ReadCellText(excelRow.Cell(2)));
            var sender = CleanText(ReadCellText(excelRow.Cell(3)));
            var receiver = CleanText(ReadCellText(excelRow.Cell(4)));
            var locationText = CleanText(ReadCellText(excelRow.Cell(5)));
            var amount = ReadDecimal(excelRow.Cell(6));
            var currencyCode = CleanText(ReadCellText(excelRow.Cell(7))).ToUpperInvariant();

            if (!number.HasValue || number <= 0) errors.Add("شماره حواله معتبر نیست.");
            if (string.IsNullOrEmpty(reference)) errors.Add("رفرنس الزامی است.");
            if (string.IsNullOrEmpty(sender)) errors.Add("نام فرستنده الزامی است.");
            if (string.IsNullOrEmpty(receiver)) errors.Add("نام گیرنده الزامی است.");
            if (string.IsNullOrEmpty(locationText)) errors.Add("محل پرداخت الزامی است.");
            if (!amount.HasValue || amount <= 0) errors.Add("مبلغ معتبر نیست.");
            if (!currencyByCode.TryGetValue(currencyCode, out var currency)) errors.Add($"ارز «{currencyCode}» تعریف یا فعال نیست.");

            locationByName.TryGetValue(PaymentLocationNameNormalizer.Normalize(locationText), out var location);
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
                ValidationErrors = JoinErrors(errors)
            });
        }

        AddDuplicateErrors(parsed);
        var numbers = parsed.Where(x => x.HawalaNumber.HasValue).Select(x => x.HawalaNumber!.Value).Distinct().ToList();
        var references = parsed.Where(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber)).Select(x => x.ReferenceNumber!).Distinct().ToList();
        var existingNumbers = await _context.Hawalas.AsNoTracking()
            .Where(x => x.CorrespondentId == correspondentId && x.HawalaType == "HawalaReceive" && numbers.Contains(x.Number))
            .Select(x => x.Number).ToHashSetAsync(cancellationToken);
        var existingReferences = await _context.Hawalas.AsNoTracking()
            .Where(x => x.CorrespondentId == correspondentId && x.HawalaType == "HawalaReceive" &&
                        x.ReferenceNumber != null && references.Contains(x.ReferenceNumber))
            .Select(x => x.ReferenceNumber!).ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
        foreach (var row in parsed)
        {
            if (row.HawalaNumber.HasValue && existingNumbers.Contains(row.HawalaNumber.Value))
                AppendError(row, "شماره حواله قبلاً برای این نمایندگی ثبت شده است.");
            if (row.ReferenceNumber != null && existingReferences.Contains(row.ReferenceNumber))
                AppendError(row, "رفرنس قبلاً برای این نمایندگی ثبت شده است.");
        }

        return parsed;
    }

    private async Task RevalidateBeforePostingAsync(HawalaImportBatch batch, CancellationToken cancellationToken)
    {
        var numbers = batch.Rows.Select(x => x.HawalaNumber!.Value).ToList();
        var references = batch.Rows.Select(x => x.ReferenceNumber!).ToList();
        if (await _context.Hawalas.AsNoTracking().AnyAsync(
                x => x.CorrespondentId == batch.CorrespondentId && x.HawalaType == "HawalaReceive" &&
                     (numbers.Contains(x.Number) || (x.ReferenceNumber != null && references.Contains(x.ReferenceNumber))),
                cancellationToken))
            throw new InvalidOperationException("در فاصلهٔ پیش‌نمایش تا ثبت، شماره یا رفرنس یکی از حواله‌ها قبلاً ثبت شده است. فایل را دوباره پیش‌نمایش کنید.");

        var currencyIds = batch.Rows.Select(x => x.CurrencyId!.Value).Distinct().ToList();
        var activeCurrencyCount = await _context.Currencies.AsNoTracking()
            .CountAsync(x => currencyIds.Contains(x.Id) && x.IsActive, cancellationToken);
        if (activeCurrencyCount != currencyIds.Count)
            throw new InvalidOperationException("یکی از ارزهای فایل غیرفعال یا حذف شده است. فایل را دوباره پیش‌نمایش کنید.");
        if (batch.Rows.Any(x => !x.PaymentLocationId.HasValue))
            throw new InvalidOperationException("برای همهٔ ردیف‌ها باید محل پرداخت مشخص باشد.");
    }

    private static HawalaImportPreviewDto BuildPreview(HawalaImportBatch batch, string correspondentName)
    {
        var rows = batch.Rows.OrderBy(x => x.ExcelRowNumber).ToList();
        return new HawalaImportPreviewDto
        {
            BatchId = batch.Id,
            FileName = batch.FileName,
            CorrespondentName = correspondentName,
            RowCount = rows.Count,
            ValidRowCount = rows.Count(x => string.IsNullOrWhiteSpace(x.ValidationErrors)),
            InvalidRowCount = rows.Count(x => !string.IsNullOrWhiteSpace(x.ValidationErrors)),
            MissingLocations = rows.Where(x => !x.PaymentLocationId.HasValue && !string.IsNullOrWhiteSpace(x.PaymentLocationText))
                .Select(x => x.PaymentLocationText!)
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

    private static void AddDuplicateErrors(List<HawalaImportRow> rows)
    {
        foreach (var group in rows.Where(x => x.HawalaNumber.HasValue).GroupBy(x => x.HawalaNumber).Where(x => x.Count() > 1))
            foreach (var row in group) AppendError(row, "شماره حواله در همین فایل تکراری است.");
        foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber))
                     .GroupBy(x => x.ReferenceNumber!, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            foreach (var row in group) AppendError(row, "رفرنس در همین فایل تکراری است.");
    }

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
    private static void AppendError(HawalaImportRow row, string error) => row.ValidationErrors = string.IsNullOrWhiteSpace(row.ValidationErrors) ? error : $"{row.ValidationErrors} | {error}";
}
