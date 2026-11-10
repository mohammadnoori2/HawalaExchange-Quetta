using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Helpers;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.Infrastructure.Services;
public sealed class CashExecutedHawalaService(ApplicationDbContext context, IConverter? pdf = null) : ICashExecutedHawalaService
{
    private sealed record Preferences(string Zone, bool Persian);
    private async Task<Preferences> Settings(CancellationToken ct)
    {
        if (context.CurrentTenantId <= 0) throw new UnauthorizedAccessException("صرافی جاری مشخص نیست.");
        if (!context.Database.IsSqlServer()) throw new NotSupportedException("گزارش اجرای صندوق برای SQL Server پیاده‌سازی شده است.");
        var zone = await context.Database.SqlQuery<string>($"SELECT TimeZoneId AS [Value] FROM dbo.TenantOperationalControls WHERE TenantId={context.CurrentTenantId}").SingleOrDefaultAsync(ct);
        var persian = await context.CompanySettings.AsNoTracking().Select(x => (bool?)x.UsePersianCalendar).FirstOrDefaultAsync(ct) ?? true;
        return new(string.IsNullOrWhiteSpace(zone) ? "Asia/Kabul" : zone, persian);
    }
    private async Task<IQueryable<CashExecutedHawalaPart>> Query(CashExecutedHawalaFilter filter, CancellationToken ct)
    {
        var settings = await Settings(ct);
        if (filter.From.Date > filter.To.Date || filter.To.Year >= 9999 || filter.Mode is not ("" or "Direct" or "Bill") || filter.State is not ("Paid" or "Reversed" or "All"))
            throw new InvalidOperationException("بازه تاریخ یا فیلتر وضعیت معتبر نیست.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.Zone);
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(filter.From.Date, DateTimeKind.Unspecified), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(filter.To.Date.AddDays(1), DateTimeKind.Unspecified), zone);
        var query = context.Database.SqlQuery<CashExecutedHawalaPart>($"SELECT * FROM dbo.ufn_CashExecutedHawalas_v1({context.CurrentTenantId})")
            .Where(x => x.PaidAt >= start && x.PaidAt < end);
        if (filter.State == "Paid") query = query.Where(x => !x.IsReversed);
        if (filter.State == "Reversed") query = query.Where(x => x.IsReversed);
        if (filter.Mode != "") query = query.Where(x => x.Mode == filter.Mode);
        if (filter.AccountId.HasValue) query = query.Where(x => x.AccountId == filter.AccountId);
        if (filter.CorrespondentId.HasValue) query = query.Where(x => x.CorrespondentId == filter.CorrespondentId);
        if (filter.LocationId.HasValue) query = filter.LocationId == 0 ? query.Where(x => x.LocationId == null) : query.Where(x => x.LocationId == filter.LocationId);
        if (filter.CurrencyId.HasValue) query = query.Where(x => x.CurrencyId == filter.CurrencyId);
        var search = filter.Search?.Trim() ?? "";
        if (search != "") query = query.Where(x => x.Number.ToString().Contains(search) || x.Reference.Contains(search) || x.Receiver.Contains(search) || x.Sender.Contains(search));
        return query;
    }
    public async Task<CashExecutedHawalaLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await Settings(cancellationToken);
        return new() {
            Today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(settings.Zone)).Date,
            Accounts = await context.Accounts.AsNoTracking().Where(x => x.AccountType == "Cash").OrderBy(x => x.AccountName).Select(x => new AgentCommissionReportOption(x.Id, x.AccountName)).ToListAsync(cancellationToken),
            Correspondents = await context.Correspondents.AsNoTracking().OrderBy(x => x.Name).Select(x => new AgentCommissionReportOption(x.Id, x.Name)).ToListAsync(cancellationToken),
            Locations = await context.PaymentLocations.AsNoTracking().OrderBy(x => x.Name).Select(x => new AgentCommissionReportOption(x.Id, x.Name)).ToListAsync(cancellationToken),
            Currencies = await context.Currencies.AsNoTracking().OrderBy(x => x.Code).Select(x => new AgentCommissionReportOption(x.Id, x.Code)).ToListAsync(cancellationToken) };
    }
    public async Task<CashExecutedHawalaResult> GetAsync(CashExecutedHawalaFilter filter, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var query = await Query(filter, cancellationToken);
        var totals = await query.GroupBy(x => new { x.Currency, x.IsReversed }).Select(g => new {
            g.Key.Currency, g.Key.IsReversed, Count = g.Select(x => x.HawalaId).Distinct().Count(), Principal = g.Sum(x => x.Principal), Commission = g.Sum(x => x.Commission) }).ToListAsync(cancellationToken);
        var count = await query.Select(x => x.HawalaId).Distinct().CountAsync(cancellationToken);
        var paymentCount = await query.Select(x => x.EventKey).Distinct().CountAsync(cancellationToken);
        pageSize = Math.Clamp(pageSize, 1, 100); var pages = Math.Max(1, (int)Math.Ceiling(paymentCount / (double)pageSize)); page = Math.Clamp(page, 1, pages);
        var keys = await query.GroupBy(x => x.EventKey).Select(g => new { Key = g.Key, PaidAt = g.Max(x => x.PaidAt) })
            .OrderByDescending(x => x.PaidAt).ThenBy(x => x.Key).Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Key).ToListAsync(cancellationToken);
        var parts = await query.Where(x => keys.Contains(x.EventKey)).OrderBy(x => x.Currency).ThenBy(x => x.AccountId).ToListAsync(cancellationToken);
        return new() { HawalaCount = count, PaymentCount = paymentCount,
            Totals = totals.Where(x => !x.IsReversed).OrderBy(x => x.Currency).Select(x => new CashExecutedHawalaTotal { Count = x.Count, Currency = x.Currency, Principal = x.Principal, Commission = x.Commission }).ToList(),
            ReversedTotals = totals.Where(x => x.IsReversed).OrderBy(x => x.Currency).Select(x => new CashExecutedHawalaTotal { Count = x.Count, Currency = x.Currency, Principal = x.Principal, Commission = x.Commission }).ToList(),
            Page = new() { TotalCount = paymentCount, PageNumber = page, PageSize = pageSize, TotalPages = pages,
                Items = keys.Select(key => new CashExecutedHawalaRow { Payment = parts.First(x => x.EventKey == key), Amounts = parts.Where(x => x.EventKey == key).ToList() }).ToList() } };
    }
    public async Task<SelectedRecordsFileDto> ExportAsync(CashExecutedHawalaFilter filter, ExportFormat format, CancellationToken cancellationToken = default)
    {
        if (format is not (ExportFormat.Excel or ExportFormat.Pdf)) throw new InvalidOperationException("نوع خروجی معتبر نیست.");
        var query = await Query(filter, cancellationToken);
        var limit = format == ExportFormat.Excel ? 50000 : 5000;
        var rows = await query.OrderByDescending(x => x.PaidAt).ThenBy(x => x.EventKey).ThenBy(x => x.Currency).Take(limit + 1).ToListAsync(cancellationToken);
        if (rows.Count > limit) throw new InvalidOperationException($"حداکثر {limit.ToString(CultureInfo.InvariantCulture)} سطر ارزی در هر خروجی پشتیبانی می‌شود؛ بازه یا فیلتر را محدود کنید. خروجی ناقص صادر نمی‌شود.");
        var settings = await Settings(cancellationToken); var dates = new DisplayDateFormatter(); dates.SetCalendar(settings.Persian); dates.SetTimeZone(settings.Zone);
        var totals = rows.GroupBy(x => new { x.Currency, x.IsReversed }).Select(g => new { g.Key.Currency, g.Key.IsReversed, Count = g.Select(x => x.HawalaId).Distinct().Count(), Principal = g.Sum(x => x.Principal), Commission = g.Sum(x => x.Commission) }).ToList();
        var headings = new[] { "نمبر مسلسل", "نمبر حواله", "گیرنده", "محل پرداخت", "نمایندگی", "اصل مبلغ", "کمیشن عامل", "مجموع صندوق", "ارز", "صندوق", "تاریخ پرداخت", "کاربر اجراکننده", "نوع اجرا", "بیل", "وضعیت", "تاریخ برگشت" };
        string[] Cells(CashExecutedHawalaPart row) => [row.Number.ToString(CultureInfo.InvariantCulture), row.Reference, row.Receiver, row.Location, row.Correspondent,
            Money(row.Principal), Money(row.Commission), Money(row.Principal + row.Commission), row.Currency, row.Account,
            dates.DateTime(row.PaidAt, true, true), row.ExecutedByName, row.Mode == "Bill" ? "از طریق بیل" : "اجرای مستقیم", row.BillId?.ToString(CultureInfo.InvariantCulture) ?? "—",
            row.IsReversed ? "برگشت‌شده" : "پرداخت‌شده", dates.DateTime(row.ReversedAt, true, true)];
        var count = rows.Select(x => x.HawalaId).Distinct().Count();
        var title = $"حواله‌های اجراشده از صندوق — {dates.Date(filter.From)} تا {dates.Date(filter.To)} — {count.ToString(CultureInfo.InvariantCulture)} حواله";
        if (format == ExportFormat.Excel)
        {
            using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Cash executions"); sheet.RightToLeft = true;
            sheet.Cell(1, 1).Value = title; sheet.Cell(2, 1).Value = "ارزها در سطرهای جدا؛ پرداخت‌های برگشت‌شده خارج از مجموع اصلی هستند.";
            for (var i = 0; i < headings.Length; i++) sheet.Cell(4, i + 1).Value = headings[i];
            var number = 5;
            foreach (var row in rows) {
                var cells = Cells(row); for (var i = 0; i < cells.Length; i++) sheet.Cell(number, i + 1).Value = cells[i];
                sheet.Cell(number, 6).Value = row.Principal; sheet.Cell(number, 7).Value = row.Commission; sheet.Cell(number, 8).Value = row.Principal + row.Commission;
                sheet.Range(number, 6, number, 8).Style.NumberFormat.Format = "[$-en-US]#,##0";
                if (row.IsReversed) sheet.Row(number).Style.Font.FontColor = XLColor.Red;
                number++;
            }
            sheet.SheetView.FreezeRows(4); sheet.Columns().Width = 21;
            var summary = book.AddWorksheet("Summary"); summary.RightToLeft = true;
            var summaryHeaders = new[] { "وضعیت", "ارز", "مجموع اصل مبلغ", "کمیشن عامل", "مجموع پرداخت صندوق", "تعداد حواله" };
            for (var i = 0; i < summaryHeaders.Length; i++) summary.Cell(1, i + 1).Value = summaryHeaders[i];
            number = 2; foreach (var value in totals) { summary.Cell(number, 1).Value = value.IsReversed ? "برگشت‌شده — خارج از مجموع اصلی" : "پرداخت‌شده"; summary.Cell(number, 2).Value = value.Currency;
                summary.Cell(number, 3).Value = value.Principal; summary.Cell(number, 4).Value = value.Commission; summary.Cell(number, 5).Value = value.Principal + value.Commission; summary.Cell(number, 6).Value = value.Count; number++; }
            summary.Columns().Width = 25; summary.Range(2, 3, Math.Max(2, number - 1), 5).Style.NumberFormat.Format = "[$-en-US]#,##0";
            using var output = new MemoryStream(); book.SaveAs(output);
            return new("CashExecutedHawalas.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", output.ToArray());
        }
        if (pdf is null) throw new InvalidOperationException("سرویس PDF در دسترس نیست.");
        static string E(string text) => WebUtility.HtmlEncode(text);
        var html = new StringBuilder("<!doctype html><html dir='rtl'><meta charset='utf-8'><style>body{font:10px Tahoma,Arial}table{width:100%;border-collapse:collapse;table-layout:fixed}td,th{border:1px solid #ccd5e0;padding:4px;word-wrap:break-word;text-align:center}th{background:#edf3fa}thead{display:table-header-group}tr{page-break-inside:avoid}.reversed{color:#bf3434}</style><body><h2>");
        html.Append(E(title)).Append("</h2><p>ارزها جدا جمع شده‌اند؛ برگشت‌ها در مجموع اصلی شامل نیستند.</p><table><tr><th>وضعیت</th><th>ارز</th><th>اصل مبلغ</th><th>کمیشن عامل</th><th>مجموع صندوق</th><th>تعداد حواله</th></tr>");
        foreach (var value in totals) html.Append("<tr><td>").Append(value.IsReversed ? "برگشت‌شده" : "پرداخت‌شده").Append("</td><td>").Append(E(value.Currency)).Append("</td><td>").Append(Money(value.Principal)).Append("</td><td>").Append(Money(value.Commission)).Append("</td><td>").Append(Money(value.Principal + value.Commission)).Append("</td><td>").Append(value.Count.ToString(CultureInfo.InvariantCulture)).Append("</td></tr>");
        html.Append("</table><h3>جزئیات</h3><table><thead><tr>"); foreach (var header in headings) html.Append("<th>").Append(E(header)).Append("</th>"); html.Append("</tr></thead><tbody>");
        foreach (var row in rows) { html.Append(row.IsReversed ? "<tr class='reversed'>" : "<tr>"); foreach (var cell in Cells(row)) html.Append("<td>").Append(E(cell)).Append("</td>"); html.Append("</tr>"); }
        html.Append("</tbody></table></body></html>");
        var document = new HtmlToPdfDocument { GlobalSettings = { PaperSize = PaperKind.A4, Orientation = Orientation.Landscape, Margins = new MarginSettings { Top = 8, Bottom = 12, Left = 5, Right = 5 } },
            Objects = { new ObjectSettings { HtmlContent = html.ToString(), WebSettings = new WebSettings { DefaultEncoding = "utf-8", EnableJavascript = false }, FooterSettings = new FooterSettings { Right = "[page]/[toPage]", FontSize = 8 } } } };
        return new("CashExecutedHawalas.pdf", "application/pdf", pdf.Convert(document));
    }
    private static string Money(decimal value) => AmountValueHelper.Format(value);
}
