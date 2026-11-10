using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Helpers;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.Infrastructure.Services;

public sealed class AgentCommissionReportService(ApplicationDbContext context, IConverter? pdfConverter = null) : IAgentCommissionReportService
{
    private sealed record Prepared(string Name, string Zone, bool Persian, IQueryable<AgentCommissionReportRow> Query);
    private sealed class Aggregate
    {
        public long? LocationId { get; set; }
        public string Location { get; set; } = "";
        public string Currency { get; set; } = "";
        public string CommissionCurrency { get; set; } = "";
        public string Status { get; set; } = "";
        public string Settlement { get; set; } = "";
        public int Count { get; set; }
        public decimal Principal { get; set; }
        public decimal Commission { get; set; }
    }

    public async Task<AgentCommissionReportLookups> GetLookupsAsync(long correspondentId, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(new() { CorrespondentId = correspondentId }, cancellationToken);
        var source = context.Hawalas.AsNoTracking().Where(x => x.CorrespondentId == correspondentId && x.HawalaType == "HawalaSend" && !x.IsDeleted);
        var locations = await source.Where(x => x.PaymentLocationId != null).Select(x => new { Id = x.PaymentLocationId!.Value, Name = x.PaymentLocation!.Name })
            .Distinct().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var currencies = await source.Select(x => new { Id = x.ToCurrencyId, Name = x.ToCurrency.Code })
            .Concat(source.Where(x => x.AgentCommissionCurrencyId != null).Select(x => new { Id = x.AgentCommissionCurrencyId!.Value, Name = x.AgentCommissionCurrency!.Code }))
            .Distinct().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return new() { CorrespondentName = prepared.Name, Today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(prepared.Zone)).Date,
            Locations = locations.Select(x => new AgentCommissionReportOption(x.Id, x.Name)).ToList(), Currencies = currencies.Select(x => new AgentCommissionReportOption(x.Id, x.Name)).ToList() };
    }

    private async Task<Prepared> PrepareAsync(AgentCommissionReportFilter filter, CancellationToken ct)
    {
        if (context.CurrentTenantId <= 0 || filter.CorrespondentId <= 0) throw new InvalidOperationException("نمایندگی و صرافی جاری مشخص نیست.");
        if (filter.From.Date > filter.To.Date || filter.To.Year >= 9999) throw new InvalidOperationException("بازه تاریخ معتبر نیست.");
        if (filter.DateBasis is not ("Commission" or "Registration")) throw new InvalidOperationException("مبنای تاریخ معتبر نیست.");
        if (filter.Status is not ("" or "Pending" or "Paid" or "Cancel") || filter.Settlement is not ("" or "Paid" or "Historical" or "Billed" or "Unrecorded"))
            throw new InvalidOperationException("فیلتر وضعیت معتبر نیست.");
        var name = await context.Correspondents.AsNoTracking().Where(x => x.Id == filter.CorrespondentId).Select(x => x.Name).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("نمایندگی یافت نشد یا در دسترس نیست.");
        var zoneId = await (context.Database.IsNpgsql()
            ? context.Database.SqlQuery<string>($"SELECT \"TimeZoneId\" AS \"Value\" FROM public.\"TenantOperationalControls\" WHERE \"TenantId\"={context.CurrentTenantId}")
            : context.Database.SqlQuery<string>($"SELECT TimeZoneId AS [Value] FROM dbo.TenantOperationalControls WHERE TenantId={context.CurrentTenantId}")).SingleOrDefaultAsync(ct);
        zoneId = string.IsNullOrWhiteSpace(zoneId) ? "Asia/Kabul" : zoneId;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(filter.From.Date, DateTimeKind.Unspecified), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(filter.To.Date.AddDays(1), DateTimeKind.Unspecified), zone);
        var source = context.Hawalas.AsNoTracking().Where(x => !x.IsDeleted && x.HawalaType == "HawalaSend" && x.CorrespondentId == filter.CorrespondentId);
        if (filter.DateBasis == "Registration") source = source.Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (!filter.IncludeZeroCommission) source = source.Where(x => x.AgentCommissionAmount > 0);
        if (filter.Status != "") source = source.Where(x => x.Status == filter.Status);
        else if (!filter.IncludeCancelled) source = source.Where(x => x.Status != "Cancel");
        if (filter.PaymentLocationId.HasValue) source = filter.PaymentLocationId == 0 ? source.Where(x => x.PaymentLocationId == null) : source.Where(x => x.PaymentLocationId == filter.PaymentLocationId);
        if (filter.CurrencyId.HasValue) source = source.Where(x => x.ToCurrencyId == filter.CurrencyId);
        var search = filter.Search?.Trim() ?? "";
        if (search != "") source = source.Where(x => x.Number.ToString().Contains(search) || (x.ReferenceNumber != null && x.ReferenceNumber.Contains(search)) ||
            (x.SenderName != null && x.SenderName.Contains(search)) || (x.ReceiverName != null && x.ReceiverName.Contains(search)));
        const string additionMessage = "کمیشن نمایندگی به مقدار";
        var encodedAddition = "\"__Description\":\"" + System.Text.Json.JsonEncodedText.Encode(additionMessage).ToString();
        var plainAddition = "\"__Description\":\"" + additionMessage;
        // The active item index permits at most one bill per hawala. A correlated
        // lookup avoids multiplying fees by cancelled bills or reissued receipts.
        var query = from h in source
                    let addedAt = context.AuditLogs.Where(a => a.TableName == "Hawalas" && a.RecordId == h.Id && a.Action == "UPDATE" &&
                        a.NewValue != null && (a.NewValue.StartsWith(additionMessage) || a.NewValue.Contains(encodedAddition) || a.NewValue.Contains(plainAddition)))
                        .Select(a => (DateTime?)a.CreatedAt).Min()
                    let postedAt = context.LedgerEntries.Where(e => e.HawalaId == h.Id && e.TransactionId == null && e.BadehKar > 0 &&
                        (e.Account.AccountType == "Expense" || e.Account.AccountType == "Income") && e.Description != null &&
                        e.Description.Contains("هزینه کمیشن عامل پرداخت")).Select(e => (DateTime?)e.CreatedAt).Min()
                    let bill = context.AgentPaymentBillItems.Where(i => i.HawalaId == h.Id && i.IsActive && i.Bill.Status != "Cancelled").Select(i => i.Bill).FirstOrDefault()
                    select new AgentCommissionReportRow {
                        HawalaId = h.Id, Number = h.Number, Reference = h.ReferenceNumber ?? "", CreatedAt = h.CreatedAt,
                        CommissionAddedAt = addedAt ?? postedAt ?? h.CreatedAt,
                        Sender = h.SenderName ?? "", Receiver = h.ReceiverName ?? "", LocationId = h.PaymentLocationId,
                        Location = h.PaymentLocation == null ? "نامشخص" : h.PaymentLocation.Name,
                        CurrencyId = h.ToCurrencyId, Currency = h.ToCurrency.Code, Amount = h.ToAmount ?? h.FromAmount,
                        CommissionCurrencyId = h.AgentCommissionCurrencyId ?? (h.AgentCommissionAmount > 0 ? 0 : h.ToCurrencyId),
                        CommissionCurrency = h.AgentCommissionCurrency != null ? h.AgentCommissionCurrency.Code : h.AgentCommissionAmount > 0 ? "نامشخص" : h.ToCurrency.Code,
                        Commission = h.AgentCommissionAmount ?? 0, Status = h.Status,
                        BillId = bill == null ? null : bill.Id,
                        Settlement = bill == null ? "Unrecorded" : bill.Status == "Paid" ? "Paid" : bill.Status == "Historical" ? "Historical" : "Billed"
                    };
        if (filter.DateBasis == "Commission") query = query.Where(x => x.CommissionAddedAt >= start && x.CommissionAddedAt < end);
        if (filter.CommissionCurrencyId.HasValue) query = query.Where(x => x.CommissionCurrencyId == filter.CommissionCurrencyId);
        if (filter.Settlement != "") query = query.Where(x => x.Settlement == filter.Settlement);
        var persian = await context.CompanySettings.AsNoTracking().Select(x => (bool?)x.UsePersianCalendar).FirstOrDefaultAsync(ct) ?? true;
        return new(name, zoneId, persian, query);
    }

    public async Task<AgentCommissionReportResult> GetAsync(AgentCommissionReportFilter filter, CancellationToken cancellationToken = default)
        => await SummarizeAsync(await PrepareAsync(filter, cancellationToken), cancellationToken);

    private static async Task<AgentCommissionReportResult> SummarizeAsync(Prepared prepared, CancellationToken ct)
    {
        var groups = await prepared.Query.GroupBy(x => new { x.LocationId, x.Location, x.Currency, x.CommissionCurrency, x.Status, x.Settlement })
            .Select(g => new Aggregate { LocationId = g.Key.LocationId, Location = g.Key.Location, Currency = g.Key.Currency,
                CommissionCurrency = g.Key.CommissionCurrency, Status = g.Key.Status, Settlement = g.Key.Settlement,
                Count = g.Count(), Principal = g.Sum(x => x.Amount), Commission = g.Sum(x => x.Commission) }).ToListAsync(ct);
        return BuildResult(prepared.Name, groups);
    }

    private static AgentCommissionReportResult BuildResult(string name, List<Aggregate> groups) =>
        new() { CorrespondentName = name, Count = groups.Sum(x => x.Count), CancelledCount = groups.Where(x => x.Status == "Cancel").Sum(x => x.Count),
            Currencies = Summaries(groups), Locations = groups.GroupBy(x => new { x.LocationId, x.Location }).OrderBy(g => g.Key.Location)
                .Select(g => new AgentCommissionLocationSummary { LocationId = g.Key.LocationId, Location = g.Key.Location, Count = g.Sum(x => x.Count), Currencies = Summaries(g) }).ToList() };

    private static List<AgentCommissionCurrencySummary> Summaries(IEnumerable<Aggregate> groups)
    {
        var values = new Dictionary<string, AgentCommissionCurrencySummary>();
        AgentCommissionCurrencySummary Currency(string code) { if (!values.TryGetValue(code, out var value)) values[code] = value = new() { Currency = code }; return value; }
        foreach (var group in groups)
        {
            var principal = Currency(group.Currency); var fee = Currency(group.CommissionCurrency);
            if (group.Status == "Cancel") { principal.CancelledPrincipal += group.Principal; fee.CancelledCommission += group.Commission; continue; }
            principal.Principal += group.Principal; fee.Commission += group.Commission;
            switch (group.Settlement) {
                case "Paid": fee.PaidCommission += group.Commission; break;
                case "Historical": fee.HistoricalCommission += group.Commission; break;
                case "Billed": fee.BilledCommission += group.Commission; break;
                default: fee.UnrecordedCommission += group.Commission; break;
            }
        }
        return values.Values.OrderBy(x => x.Currency).ToList();
    }

    public async Task<PaginatedResult<AgentCommissionReportRow>> GetDetailsAsync(AgentCommissionReportFilter filter, long? locationId = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(filter, cancellationToken); var query = prepared.Query;
        if (locationId.HasValue) query = locationId == 0 ? query.Where(x => x.LocationId == null) : query.Where(x => x.LocationId == locationId);
        pageSize = Math.Clamp(pageSize, 1, 100); page = Math.Max(1, page);
        var count = await query.CountAsync(cancellationToken); var pages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize)); page = Math.Min(page, pages);
        var ordered = filter.DateBasis == "Commission" ? query.OrderByDescending(x => x.CommissionAddedAt) : query.OrderByDescending(x => x.CreatedAt);
        return new() { Items = await ordered.ThenByDescending(x => x.HawalaId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken),
            TotalCount = count, PageNumber = page, PageSize = pageSize, TotalPages = pages };
    }

    public async Task<SelectedRecordsFileDto> ExportAsync(AgentCommissionReportFilter filter, ExportFormat format, CancellationToken cancellationToken = default)
    {
        if (format is not (ExportFormat.Excel or ExportFormat.Pdf)) throw new InvalidOperationException("نوع خروجی معتبر نیست.");
        var prepared = await PrepareAsync(filter, cancellationToken);
        var limit = format == ExportFormat.Pdf ? 5000 : 50000;
        var rows = await prepared.Query.OrderBy(x => x.Location).ThenBy(x => filter.DateBasis == "Commission" ? x.CommissionAddedAt : x.CreatedAt).ThenBy(x => x.HawalaId).Take(limit + 1).ToListAsync(cancellationToken);
        if (rows.Count > limit) throw new InvalidOperationException($"برای این خروجی حداکثر {limit.ToString(CultureInfo.InvariantCulture)} حواله پشتیبانی می‌شود؛ بازه یا فیلتر را محدود کنید. خروجی ناقص صادر نمی‌شود.");
        // Export totals from the exact exported rows, even if another user posts
        // or cancels a bill while the file is being generated.
        var summary = BuildResult(prepared.Name, rows.GroupBy(x => new { x.LocationId, x.Location, x.Currency, x.CommissionCurrency, x.Status, x.Settlement })
            .Select(g => new Aggregate { LocationId = g.Key.LocationId, Location = g.Key.Location, Currency = g.Key.Currency, CommissionCurrency = g.Key.CommissionCurrency,
                Status = g.Key.Status, Settlement = g.Key.Settlement, Count = g.Count(), Principal = g.Sum(x => x.Amount), Commission = g.Sum(x => x.Commission) }).ToList());
        var dates = new DisplayDateFormatter(); dates.SetCalendar(prepared.Persian); dates.SetTimeZone(prepared.Zone);
        return format == ExportFormat.Excel ? Excel(filter, prepared.Name, rows, summary, dates) : Pdf(filter, prepared.Name, rows, summary, dates);
    }

    private static readonly string[] Headers = ["نمبر مسلسل", "نمبر حواله", "تاریخ ثبت", "فرستنده", "گیرنده", "محل پرداخت", "مبلغ حواله", "ارز حواله", "کمیشن عامل", "ارز کمیشن", "وضعیت حواله", "وضعیت تسویه", "نمبر بیل", "تاریخ افزودن کمیشن"];
    private static string[] Cells(AgentCommissionReportRow x, DisplayDateFormatter dates) => [x.Number.ToString(CultureInfo.InvariantCulture), x.Reference, dates.TimestampDate(x.CreatedAt),
        x.Sender, x.Receiver, x.Location, Money(x.Amount), x.Currency, Money(x.Commission), x.CommissionCurrency,
        HawalaExchange.Application.Services.AppDisplayText.Status(x.Status, "HawalaSend"), AgentCommissionReportLabels.Settlement(x.Settlement), x.BillId?.ToString(CultureInfo.InvariantCulture) ?? "—", dates.TimestampDate(x.CommissionAddedAt)];
    private static string Money(decimal value) => HawalaExchange.Application.Services.AmountValueHelper.Format(value);

    private static SelectedRecordsFileDto Excel(AgentCommissionReportFilter filter, string name, List<AgentCommissionReportRow> rows, AgentCommissionReportResult report, DisplayDateFormatter dates)
    {
        using var book = new XLWorkbook(); var details = book.AddWorksheet("Hawalas"); details.RightToLeft = true;
        details.Cell(1, 1).Value = "راپور کمیشن عامل پرداخت — " + name;
        details.Cell(2, 1).Value = dates.Date(filter.From) + " تا " + dates.Date(filter.To);
        details.Cell(3, 1).Value = filter.DateBasis == "Commission" ? "مبنای تاریخ: افزودن کمیشن" : "مبنای تاریخ: ثبت حواله";
        for (var i = 0; i < Headers.Length; i++) details.Cell(4, i + 1).Value = Headers[i];
        var rowIndex = 5;
        foreach (var row in rows)
        {
            var cells = Cells(row, dates);
            for (var i = 0; i < cells.Length; i++) details.Cell(rowIndex, i + 1).Value = cells[i];
            details.Cell(rowIndex, 7).Value = row.Amount; details.Cell(rowIndex, 9).Value = row.Commission;
            details.Cell(rowIndex, 7).Style.NumberFormat.Format = "[$-en-US]#,##0"; details.Cell(rowIndex, 9).Style.NumberFormat.Format = "[$-en-US]#,##0";
            if (row.Status == "Cancel") details.Row(rowIndex).Style.Font.FontColor = XLColor.Red;
            rowIndex++;
        }
        details.SheetView.FreezeRows(4); details.Range(4, 1, Math.Max(4, rowIndex - 1), Headers.Length).SetAutoFilter(); details.Columns().Width = 21;
        var summary = book.AddWorksheet("Summary"); summary.RightToLeft = true;
        var headings = new[] { "محل پرداخت", "تعداد حواله", "ارز", "اصل حواله‌ها", "مجموع کمیشن", "تسویه از طریق بیل", "رسید تسویه قبلی", "بیل در انتظار", "بدون سابقه تسویه", "کمیشن لغوشده (خارج از مجموع)" };
        for (var i = 0; i < headings.Length; i++) summary.Cell(1, i + 1).Value = headings[i];
        rowIndex = 2;
        void Write(string location, int count, IEnumerable<AgentCommissionCurrencySummary> values)
        {
            var first = true;
            foreach (var value in values)
            {
                summary.Cell(rowIndex, 1).Value = location; if (first) summary.Cell(rowIndex, 2).Value = count; first = false; summary.Cell(rowIndex, 3).Value = value.Currency;
                var amounts = new[] { value.Principal, value.Commission, value.PaidCommission, value.HistoricalCommission, value.BilledCommission, value.UnrecordedCommission, value.CancelledCommission };
                for (var i = 0; i < amounts.Length; i++) summary.Cell(rowIndex, i + 4).Value = amounts[i];
                rowIndex++;
            }
        }
        Write("مجموع تمام محل‌ها", report.Count, report.Currencies);
        foreach (var location in report.Locations) Write(location.Location, location.Count, location.Currencies);
        summary.Columns().Width = 24; summary.Range(2, 4, Math.Max(2, rowIndex - 1), 10).Style.NumberFormat.Format = "[$-en-US]#,##0";
        using var output = new MemoryStream(); book.SaveAs(output);
        return new("AgentCommissionReport.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", output.ToArray());
    }

    private SelectedRecordsFileDto Pdf(AgentCommissionReportFilter filter, string name, List<AgentCommissionReportRow> rows, AgentCommissionReportResult report, DisplayDateFormatter dates)
    {
        if (pdfConverter is null) throw new InvalidOperationException("سرویس ساخت PDF در دسترس نیست.");
        static string E(string text) => WebUtility.HtmlEncode(text);
        var html = new StringBuilder("<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><style>body{font-family:Tahoma,Arial;font-size:10px;color:#172033}table{width:100%;border-collapse:collapse;margin:12px 0}th,td{border:1px solid #ccd5e0;padding:5px;text-align:center;word-wrap:break-word}table{table-layout:fixed}th{background:#edf3fa}thead{display:table-header-group}tr{page-break-inside:avoid}.cancelled{color:#bf3434}h2{text-align:center}</style><body>");
        html.Append("<h2>راپور کمیشن عامل پرداخت — ").Append(E(name)).Append("</h2><p>").Append(E(dates.Date(filter.From) + " تا " + dates.Date(filter.To)))
            .Append(filter.DateBasis == "Commission" ? " · مبنا: تاریخ افزودن کمیشن" : " · مبنا: تاریخ ثبت حواله").Append("</p><p>هر حواله یک‌بار محاسبه شده؛ کمیشن لغوشده در مجموع شامل نیست. ارز اصل حواله و کمیشن جدا جمع می‌شوند.</p>");
        html.Append("<table><thead><tr><th>محل پرداخت</th><th>ارز</th><th>اصل حواله‌ها</th><th>اصل لغوشده</th><th>مجموع کمیشن</th><th>تسویه با بیل</th><th>تسویه قبلی</th><th>بیل در انتظار</th><th>بدون سابقه</th><th>لغوشده</th></tr></thead><tbody>");
        void Write(string label, IEnumerable<AgentCommissionCurrencySummary> values)
        {
            foreach (var value in values) html.Append("<tr><td>").Append(E(label)).Append("</td><td>").Append(E(value.Currency)).Append("</td><td>")
                .Append(Money(value.Principal)).Append("</td><td>").Append(Money(value.CancelledPrincipal)).Append("</td><td>").Append(Money(value.Commission)).Append("</td><td>").Append(Money(value.PaidCommission)).Append("</td><td>").Append(Money(value.HistoricalCommission))
                .Append("</td><td>").Append(Money(value.BilledCommission)).Append("</td><td>").Append(Money(value.UnrecordedCommission)).Append("</td><td>").Append(Money(value.CancelledCommission)).Append("</td></tr>");
        }
        Write("مجموع تمام محل‌ها", report.Currencies); foreach (var location in report.Locations) Write(location.Location, location.Currencies);
        html.Append("</tbody></table><h3>جزئیات حواله‌ها</h3><table><thead><tr>");
        foreach (var header in Headers) html.Append("<th>").Append(E(header)).Append("</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var row in rows) { html.Append(row.Status == "Cancel" ? "<tr class='cancelled'>" : "<tr>"); foreach (var cell in Cells(row, dates)) html.Append("<td>").Append(E(cell)).Append("</td>"); html.Append("</tr>"); }
        html.Append("</tbody></table></body></html>");
        var document = new HtmlToPdfDocument { GlobalSettings = { PaperSize = PaperKind.A4, Orientation = Orientation.Landscape,
            DocumentTitle = "Agent commission report", Margins = new MarginSettings { Top = 8, Bottom = 12, Left = 6, Right = 6 } },
            Objects = { new ObjectSettings { HtmlContent = html.ToString(), WebSettings = new WebSettings { DefaultEncoding = "utf-8", EnableJavascript = false },
                FooterSettings = new FooterSettings { Right = "[page] / [toPage]", FontSize = 8 } } } };
        return new("AgentCommissionReport.pdf", "application/pdf", pdfConverter.Convert(document));
    }
}
