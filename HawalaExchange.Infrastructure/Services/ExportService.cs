using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DinkToPdf;
using DinkToPdf.Contracts;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;

namespace HawalaExchange.Infrastructure.Services
{
    public class ExportService : IExportService
    {
        private readonly ITransactionService _transactionService;
        private readonly IMoneyExchangeOperationService _moneyExchangeService;
        private readonly IHawalaService _hawalaService;
        private readonly IAccountService _accountService;
        private readonly IConverter _pdfConverter;

        public ExportService(
            ITransactionService transactionService,
            IMoneyExchangeOperationService moneyExchangeService,
            IHawalaService hawalaService,
            IAccountService accountService,
            IConverter pdfConverter)
        {
            _transactionService = transactionService;
            _moneyExchangeService = moneyExchangeService;
            _hawalaService = hawalaService;
            _accountService = accountService;
            _pdfConverter = pdfConverter;
        }

        public async Task<(byte[] Content, string ContentType, string FileName)> ExportCustomerActivitiesAsync(ExportFilterDto filter, ExportFormat format)
        {
            if (!filter.CustomerId.HasValue)
                throw new ArgumentException("CustomerId is required.");

            var rows = new List<ActivityRow>();

            // Transactions
            var transactions = await _transactionService.GetByCustomerAsync(filter.CustomerId.Value);
            foreach (var t in transactions)
            {
                if (filter.FromDate.HasValue && t.CreatedAt < filter.FromDate.Value) continue;
                if (filter.ToDate.HasValue && t.CreatedAt > filter.ToDate.Value) continue;
                if (filter.TransactionTypes != null && filter.TransactionTypes.Any() && !filter.TransactionTypes.Contains(t.TransactionType)) continue;

                // prefer detail amounts if available
                var detail = t.TransactionDetails?.FirstOrDefault();
                decimal amount = 0;
                string currency = string.Empty;
                if (detail != null)
                {
                    amount = detail.FromAmount ?? detail.ToAmount ?? 0;
                    currency = detail.FromCurrencyCode ?? detail.ToCurrencyCode ?? string.Empty;
                }
                else if (t.LedgerEntries != null && t.LedgerEntries.Any())
                {
                    var entry = t.LedgerEntries.First();
                    amount = entry.TalabKar != 0 ? entry.TalabKar : entry.BadehKar;
                    currency = entry.CurrencyCode;
                }

                rows.Add(new ActivityRow
                {
                    Date = t.CreatedAt,
                    Type = t.TransactionType,
                    Reference = t.TransactionNo,
                    Amount = amount,
                    Currency = currency,
                    AccountFrom = t.TransactionDetails?.FirstOrDefault()?.SenderName,
                    AccountTo = t.TransactionDetails?.FirstOrDefault()?.ReceiverName,
                    Description = t.Remarks
                });
            }

            // Money exchange operations
            var exchanges = await _moneyExchangeService.GetAllAsync();
            // determine account ids for customer
            var customerAccounts = (await _accountService.GetByReferenceAsync("Customer", filter.CustomerId.Value)).Select(a => a.Id).ToHashSet();
            foreach (var ex in exchanges)
            {
                if (filter.FromDate.HasValue && ex.ExchangeDate < filter.FromDate.Value) continue;
                if (filter.ToDate.HasValue && ex.ExchangeDate > filter.ToDate.Value) continue;

                if (!customerAccounts.Contains(ex.FromAccountId) && !customerAccounts.Contains(ex.ToAccountId))
                    continue;

                rows.Add(new ActivityRow
                {
                    Date = ex.ExchangeDate,
                    Type = "MoneyExchange",
                    Reference = ex.Id.ToString(),
                    Amount = ex.FromAmount,
                    Currency = ex.FromCurrencyCode,
                    AccountFrom = ex.FromAccountName,
                    AccountTo = ex.ToAccountName,
                    Description = ex.Description
                });
            }

            // Hawalas
            var hawalaFilter = new HawalaFilterDto
            {
                FromDate = filter.FromDate,
                ToDate = filter.ToDate
            };
            var hawalaList = await _hawalaService.GetHawalasAsync(hawalaFilter);
            foreach (var h in hawalaList.Items)
            {
                // if customer is involved as sender or receiver, try to match via names or accounts
                bool involved = false;
                if (h.SenderName != null || h.ReceiverName != null)
                {
                    // best-effort: check accounts
                    if (h.FromAccountId.HasValue && customerAccounts.Contains(h.FromAccountId.Value)) involved = true;
                }
                if (!involved)
                {
                    // skip if not clearly related
                    continue;
                }

                rows.Add(new ActivityRow
                {
                    Date = h.CreatedAt,
                    Type = h.HawalaType,
                    Reference = h.Number.ToString(),
                    Amount = h.FromAmount,
                    Currency = h.FromCurrencyCode,
                    AccountFrom = h.FromAccountId.HasValue ? h.FromAccountId.ToString() : null,
                    AccountTo = h.ReceiverName,
                    Description = h.ReferenceNumber ?? h.Notes
                });
            }

            // order
            rows = rows.OrderByDescending(r => r.Date).ToList();

            if (format == ExportFormat.Excel)
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Activities");
                var header = new[] { "تاریخ", "نوع", "مرجع", "مبلغ", "واحد پول", "از حساب", "به حساب", "توضیحات" };
                for (int i = 0; i < header.Length; i++) ws.Cell(1, i + 1).Value = header[i];

                // Header styling
                var headerRow = ws.Row(1);
                headerRow.Style.Font.SetBold(true);
                headerRow.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F3F4F6"));
                headerRow.Style.Alignment.SetHorizontal(XLHelper.HorizontalAlignmentValues.Right);

                // Freeze top row
                ws.SheetView.FreezeRows(1);

                int r = 2;
                foreach (var row in rows)
                {
                    ws.Cell(r, 1).Value = row.Date;
                    ws.Cell(r, 2).Value = row.Type;
                    ws.Cell(r, 3).Value = row.Reference;
                    ws.Cell(r, 4).Value = row.Amount;
                    ws.Cell(r, 5).Value = row.Currency;
                    ws.Cell(r, 6).Value = row.AccountFrom;
                    ws.Cell(r, 7).Value = row.AccountTo;
                    ws.Cell(r, 8).Value = row.Description;
                    r++;
                }

                // Column formats and widths
                ws.Column(1).Width = 20; // Date
                ws.Column(1).Style.DateFormat.Format = "yyyy/MM/dd HH:mm";
                ws.Column(4).Width = 16; // Amount
                ws.Column(4).Style.NumberFormat.Format = "#,##0.00";
                ws.Column(8).Width = 40; // Description

                // Right-to-left layout for better Persian readability
                ws.Style.Alignment.SetHorizontal(XLHelper.HorizontalAlignmentValues.Right);

                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                var bytes = ms.ToArray();
                return (bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"customer-activities-{filter.CustomerId}-{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx");
            }
            else
            {
                // build simple HTML
                var sb = new StringBuilder();
                sb.Append("<html><head><meta charset='utf-8'><style>table{width:100%;border-collapse:collapse}th,td{border:1px solid #ccc;padding:6px;text-align:right}</style></head><body>");
                sb.Append($"<h3>Customer activities - {filter.CustomerId}</h3>");
                sb.Append("<table><thead><tr><th>Date</th><th>Type</th><th>Reference</th><th>Amount</th><th>Currency</th><th>From</th><th>To</th><th>Description</th></tr></thead><tbody>");
                foreach (var row in rows)
                {
                    sb.Append("<tr>");
                    sb.Append($"<td>{row.Date:yyyy-MM-dd HH:mm}</td>");
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Type)}</td>");
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Reference)}</td>");
                    sb.Append($"<td>{row.Amount}</td>");
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Currency)}</td>");
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.AccountFrom)}</td>");
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.AccountTo)}</td>");
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Description)}</td>");
                    sb.Append("</tr>");
                }
                sb.Append("</tbody></table></body></html>");

                var doc = new HtmlToPdfDocument()
                {
                    GlobalSettings = { PaperSize = PaperKind.A4, Orientation = Orientation.Portrait },
                    Objects = { new ObjectSettings { HtmlContent = sb.ToString() } }
                };

                var pdf = _pdfConverter.Convert(doc);
                return (pdf, "application/pdf", $"customer-activities-{filter.CustomerId}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
            }
        }

        private class ActivityRow
        {
            public DateTime Date { get; set; }
            public string Type { get; set; } = string.Empty;
            public string? Reference { get; set; }
            public decimal Amount { get; set; }
            public string Currency { get; set; } = string.Empty;
            public string? AccountFrom { get; set; }
            public string? AccountTo { get; set; }
            public string? Description { get; set; }
        }
    }
}
