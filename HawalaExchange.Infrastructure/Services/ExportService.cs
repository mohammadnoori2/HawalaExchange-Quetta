using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Helpers;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.Infrastructure.Services;

public class ExportService : IExportService
{
    private const int ReportColumnCount = 8;

    private readonly ApplicationDbContext _context;
    private readonly IConverter _pdfConverter;

    public ExportService(ApplicationDbContext context, IConverter pdfConverter)
    {
        _context = context;
        _pdfConverter = pdfConverter;
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> ExportCustomerActivitiesAsync(
        ExportFilterDto filter,
        ExportFormat format)
    {
        var (customer, company, report) = await LoadReportAsync(filter);
        return format == ExportFormat.Excel
            ? BuildExcelReport(customer, company, report)
            : BuildPdfReport(customer, company, report);
    }

    public async Task<CustomerActivityReportDto> GetCustomerActivitiesPreviewAsync(ExportFilterDto filter) =>
        (await LoadReportAsync(filter)).Report;

    // Export the server-held preview snapshot, not a newly queried set of financial movements.
    public async Task<(byte[] Content, string ContentType, string FileName)> ExportCustomerActivitiesPreviewAsync(CustomerActivityReportDto report, ExportFormat format)
    {
        var customer = report.AccountId.HasValue ? await GetAccountReportOwnerAsync(report.AccountId.Value) : report.CorrespondentId.HasValue
            ? await GetCorrespondentReportOwnerAsync(report.CorrespondentId.Value)
            : await _context.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == report.CustomerId)
                ?? throw new ArgumentException("مشتری مورد نظر پیدا نشد.");
        var company = await _context.CompanySettings.AsNoTracking().FirstOrDefaultAsync();
        return format == ExportFormat.Excel ? BuildExcelReport(customer, company, report) : BuildPdfReport(customer, company, report);
    }

    private async Task<(Customer Customer, CompanySetting? Company, CustomerActivityReportDto Report)> LoadReportAsync(ExportFilterDto filter)
    {
        if (filter.AccountId.HasValue || filter.CorrespondentId.HasValue)
        {
            if (filter.CustomerId.HasValue || (filter.AccountId.HasValue && filter.CorrespondentId.HasValue)) throw new ArgumentException("فقط یک حساب، مشتری یا نمایندگی را انتخاب کنید.");
            return await LoadCorrespondentReportAsync(filter);
        }
        if (!filter.CustomerId.HasValue || filter.CustomerId.Value <= 0)
            throw new ArgumentException("مشتری مشخص نشده است.");
        if (filter.FromDate > filter.ToDate || filter.MinAmount < 0 || filter.MaxAmount < 0 || filter.MinAmount > filter.MaxAmount)
            throw new ArgumentException("بازه تاریخ یا مبلغ معتبر نیست.");

        var customer = await _context.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == filter.CustomerId.Value)
            ?? throw new ArgumentException("مشتری مورد نظر پیدا نشد.");
        var company = await _context.CompanySettings.AsNoTracking().FirstOrDefaultAsync();
        var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
        var report = account == null ? new CustomerActivityReportDto() : await LoadActivityRowsAsync(account.Id, filter);
        report.CustomerName = customer.FullName;
        report.CustomerId = customer.Id;
        report.CustomerCode = customer.CustomerCode;
        report.FromDate = filter.FromDate;
        report.ToDate = filter.ToDate;
        return (customer, company, report);
    }

    private async Task<Customer> GetCorrespondentReportOwnerAsync(long id)
    {
        var correspondent = await _context.Correspondents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ArgumentException("نمایندگی مورد نظر پیدا نشد.");
        return new Customer { FullName = correspondent.Name, CustomerCode = correspondent.Code };
    }

    private async Task<Customer> GetAccountReportOwnerAsync(long id)
    {
        var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ArgumentException("حساب مورد نظر پیدا نشد.");
        return new Customer { FullName = account.AccountName, CustomerCode = account.AccountCode };
    }

    private async Task<(Customer Customer, CompanySetting? Company, CustomerActivityReportDto Report)> LoadCorrespondentReportAsync(ExportFilterDto filter)
    {
        if (filter.FromDate > filter.ToDate || filter.MinAmount < 0 || filter.MaxAmount < 0 || filter.MinAmount > filter.MaxAmount)
            throw new ArgumentException("بازه تاریخ یا مبلغ معتبر نیست.");
        var owner = filter.AccountId.HasValue ? await GetAccountReportOwnerAsync(filter.AccountId.Value) : await GetCorrespondentReportOwnerAsync(filter.CorrespondentId!.Value);
        var account = filter.AccountId.HasValue ? await _context.Accounts.AsNoTracking().FirstAsync(x => x.Id == filter.AccountId.Value)
            : await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.CorrespondentId == filter.CorrespondentId!.Value);
        var report = new CustomerActivityReportDto { AccountId = filter.AccountId, CorrespondentId = filter.CorrespondentId ?? account?.CorrespondentId, CustomerName = owner.FullName,
            CustomerCode = owner.CustomerCode, FromDate = filter.FromDate, ToDate = filter.ToDate };
        if (account != null)
        {
            // Reuse the operational statement rules: pending commissions are excluded,
            // recognized commissions move on the closing boundary, not their posting date.
            var statement = await new AccountStatementService(_context).GetAsync(new AccountStatementFilter
            {
                AccountId = account.Id,
                From = filter.FromDate.HasValue ? DateTime.SpecifyKind(filter.FromDate.Value, DateTimeKind.Utc).ToLocalTime().Date : DateTime.MinValue,
                To = filter.ToDate.HasValue ? DateTime.SpecifyKind(filter.ToDate.Value, DateTimeKind.Utc).ToLocalTime().Date : new DateTime(9998, 12, 30),
                Currency = filter.CurrencyCode?.Trim().ToUpperInvariant() ?? "", Search = "",
                Direction = filter.Direction ?? "", MinAmount = filter.MinAmount, MaxAmount = filter.MaxAmount
            });
            report.Rows = statement.Entries.Select(x => new CustomerActivityRowDto { HawalaId = x.HawalaId, AccountMoneyOperationId = x.AccountMoneyOperationId, Date = DateTime.SpecifyKind(x.Date, DateTimeKind.Utc),
                Reference = x.Document, Description = x.Description, DisplayDescription = string.Join(" - ", new[] { x.Document, x.Description }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Currency = x.Currency, TalabKar = x.Credit, BadehKar = x.Debit, RunningBalance = x.Balance }).ToList();
            await ApplyHawalaDescriptionsAsync(report.Rows);
            await ApplyAccountOperationDescriptionsAsync(report.Rows);
            var search = filter.Search?.Trim() ?? "";
            if (search.Length > 0) report.Rows = report.Rows.Where(x => x.DisplayDescription.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (x.Reference?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
            report.Balances = statement.Summaries.Select(x => new CustomerActivityBalanceDto { Currency = x.Currency, Opening = x.Opening,
                Credit = x.Credit, Debit = x.Debit, PendingCredit = x.PendingCredit, PendingDebit = x.PendingDebit }).ToList();
        }
        var company = await _context.CompanySettings.AsNoTracking().FirstOrDefaultAsync();
        return (owner, company, report);
    }

    private async Task<CustomerActivityReportDto> LoadActivityRowsAsync(long accountId, ExportFilterDto filter)
    {
        var ledger = _context.LedgerEntries.AsNoTracking().Where(e => e.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(filter.CurrencyCode))
            ledger = ledger.Where(e => e.Currency!.Code == filter.CurrencyCode.Trim());
        var opening = filter.FromDate.HasValue
            ? await ledger.Where(e => e.CreatedAt < filter.FromDate.Value)
                .GroupBy(e => e.Currency!.Code)
                .Select(g => new CustomerActivityBalanceDto { Currency = g.Key, Opening = g.Sum(e => e.TalabKar - e.BadehKar) }).ToListAsync()
            : new List<CustomerActivityBalanceDto>();
        if (filter.FromDate.HasValue) ledger = ledger.Where(e => e.CreatedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) ledger = ledger.Where(e => e.CreatedAt <= filter.ToDate.Value);
        var entries = await ledger
            .Include(e => e.Account).Include(e => e.Currency).Include(e => e.Hawala)
            .Include(e => e.Transfer!).ThenInclude(t => t!.FromAccount)
            .Include(e => e.Transfer!).ThenInclude(t => t!.ToAccount)
            .Include(e => e.MoneyExchangeOperation!).ThenInclude(m => m!.FromAccount)
            .Include(e => e.MoneyExchangeOperation!).ThenInclude(m => m!.ToAccount)
            .Include(e => e.Transaction)
            .Include(e => e.AccountMoneyOperation!).ThenInclude(m => m!.CashOrBankAccount)
            .Include(e => e.CapitalInvestment).Include(e => e.Expense)
            .Where(e => e.AccountId == accountId)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync();

        var rows = new List<CustomerActivityRowDto>(entries.Count);
        foreach (var entry in entries)
        {
            var typeCode = ResolveTypeCode(entry);

            rows.Add(new CustomerActivityRowDto
            {
                HawalaId = entry.HawalaId,
                AccountMoneyOperationId = entry.AccountMoneyOperationId,
                Date = DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc),
                Type = typeCode,
                Reference = ResolveReference(entry),
                TalabKar = entry.TalabKar,
                BadehKar = entry.BadehKar,
                Currency = entry.Currency?.Code ?? string.Empty,
                AccountFrom = ResolveFromAccount(entry),
                AccountTo = ResolveToAccount(entry),
                Description = ResolveDescription(entry)
            });
        }
        CalculateRunningBalances(rows, opening);
        foreach (var row in rows) row.DisplayDescription = BuildPlainDescription(row);
        await ApplyHawalaDescriptionsAsync(rows);
        await ApplyAccountOperationDescriptionsAsync(rows);
        var summaries = opening.ToDictionary(x => x.Currency, StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows.GroupBy(x => x.Currency))
        {
            if (!summaries.TryGetValue(group.Key, out var summary)) summaries[group.Key] = summary = new() { Currency = group.Key };
            summary.Debit = group.Sum(x => x.BadehKar);
            summary.Credit = group.Sum(x => x.TalabKar);
        }
        var search = filter.Search?.Trim() ?? "";
        var selected = rows.Where(row => (!filter.FromDate.HasValue || row.Date >= filter.FromDate) &&
            (!filter.ToDate.HasValue || row.Date <= filter.ToDate) &&
            (filter.TransactionTypes is not { Count: > 0 } || filter.TransactionTypes.Contains(row.Type)) &&
            (string.IsNullOrWhiteSpace(filter.CurrencyCode) || row.Currency.Equals(filter.CurrencyCode.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (search == "" || (row.Reference?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) || row.DisplayDescription.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(filter.Direction) || (filter.Direction == "Credit" ? row.TalabKar > 0 : row.BadehKar > 0)) &&
            (!filter.MinAmount.HasValue || Math.Max(row.TalabKar, row.BadehKar) >= filter.MinAmount) &&
            (!filter.MaxAmount.HasValue || Math.Max(row.TalabKar, row.BadehKar) <= filter.MaxAmount)).ToList();
        return new() { Rows = selected, Balances = summaries.Values.OrderBy(x => x.Currency).ToList() };
    }

    private static void CalculateRunningBalances(IEnumerable<CustomerActivityRowDto> rows, IEnumerable<CustomerActivityBalanceDto> opening)
    {
        var balances = opening.ToDictionary(x => x.Currency, x => x.Opening, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            balances.TryGetValue(row.Currency, out var balance);
            balance += row.TalabKar - row.BadehKar;
            balances[row.Currency] = balance;
            row.RunningBalance = balance;
        }
    }

    private static (byte[] Content, string ContentType, string FileName) BuildExcelReport(
        Customer customer, CompanySetting? company, CustomerActivityReportDto report)
    {
        var rows = report.Rows;
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add($"صورت حساب {report.OwnerType}");
        worksheet.RightToLeft = true;
        worksheet.Style.Font.FontName = "Arial";
        worksheet.Style.Font.FontSize = 10;
        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        worksheet.Range(1, 1, 1, ReportColumnCount).Merge();
        worksheet.Cell(1, 1).Value = CompanyName(company);
        worksheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16);
        worksheet.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        worksheet.Row(1).Height = 27;

        worksheet.Range(2, 1, 2, 4).Merge();
        worksheet.Cell(2, 1).Value = $"صورت حساب {report.OwnerType}: {customer.FullName} - کد {customer.CustomerCode}";
        worksheet.Cell(2, 1).Style.Font.SetBold();
        worksheet.Cell(2, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        worksheet.Range(2, 5, 2, ReportColumnCount).Merge();
        worksheet.Cell(2, 5).Value = $"تاریخ تهیه: {FormatDate(DateTime.Now, company)}";
        worksheet.Cell(2, 5).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);

        worksheet.Range(3, 1, 3, ReportColumnCount).Merge();
        worksheet.Cell(3, 1).Value = ReportRange(report, company);
        worksheet.Cell(3, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        var headerRowNumber = 4;
        foreach (var balance in report.Balances)
        {
            worksheet.Range(headerRowNumber, 1, headerRowNumber, 6).Merge();
            worksheet.Cell(headerRowNumber, 1).Value = $"مانده قبل از شروع بازه - {balance.Currency}";
            worksheet.Cell(headerRowNumber, 7).Value = Math.Abs(balance.Opening);
            worksheet.Cell(headerRowNumber, 8).Value = BalanceStatus(balance.Opening);
            worksheet.Range(headerRowNumber, 1, headerRowNumber, ReportColumnCount).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EDF4FF"));
            worksheet.Cell(headerRowNumber, 7).Style.NumberFormat.Format = "#,##0";
            headerRowNumber++;
        }
        var headers = new[] { "ردیف", "تاریخ", "شرح", "بدهکار", "طلبکار", "نوع ارز", "باقی‌مانده", "وضعیت" };
        for (var column = 1; column <= headers.Length; column++)
            worksheet.Cell(headerRowNumber, column).Value = headers[column - 1];

        var headerRange = worksheet.Range(headerRowNumber, 1, headerRowNumber, ReportColumnCount);
        headerRange.Style.Font.SetBold();
        headerRange.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E7E7E7"));
        headerRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        worksheet.Row(headerRowNumber).Height = 23;

        var currentRow = headerRowNumber + 1;
        var sequence = 1;
        foreach (var row in rows)
        {
            worksheet.Cell(currentRow, 1).Value = sequence++;
            worksheet.Cell(currentRow, 2).Value = FormatDate(row.Date, company);
            worksheet.Cell(currentRow, 3).Value = BuildPlainDescription(row);
            if (row.MoneyOperationType is "Deposit" or "Withdraw")
            {
                var name = AccountOperationDisplay.Name(row.MoneyOperationType);
                var text = BuildPlainDescription(row);
                var richText = worksheet.Cell(currentRow, 3).GetRichText();
                richText.ClearText();
                richText.AddText(name).SetBold().SetFontColor(XLColor.FromHtml(row.MoneyOperationType == "Deposit" ? "#1a7a3a" : "#dc3545"));
                richText.AddText(text[name.Length..]);
            }
            if (row.BadehKar != 0) worksheet.Cell(currentRow, 4).Value = row.BadehKar;
            if (row.TalabKar != 0) worksheet.Cell(currentRow, 5).Value = row.TalabKar;
            worksheet.Cell(currentRow, 6).Value = row.Currency;
            worksheet.Cell(currentRow, 7).Value = Math.Abs(row.RunningBalance);
            worksheet.Cell(currentRow, 8).Value = BalanceStatus(row.RunningBalance);
            currentRow++;
        }

        if (rows.Count == 0)
        {
            worksheet.Range(currentRow, 1, currentRow, ReportColumnCount).Merge();
            worksheet.Cell(currentRow, 1).Value = "در بازه و فیلتر انتخاب‌شده فعالیتی یافت نشد.";
            worksheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            currentRow++;
        }

        var dataEndRow = currentRow - 1;
        var tableRange = worksheet.Range(headerRowNumber, 1, dataEndRow, ReportColumnCount);
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Alignment.WrapText = true;
        worksheet.Range(headerRowNumber + 1, 1, dataEndRow, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        worksheet.Range(headerRowNumber + 1, 4, dataEndRow, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        worksheet.Range(headerRowNumber + 1, 4, dataEndRow, 7).Style.NumberFormat.Format = "#,##0";

        foreach (var summary in report.Balances)
        {
            var totalTalabKar = summary.Credit;
            var totalBadehKar = summary.Debit;
            var balance = summary.Closing;
            worksheet.Range(currentRow, 1, currentRow, 3).Merge();
            worksheet.Cell(currentRow, 1).Value = $"جمع بازه و مانده پایان {summary.Currency}";
            worksheet.Cell(currentRow, 4).Value = totalBadehKar;
            worksheet.Cell(currentRow, 5).Value = totalTalabKar;
            worksheet.Cell(currentRow, 6).Value = summary.Currency;
            worksheet.Cell(currentRow, 7).Value = Math.Abs(balance);
            worksheet.Cell(currentRow, 8).Value = BalanceStatus(balance);
            var totalRange = worksheet.Range(currentRow, 1, currentRow, ReportColumnCount);
            totalRange.Style.Font.SetBold();
            totalRange.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F2F2F2"));
            totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            totalRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            totalRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            worksheet.Range(currentRow, 4, currentRow, 7).Style.NumberFormat.Format = "#,##0";
            ApplyFinalBalanceStyle(worksheet.Range(currentRow, 7, currentRow, 8), balance);
            currentRow++;
        }

        if (report.CorrespondentId.HasValue)
        {
            foreach (var summary in report.Balances)
            {
                worksheet.Range(currentRow, 1, currentRow, 3).Merge();
                worksheet.Cell(currentRow, 1).Value = "کمیشن منتظر بستن دوره (خارج از مانده)";
                worksheet.Cell(currentRow, 4).Value = summary.PendingDebit;
                worksheet.Cell(currentRow, 5).Value = summary.PendingCredit;
                worksheet.Cell(currentRow, 6).Value = summary.Currency;
                worksheet.Range(currentRow, 1, currentRow, ReportColumnCount).Style.Alignment.WrapText = true;
                worksheet.Range(currentRow, 4, currentRow, 5).Style.NumberFormat.Format = "#,##0";
                worksheet.Row(currentRow).Height = 30;
                currentRow++;
            }
        }
        worksheet.Range(currentRow, 1, currentRow, ReportColumnCount).Merge();
        worksheet.Cell(currentRow, 1).Value = "خلاصه حساب شامل تمام فعالیت‌های بازه است؛ فیلترهای جزئی فقط ردیف‌های جدول را محدود می‌کنند.";
        worksheet.Row(currentRow).Height = 30;
        worksheet.Range(currentRow, 1, currentRow, ReportColumnCount).Style.Alignment.WrapText = true;
        currentRow++;
        foreach (var summary in report.Balances)
        {
            worksheet.Range(currentRow, 1, currentRow, 6).Merge();
            worksheet.Cell(currentRow, 1).Value = $"وضعیت نهایی حساب - {summary.Currency}";
            worksheet.Cell(currentRow, 7).Value = Math.Abs(summary.Closing);
            worksheet.Cell(currentRow, 8).Value = BalanceStatus(summary.Closing);
            var finalRange = worksheet.Range(currentRow, 1, currentRow, ReportColumnCount);
            finalRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            finalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            finalRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ApplyFinalBalanceStyle(finalRange, summary.Closing);
            worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0";
            worksheet.Row(currentRow).Height = 26;
            currentRow++;
        }
        worksheet.Column(1).Width = 7; worksheet.Column(2).Width = 14; worksheet.Column(3).Width = 46;
        worksheet.Column(4).Width = 15; worksheet.Column(5).Width = 15; worksheet.Column(6).Width = 11;
        worksheet.Column(7).Width = 16; worksheet.Column(8).Width = 12;
        worksheet.SheetView.FreezeRows(headerRowNumber);
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.PagesWide = 1;
        worksheet.PageSetup.PagesTall = 0;
        worksheet.PageSetup.SetRowsToRepeatAtTop(headerRowNumber, headerRowNumber);
        worksheet.PageSetup.Margins.Top = 0.45; worksheet.PageSetup.Margins.Bottom = 0.55;
        worksheet.PageSetup.Margins.Left = 0.25; worksheet.PageSetup.Margins.Right = 0.25;
        worksheet.PageSetup.Margins.Header = 0.15; worksheet.PageSetup.Margins.Footer = 0.2;
        var footerText = BuildContactLine(company);
        if (!string.IsNullOrWhiteSpace(footerText)) worksheet.PageSetup.Footer.Center.AddText(footerText);
        worksheet.PageSetup.Footer.Right.AddText("صفحه ");
        worksheet.PageSetup.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
        worksheet.PageSetup.Footer.Right.AddText(" از ");
        worksheet.PageSetup.Footer.Right.AddText(XLHFPredefinedText.NumberOfPages);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"صورت-حساب-{SafeFileName(customer.FullName)}-{DateTime.Now:yyyyMMddHHmmss}.xlsx";
        return (stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private (byte[] Content, string ContentType, string FileName) BuildPdfReport(
        Customer customer, CompanySetting? company, CustomerActivityReportDto report)
    {
        var document = new HtmlToPdfDocument
        {
            GlobalSettings =
            {
                PaperSize = PaperKind.A4, Orientation = Orientation.Portrait,
                DocumentTitle = $"صورت حساب {customer.FullName}",
                Margins = new MarginSettings { Top = 9, Bottom = 16, Left = 7, Right = 7 }
            },
            Objects =
            {
                new ObjectSettings
                {
                    HtmlContent = BuildPdfHtml(customer, company, report), PagesCount = true,
                    WebSettings = new WebSettings { DefaultEncoding = "utf-8", PrintMediaType = true, EnableIntelligentShrinking = true },
                    FooterSettings = new FooterSettings
                    {
                        FontName = "Arial", FontSize = 8, Left = BuildContactLine(company),
                        Right = "صفحه [page] از [toPage]", Line = true, Spacing = 4
                    }
                }
            }
        };
        var pdf = _pdfConverter.Convert(document);
        var fileName = $"صورت-حساب-{SafeFileName(customer.FullName)}-{DateTime.Now:yyyyMMddHHmmss}.pdf";
        return (pdf, "application/pdf", fileName);
    }

    private static string BuildPdfHtml(Customer customer, CompanySetting? company, CustomerActivityReportDto report)
    {
        var rows = report.Rows;
        var builder = new StringBuilder();
        builder.Append("""
            <!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><style>
            *{box-sizing:border-box}body{margin:0;color:#111;direction:rtl;font-family:Tahoma,Arial,sans-serif;font-size:9px}
            .company{margin:0 0 8px;text-align:center;font-size:17px;font-weight:700}.meta{width:100%;margin-bottom:7px;border-collapse:collapse}
            .meta td{width:50%;border:0;padding:1px 0 4px;font-size:10px;font-weight:700}.meta .date{direction:rtl;text-align:left;font-weight:400}
            table.report{width:100%;border-collapse:collapse;table-layout:fixed}.report thead{display:table-header-group}.report tr{page-break-inside:avoid}
            .report th,.report td{border:1px solid #333;padding:4px 3px;vertical-align:middle}.report th{background:#e7e7e7;text-align:center;font-weight:700}
            .center{text-align:center}.number{direction:ltr;text-align:center;white-space:nowrap}.description{text-align:right;line-height:1.55;overflow-wrap:anywhere}
            .empty{padding:14px!important;text-align:center}.total td{background:#f2f2f2;font-weight:700}
            .w-seq{width:5%}.w-date{width:11%}.w-desc{width:36%}.w-money{width:11%}.w-currency{width:8%}.w-balance{width:11%}.w-status{width:7%}
            </style></head><body>
            """);
        builder.Append($"<h1 class='company'>{Encode(CompanyName(company))}</h1><table class='meta'><tr>");
        builder.Append($"<td>صورت حساب {report.OwnerType}: {Encode(customer.FullName)} - کد {Encode(customer.CustomerCode)}</td>");
        builder.Append($"<td class='date'>تاریخ تهیه: {Encode(FormatDate(DateTime.Now, company))}</td></tr></table>");
        builder.Append($"<p class='center'>{Encode(ReportRange(report, company))}</p><table class='report'>");
        foreach (var balance in report.Balances)
            builder.Append($"<tr class='total'><td>مانده قبل از شروع بازه - {Encode(balance.Currency)}</td><td class='number'>{FormatMoney(Math.Abs(balance.Opening))}</td><td class='center'>{BalanceStatus(balance.Opening)}</td></tr>");
        if (report.CorrespondentId.HasValue)
            foreach (var balance in report.Balances)
                builder.Append($"<tr><td>کمیشن منتظر بستن دوره (خارج از مانده) - {Encode(balance.Currency)}</td><td class='number'>بدهکار: {FormatMoney(balance.PendingDebit)}</td><td class='number'>طلبکار: {FormatMoney(balance.PendingCredit)}</td></tr>");
        builder.Append("</table><p>خلاصه حساب شامل تمام فعالیت‌های بازه است؛ فیلترهای جزئی فقط ردیف‌های جدول را محدود می‌کنند.</p>");
        builder.Append("""
            <table class="report"><thead><tr><th class="w-seq">ردیف</th><th class="w-date">تاریخ</th>
            <th class="w-desc">شرح</th><th class="w-money">بدهکار</th><th class="w-money">طلبکار</th>
            <th class="w-currency">نوع ارز</th><th class="w-balance">باقی‌مانده</th><th class="w-status">وضعیت</th>
            </tr></thead><tbody>
            """);
        var sequence = 1;
        foreach (var row in rows)
        {
            builder.Append($"<tr><td class='center'>{sequence++}</td><td class='number'>{Encode(FormatDate(row.Date, company))}</td>");
            builder.Append($"<td class='description'>{BuildPdfDescription(row)}</td><td class='number'>{FormatMoney(row.BadehKar)}</td>");
            builder.Append($"<td class='number'>{FormatMoney(row.TalabKar)}</td><td class='center'>{Encode(row.Currency)}</td>");
            builder.Append($"<td class='number'>{FormatMoney(Math.Abs(row.RunningBalance))}</td><td class='center'>{BalanceStatus(row.RunningBalance)}</td></tr>");
        }
        if (rows.Count == 0) builder.Append("<tr><td colspan='8' class='empty'>در بازه و فیلتر انتخاب‌شده فعالیتی یافت نشد.</td></tr>");
        foreach (var summary in report.Balances)
        {
            var totalTalabKar = summary.Credit;
            var totalBadehKar = summary.Debit;
            var balance = summary.Closing;
            builder.Append($"<tr class='total'><td colspan='3' class='center'>جمع بازه و مانده پایان {Encode(summary.Currency)}</td>");
            builder.Append($"<td class='number'>{FormatMoney(totalBadehKar)}</td><td class='number'>{FormatMoney(totalTalabKar)}</td>");
            builder.Append($"<td class='center'>{Encode(summary.Currency)}</td><td class='number' style='{FinalBalanceCss(balance)}'>{FormatMoney(Math.Abs(balance))}</td>");
            builder.Append($"<td class='center' style='{FinalBalanceCss(balance)}'>{BalanceStatus(balance)}</td></tr>");
        }
        builder.Append("</tbody></table><h3>وضعیت نهایی حساب</h3><table class='report'>");
        foreach (var summary in report.Balances)
            builder.Append($"<tr><td class='center' style='{FinalBalanceCss(summary.Closing)}'>{Encode(summary.Currency)}</td><td class='number' style='{FinalBalanceCss(summary.Closing)}'>{FormatMoney(Math.Abs(summary.Closing))}</td><td class='center' style='{FinalBalanceCss(summary.Closing)}'>{BalanceStatus(summary.Closing)}</td></tr>");
        builder.Append("</table></body></html>");
        return builder.ToString();
    }

    private async Task ApplyHawalaDescriptionsAsync(IReadOnlyCollection<CustomerActivityRowDto> rows)
    {
        var ids = rows.Where(x => x.HawalaId.HasValue).Select(x => x.HawalaId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return;
        var records = new Dictionary<long, SelectedRecordDto>();
        var reader = new SelectedRecordsService(_context);
        foreach (var chunk in ids.Chunk(10000))
            foreach (var record in await reader.ReadAsync(chunk)) records[record.Id] = record;
        foreach (var row in rows)
        {
            if (!row.HawalaId.HasValue || !records.TryGetValue(row.HawalaId.Value, out var hawala)) continue;
            var kind = hawala.Kind == "HawalaReceive" ? "حواله دریافتی" : hawala.Kind == "HawalaSend" ? "حواله ارسالی" : "حواله";
            static string Name(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
            var description = $"{kind} از {Name(hawala.SenderName)} به {Name(hawala.ReceiverName)}؛ محل پرداخت: {Name(hawala.PaymentLocationName)}";
            var note = hawala.Notes.Trim();
            if (hawala.SourceHawalaId.HasValue && hawala.Kind == "HawalaSend")
                note = string.Join(Environment.NewLine, note.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !line.StartsWith("حواله ارسالی خودکار بابت پرداخت حواله دریافتی شماره", StringComparison.Ordinal))).Trim();
            row.DisplayDescription = note.Length == 0 ? description : $"{description}؛ یادداشت: {note}";
        }
    }

    private async Task ApplyAccountOperationDescriptionsAsync(IReadOnlyCollection<CustomerActivityRowDto> rows)
    {
        var ids = rows.Where(x => x.AccountMoneyOperationId.HasValue).Select(x => x.AccountMoneyOperationId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return;
        var sources = await _context.AccountMoneyOperations.AsNoTracking().Include(x => x.Account).Include(x => x.CashOrBankAccount)
            .Include(x => x.Currency).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        foreach (var row in rows)
        {
            if (!row.AccountMoneyOperationId.HasValue || !sources.TryGetValue(row.AccountMoneyOperationId.Value, out var source)) continue;
            row.MoneyOperationType = source.OperationType;
            row.DisplayDescription = AccountOperationDisplay.Description(source.OperationType, source.Account.AccountName,
                source.CashOrBankAccount.AccountName, source.Amount, source.Currency.Code, source.Description);
        }
    }

    private static string BuildPlainDescription(CustomerActivityRowDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.DisplayDescription)) return row.DisplayDescription;
        var parts = new List<string>();
        var type = AppDisplayText.TransactionType(row.Type);
        if (!string.IsNullOrWhiteSpace(type)) parts.Add(type);
        if (!string.IsNullOrWhiteSpace(row.Reference)) parts.Add(row.Reference);
        if (!string.IsNullOrWhiteSpace(row.AccountFrom) && !string.IsNullOrWhiteSpace(row.AccountTo)) parts.Add($"از حساب {row.AccountFrom} به حساب {row.AccountTo}");
        else if (!string.IsNullOrWhiteSpace(row.AccountFrom)) parts.Add($"از حساب {row.AccountFrom}");
        else if (!string.IsNullOrWhiteSpace(row.AccountTo)) parts.Add($"به حساب {row.AccountTo}");
        if (!string.IsNullOrWhiteSpace(row.Description) && !parts.Contains(row.Description)) parts.Add(row.Description);
        return parts.Count == 0 ? "—" : string.Join(" - ", parts);
    }

    private static string BuildPdfDescription(CustomerActivityRowDto row)
    {
        var text = BuildPlainDescription(row);
        if (row.MoneyOperationType is not ("Deposit" or "Withdraw")) return Encode(text);
        var name = AccountOperationDisplay.Name(row.MoneyOperationType);
        var background = row.MoneyOperationType == "Deposit" ? "#e6f7e6" : "#fde8e8";
        var color = row.MoneyOperationType == "Deposit" ? "#1a7a3a" : "#dc3545";
        return $"<span style='background:{background};color:{color};padding:2px 5px;border-radius:4px;font-weight:bold'>{name}</span>{Encode(text[name.Length..])}";
    }

    private static string CompanyName(CompanySetting? company) => string.IsNullOrWhiteSpace(company?.CompanyName) ? "نام شرکت" : company.CompanyName.Trim();
    private static string BuildContactLine(CompanySetting? company)
    {
        if (company == null) return string.Empty;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(company.Address)) parts.Add(company.Address.Trim());
        if (!string.IsNullOrWhiteSpace(company.PhoneNumber)) parts.Add($"تلفن: {company.PhoneNumber.Trim()}");
        if (!string.IsNullOrWhiteSpace(company.WhatsAppNumber) && company.WhatsAppNumber != company.PhoneNumber) parts.Add($"واتساپ: {company.WhatsAppNumber.Trim()}");
        return string.Join(" | ", parts);
    }

    private static string BalanceStatus(decimal balance) => balance > 0 ? "طلبکار" : balance < 0 ? "بدهکار" : "تسویه";
    private static string FinalBalanceCss(decimal balance) => balance > 0 ? "background:#e6f7e6;color:#1a7a3a;font-weight:bold" : balance < 0 ? "background:#fde8e8;color:#dc3545;font-weight:bold" : "background:#f2f2f2;color:#495057;font-weight:bold";
    private static void ApplyFinalBalanceStyle(IXLRange range, decimal balance)
    {
        range.Style.Font.SetBold();
        range.Style.Fill.SetBackgroundColor(XLColor.FromHtml(balance > 0 ? "#e6f7e6" : balance < 0 ? "#fde8e8" : "#f2f2f2"));
        range.Style.Font.SetFontColor(XLColor.FromHtml(balance > 0 ? "#1a7a3a" : balance < 0 ? "#dc3545" : "#495057"));
    }
    private static string FormatMoney(decimal amount) => AmountValueHelper.Format(amount, 0);
    private static string ReportRange(CustomerActivityReportDto report, CompanySetting? company) =>
        $"از تاریخ: {(report.FromDate.HasValue ? FormatDate(DateTime.SpecifyKind(report.FromDate.Value, DateTimeKind.Utc), company) : "ابتدای حساب")}   تا تاریخ: {(report.ToDate.HasValue ? FormatDate(DateTime.SpecifyKind(report.ToDate.Value, DateTimeKind.Utc), company) : "آخرین فعالیت")}";
    private static string FormatDate(DateTime date, CompanySetting? company)
    {
        var formatter = new DisplayDateFormatter();
        formatter.SetCalendar(company?.UsePersianCalendar ?? true);
        return formatter.Date(date);
    }
    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string SafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalidCharacters.Contains(c) ? '-' : c).ToArray()).Trim();
    }

    private static string ResolveTypeCode(LedgerEntry e)
    {
        if (e.HawalaId != null) return string.IsNullOrEmpty(e.Hawala?.HawalaType) ? "HawalaOther" : e.Hawala.HawalaType;
        if (e.TransferId != null) return "Transfer";
        if (e.MoneyExchangeOperationId != null) return "MoneyExchange";
        if (e.CapitalInvestmentId != null) return e.CapitalInvestment?.IsWithdrawal == true ? "OwnerWithdrawal" : "CapitalInvestment";
        if (e.ExpenseId != null) return "Expense";
        if (e.AccountMoneyOperationId != null) return e.AccountMoneyOperation?.OperationType ?? string.Empty;
        if (e.TransactionId != null) return e.Transaction?.TransactionType ?? string.Empty;
        return string.Empty;
    }
    private static string? ResolveReference(LedgerEntry e)
    {
        if (e.HawalaId != null) return e.Hawala != null ? $"حواله شماره {e.Hawala.Number}" : $"حواله شماره {e.HawalaId}";
        if (e.TransferId != null) return e.Transfer?.ReferenceNumber ?? $"انتقال شماره {e.TransferId}";
        if (e.MoneyExchangeOperationId != null) return $"تبدیل ارز شماره {e.MoneyExchangeOperationId}";
        if (e.CapitalInvestmentId != null) return $"{(e.CapitalInvestment?.IsWithdrawal == true ? "برداشت مالک" : "سرمایه")} شماره {e.CapitalInvestmentId}";
        if (e.ExpenseId != null) return $"مصرف شماره {e.ExpenseId}";
        if (e.AccountMoneyOperationId != null) return $"عملیات شماره {e.AccountMoneyOperationId}";
        if (e.TransactionId != null) return e.Transaction?.TransactionNo;
        return null;
    }
    private static string? ResolveFromAccount(LedgerEntry e)
    {
        if (e.Transfer != null) return e.Transfer.FromAccount?.AccountName;
        if (e.MoneyExchangeOperation != null) return e.MoneyExchangeOperation.FromAccount.AccountName;
        if (e.AccountMoneyOperation != null) return e.AccountMoneyOperation.OperationType == "Deposit" ? e.Account?.AccountName : e.AccountMoneyOperation.CashOrBankAccount?.AccountName;
        return null;
    }
    private static string? ResolveToAccount(LedgerEntry e)
    {
        if (e.Transfer != null) return e.Transfer.ToAccount?.AccountName;
        if (e.MoneyExchangeOperation != null) return e.MoneyExchangeOperation.ToAccount.AccountName;
        if (e.AccountMoneyOperation != null) return e.AccountMoneyOperation.OperationType == "Deposit" ? e.AccountMoneyOperation.CashOrBankAccount?.AccountName : e.Account?.AccountName;
        return null;
    }
    private static string? ResolveDescription(LedgerEntry e)
    {
        if (!string.IsNullOrWhiteSpace(e.Description)) return e.Description;
        if (e.Hawala != null) return string.Join(" به ", new[] { e.Hawala.SenderName, e.Hawala.ReceiverName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return null;
    }

}
