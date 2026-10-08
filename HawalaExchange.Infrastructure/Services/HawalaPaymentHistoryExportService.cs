using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.Infrastructure.Services;

public sealed class HawalaPaymentHistoryExportService(IHawalaService hawalas, ICompanySettingService companySettings, IConverter converter) : IHawalaPaymentHistoryExportService
{
    public async Task<SelectedRecordsFileDto> ExportAsync(long batchId, ExportFormat format)
    {
        var batch = await hawalas.GetPaymentBatchAsync(batchId);
        var company = await companySettings.GetAsync();
        string Date(DateTime date)
        {
            date = date.ToLocalTime();
            if (!company.UsePersianCalendar) return date.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
            var p = new PersianCalendar();
            return FormattableString.Invariant($"{p.GetYear(date):0000}/{p.GetMonth(date):00}/{p.GetDayOfMonth(date):00} {date:HH:mm}");
        }
        var name = $"HawalaPaymentBatch_{batch.Id}";
        if (format == ExportFormat.Excel)
        {
            using var workbook = new XLWorkbook();
            var summary = workbook.AddWorksheet("Summary"); summary.RightToLeft = true;
            summary.Cell(1, 1).Value = "اجرای گروهی"; summary.Cell(1, 2).Value = batch.Id;
            summary.Cell(2, 1).Value = "تاریخ اجرا"; summary.Cell(2, 2).Value = Date(batch.ExecutedAt);
            summary.Cell(3, 1).Value = "حساب پرداخت"; summary.Cell(3, 2).Value = batch.AccountName;
            summary.Cell(4, 1).Value = "اجراکننده"; summary.Cell(4, 2).Value = batch.ExecutedByName;
            summary.Cell(5, 1).Value = "تعداد حواله"; summary.Cell(5, 2).Value = batch.Count;
            var headers = new[] { "ارز", "اصل حواله", "کمیشن عامل پرداخت", "مجموع پرداخت" };
            for (var c = 0; c < headers.Length; c++) summary.Cell(7, c + 1).Value = headers[c];
            var row = 8;
            foreach (var total in batch.Totals)
            {
                summary.Cell(row, 1).Value = total.CurrencyCode; summary.Cell(row, 2).Value = total.Principal;
                summary.Cell(row, 3).Value = total.AgentCommission; summary.Cell(row++, 4).Value = total.Total;
            }
            summary.Row(7).Style.Font.Bold = true;
            foreach (var group in batch.Items.GroupBy(x => x.CurrencyCode))
            {
                var sheet = workbook.AddWorksheet($"Currency_{workbook.Worksheets.Count}"); sheet.RightToLeft = true;
                var headings = new[] { "نمبر حواله", "نمبر متفرقه", "فرستنده", "گیرنده", "نمایندگی", "محل پرداخت", "مبلغ پرداخت", "ارز", "کمیشن عامل پرداخت", "ارز کمیشن", "تاریخ ثبت" };
                for (var c = 0; c < headings.Length; c++) sheet.Cell(1, c + 1).Value = headings[c];
                row = 2;
                foreach (var item in group)
                {
                    sheet.Cell(row, 1).Value = item.Number; sheet.Cell(row, 2).Value = item.ReferenceNumber;
                    sheet.Cell(row, 3).Value = item.SenderName; sheet.Cell(row, 4).Value = item.ReceiverName;
                    sheet.Cell(row, 5).Value = item.CorrespondentName; sheet.Cell(row, 6).Value = item.PaymentLocation;
                    sheet.Cell(row, 7).Value = item.Amount; sheet.Cell(row, 8).Value = item.CurrencyCode;
                    sheet.Cell(row, 9).Value = item.AgentCommission; sheet.Cell(row, 10).Value = item.CommissionCurrencyCode;
                    sheet.Cell(row++, 11).Value = Date(item.RegisteredAt);
                }
                sheet.Cell(row, 6).Value = "مجموع اصل حواله"; sheet.Cell(row, 7).Value = group.Sum(x => x.Amount); sheet.Cell(row, 8).Value = group.Key;
                sheet.Row(1).Style.Font.Bold = true; sheet.Row(row).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1);
                sheet.Range(1, 1, row - 1, headings.Length).SetAutoFilter();
            }
            foreach (var sheet in workbook.Worksheets)
            {
                sheet.CellsUsed().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                sheet.CellsUsed().Style.NumberFormat.Format = "[$-en-US]#,##0";
                sheet.Columns().AdjustToContents(8, 40);
            }
            using var stream = new MemoryStream(); workbook.SaveAs(stream);
            return new(name + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", stream.ToArray());
        }
        if (format != ExportFormat.Pdf) throw new InvalidOperationException("نوع خروجی معتبر نیست.");
        static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
        static string N(decimal number) => number.ToString("N0", CultureInfo.InvariantCulture);
        var html = new StringBuilder("<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><style>body{font-family:Tahoma,Arial;font-size:11px}table{width:100%;border-collapse:collapse;margin-bottom:14px}td,th{border:1px solid #bbb;padding:5px;text-align:center}th{background:#eee}thead{display:table-header-group}tr{page-break-inside:avoid}.number{direction:ltr}h3{page-break-after:avoid}</style><body>");
        html.Append($"<h2>تاریخچه اجرای گروهی {batch.Id}</h2><p>تاریخ: {E(Date(batch.ExecutedAt))} · حساب پرداخت: {E(batch.AccountName)} · اجراکننده: {E(batch.ExecutedByName)} · تعداد: {batch.Count}</p><table><thead><tr><th>ارز</th><th>اصل حواله</th><th>کمیشن عامل پرداخت</th><th>مجموع پرداخت</th></tr></thead><tbody>");
        foreach (var total in batch.Totals) html.Append($"<tr><td>{E(total.CurrencyCode)}</td><td class='number'>{N(total.Principal)}</td><td class='number'>{N(total.AgentCommission)}</td><td class='number'>{N(total.Total)}</td></tr>");
        html.Append("</tbody></table>");
        foreach (var group in batch.Items.GroupBy(x => x.CurrencyCode))
        {
            html.Append($"<h3>{E(group.Key)} · مجموع اصل حواله: {N(group.Sum(x => x.Amount))}</h3><table><thead><tr><th>نمبر</th><th>متفرقه</th><th>فرستنده</th><th>گیرنده</th><th>نمایندگی</th><th>محل پرداخت</th><th>مبلغ</th><th>کمیشن عامل</th><th>تاریخ ثبت</th></tr></thead><tbody>");
            foreach (var item in group) html.Append($"<tr><td>{item.Number}</td><td>{E(item.ReferenceNumber)}</td><td>{E(item.SenderName)}</td><td>{E(item.ReceiverName)}</td><td>{E(item.CorrespondentName)}</td><td>{E(item.PaymentLocation)}</td><td class='number'>{N(item.Amount)} {E(item.CurrencyCode)}</td><td class='number'>{N(item.AgentCommission)} {E(item.CommissionCurrencyCode)}</td><td>{E(Date(item.RegisteredAt))}</td></tr>");
            html.Append("</tbody></table>");
        }
        html.Append("</body></html>");
        var document = new HtmlToPdfDocument
        {
            GlobalSettings = { PaperSize = PaperKind.A4, Orientation = Orientation.Landscape, DocumentTitle = name, Margins = new MarginSettings { Top = 10, Bottom = 15, Left = 8, Right = 8 } },
            Objects = { new ObjectSettings { HtmlContent = html.ToString(), WebSettings = new WebSettings { DefaultEncoding = "utf-8", PrintMediaType = true }, FooterSettings = new FooterSettings { Right = "[page] / [toPage]", FontSize = 8 } } }
        };
        return new(name + ".pdf", "application/pdf", converter.Convert(document));
    }
}
