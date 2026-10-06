using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class SelectedRecordsService(ApplicationDbContext context) : ISelectedRecordsService
{
    public async Task<IReadOnlyList<SelectedRecordDto>> ReadAsync(IReadOnlyCollection<long> ids, bool transactions = false,
        CancellationToken cancellationToken = default)
    {
        var keys = ids.Distinct().ToArray();
        if (keys.Length == 0 || keys.Length > 10000 || keys.Any(x => x <= 0))
            throw new InvalidOperationException("بین 1 تا 10000 ردیف معتبر انتخاب کنید.");
        if (context.CurrentTenantId <= 0)
            throw new InvalidOperationException("صرافی جاری مشخص نیست.");
        using var scope = context.UseTenantScope(context.CurrentTenantId);
        List<SelectedRecordDto> rows;
        if (transactions)
            rows = await context.Transactions.AsNoTracking().Where(x => keys.Contains(x.Id))
                .Select(x => new SelectedRecordDto
                {
                    Id = x.Id, Number = x.TransactionNo, Kind = x.TransactionType,
                    SenderName = x.CustomerFullName ?? (x.Customer == null ? "" : x.Customer.FullName),
                    CorrespondentName = x.Branch == null ? "" : x.Branch.Name, Status = x.Status,
                    Notes = x.Remarks ?? "", CreatedAt = x.CreatedAt,
                    TransactionDetails = x.TransactionDetails.OrderBy(d => d.Id).Select(d => new SelectedTransactionDetailDto
                    {
                        FromAmount = d.FromAmount, FromCurrency = d.FromCurrency == null ? "" : d.FromCurrency.Code,
                        ToAmount = d.ToAmount, ToCurrency = d.ToCurrency == null ? "" : d.ToCurrency.Code,
                        ExchangeRate = d.ExchangeRate, TransferAmount = d.TransferAmount,
                        Commission = d.CommissionAmount, CommissionCurrency = d.CommissionCurrency == null ? "" : d.CommissionCurrency.Code
                    }).ToList()
                }).ToListAsync(cancellationToken);
        else
            rows = await context.Hawalas.AsNoTracking().Where(x => keys.Contains(x.Id))
                .Select(x => new SelectedRecordDto
                {
                    Id = x.Id, Number = x.Number.ToString(), Kind = x.HawalaType,
                    CorrespondentId = x.CorrespondentId, CorrespondentName = x.Correspondent == null ? "" : x.Correspondent.Name,
                    SenderName = x.SenderName ?? "", ReceiverName = x.ReceiverName ?? "",
                    ReferenceNumber = x.ReferenceNumber ?? "", PaymentLocationName = x.PaymentLocation == null ? "" : x.PaymentLocation.Name,
                    Amount = x.HawalaType == "HawalaSend" ? x.ToAmount ?? x.FromAmount : x.FromAmount,
                    CurrencyCode = x.HawalaType == "HawalaSend" ? x.ToCurrency.Code : x.FromCurrency.Code,
                    AgentCommission = x.AgentCommissionAmount,
                    CommissionCurrencyCode = x.AgentCommissionCurrency == null ? "" : x.AgentCommissionCurrency.Code,
                    SenderPhone = x.SenderPhone ?? "", ReceiverPhone = x.ReceiverPhone ?? "",
                    SenderIdentity = x.SenderTazkiraNumber ?? "", ReceiverIdentity = x.ReceiverTazkiraNumber ?? "",
                    Notes = x.Notes ?? "", Status = x.Status, CreatedAt = x.CreatedAt, SourceHawalaId = x.SourceHawalaId
                }).ToListAsync(cancellationToken);
        if (rows.Count != keys.Length)
            throw new InvalidOperationException("بعضی ردیف‌ها حذف شده‌اند یا قابل دسترسی نیستند؛ انتخاب‌ها را بازبینی کنید.");
        if (!transactions)
        {
            // Also protect sharing before the cleanup migration has been deployed.
            // Match the full system note to its actual import file; never hide an edited user note.
            var provenance = await context.HawalaImportRows.AsNoTracking()
                .Where(r => (r.HawalaId.HasValue && keys.Contains(r.HawalaId.Value)) ||
                            (r.GeneratedSendHawalaId.HasValue && keys.Contains(r.GeneratedSendHawalaId.Value)))
                .Select(r => new { r.HawalaId, r.GeneratedSendHawalaId, r.Batch!.FileName })
                .ToListAsync(cancellationToken);
            var byRowId = rows.ToDictionary(r => r.Id);
            foreach (var import in provenance)
                foreach (var id in new[] { import.HawalaId, import.GeneratedSendHawalaId })
                    if (id.HasValue && byRowId.TryGetValue(id.Value, out var row) &&
                        row.Notes == $"آپلود گروهی از فایل {import.FileName}") row.Notes = "";
        }
        var byId = rows.ToDictionary(x => x.Id);
        return keys.Select(x => byId[x]).ToList();
    }

    public async Task<SelectedRecordsFileDto> ExportAsync(IReadOnlyCollection<long> ids, bool transactions = false,
        CancellationToken cancellationToken = default)
    {
        var rows = await ReadAsync(ids, transactions, cancellationToken);
        if (transactions)
            return new("SelectedTransactions.xlsx", ExcelMimeType, CreateWorkbook(rows, true));
        var groups = rows.GroupBy(x => (x.CorrespondentId, x.Kind)).ToList();
        string Name(IGrouping<(long? CorrespondentId, string Kind), SelectedRecordDto> group) =>
            $"Hawalas_{group.Key.CorrespondentId?.ToString(CultureInfo.InvariantCulture) ?? "none"}_{group.Key.Kind}.xlsx";
        if (groups.Count == 1)
            return new(Name(groups[0]), ExcelMimeType, CreateWorkbook(groups[0].ToList(), false));
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = archive.CreateEntry(Name(group)).Open();
                stream.Write(CreateWorkbook(group.ToList(), false));
            }
        return new("SelectedHawalas.zip", "application/zip", output.ToArray());
    }

    private const string ExcelMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static byte[] CreateWorkbook(IReadOnlyList<SelectedRecordDto> rows, bool transactions)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(transactions ? "Transactions" : "Hawalas");
        sheet.RightToLeft = true;
        var index = 1;
        if (transactions)
        {
            var headings = new[] { "شماره", "نوع", "مشتری", "شعبه", "وضعیت", "تاریخ میلادی", "مبلغ مبدأ", "ارز مبدأ", "مبلغ مقصد", "ارز مقصد", "نرخ", "مبلغ انتقال", "کمیشن", "ارز کمیشن" };
            for (var column = 0; column < headings.Length; column++)
                sheet.Cell(index, column + 1).Value = headings[column];
            index++;
        }
        foreach (var row in rows)
        {
            if (transactions)
            {
                var details = row.TransactionDetails.Count > 0 ? row.TransactionDetails : [new SelectedTransactionDetailDto()];
                foreach (var detail in details)
                {
                    sheet.Cell(index, 1).Value = row.Number;
                    sheet.Cell(index, 2).Value = row.Kind;
                    sheet.Cell(index, 3).Value = row.SenderName;
                    sheet.Cell(index, 4).Value = row.CorrespondentName;
                    sheet.Cell(index, 5).Value = row.Status;
                    sheet.Cell(index, 6).Value = row.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
                    if (detail.FromAmount.HasValue) sheet.Cell(index, 7).Value = detail.FromAmount.Value;
                    sheet.Cell(index, 8).Value = detail.FromCurrency;
                    if (detail.ToAmount.HasValue) sheet.Cell(index, 9).Value = detail.ToAmount.Value;
                    sheet.Cell(index, 10).Value = detail.ToCurrency;
                    if (detail.ExchangeRate.HasValue) sheet.Cell(index, 11).Value = detail.ExchangeRate.Value;
                    if (detail.TransferAmount.HasValue) sheet.Cell(index, 12).Value = detail.TransferAmount.Value;
                    sheet.Cell(index, 13).Value = detail.Commission;
                    sheet.Cell(index, 14).Value = detail.CommissionCurrency;
                    foreach (var column in new[] { 7, 9, 12, 13 }) sheet.Cell(index, column).Style.NumberFormat.Format = "[$-en-US]0";
                    sheet.Cell(index, 11).Style.NumberFormat.Format = "[$-en-US]0.########";
                    index++;
                }
                continue;
            }
            else
            {
                // Import contract: nine columns, no header, no summary rows. Text cells are
                // literal values (including strings beginning with '='), never formulas.
                sheet.Cell(index, 1).Value = row.Number; // Preserve long numbers exactly, beyond Excel's 15-digit numeric precision.
                sheet.Cell(index, 2).Value = row.ReferenceNumber;
                sheet.Cell(index, 3).Value = row.SenderName;
                sheet.Cell(index, 4).Value = row.ReceiverName;
                sheet.Cell(index, 5).Value = row.PaymentLocationName;
                sheet.Cell(index, 6).Value = row.Amount;
                sheet.Cell(index, 7).Value = row.CurrencyCode;
                if (row.AgentCommission is > 0)
                {
                    sheet.Cell(index, 8).Value = row.AgentCommission.Value;
                    sheet.Cell(index, 9).Value = row.CommissionCurrencyCode;
                }
                sheet.Range(index, 6, index, 8).Style.NumberFormat.Format = "[$-en-US]0";
            }
            index++;
        }
        sheet.Columns(1, transactions ? 14 : 9).Width = 22;
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
