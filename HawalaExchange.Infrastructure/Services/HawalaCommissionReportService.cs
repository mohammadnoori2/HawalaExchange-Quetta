using System.Globalization;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class HawalaCommissionReportService(ApplicationDbContext context) : IHawalaCommissionReportService
{
    public async Task<HawalaCommissionReportResult> GetAsync(HawalaCommissionReportFilter filter, CancellationToken cancellationToken = default)
    {
        if (context.CurrentTenantId <= 0) throw new InvalidOperationException("صرافی جاری مشخص نیست.");
        if (filter.From.Date > filter.To.Date || filter.To.Year >= 9999) throw new InvalidOperationException("بازه تاریخ معتبر نیست.");
        if (new[] { filter.IncomingRate, filter.ForwardingRate, filter.DestinationAfnRate, filter.DestinationUsdRate }.Any(x => x < 0) ||
            filter.LocationRates.Any(x => x.ForwardingRate < 0 || x.DestinationAfnRate < 0 || x.DestinationUsdRate < 0))
            throw new InvalidOperationException("نرخ کمیشن منفی مجاز نیست.");
        using var scope = context.UseTenantScope(context.CurrentTenantId);
        var start = filter.From.Date.ToUniversalTime(); var end = filter.To.Date.AddDays(1).ToUniversalTime();
        var query = context.Hawalas.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (filter.HawalaType != "") query = query.Where(x => x.HawalaType == filter.HawalaType);
        if (filter.Status != "") query = query.Where(x => x.Status == filter.Status);
        if (filter.PaymentLocationId.HasValue) query = query.Where(x => x.PaymentLocationId == filter.PaymentLocationId);
        if (filter.Currency != "") query = query.Where(x => (x.HawalaType == "HawalaSend" ? x.ToCurrency.Code : x.FromCurrency.Code) == filter.Currency);
        if (filter.CorrespondentIds.Count > 0) query = query.Where(x => (x.CorrespondentId.HasValue && filter.CorrespondentIds.Contains(x.CorrespondentId.Value)) ||
            (x.SourceHawala != null && x.SourceHawala.CorrespondentId.HasValue && filter.CorrespondentIds.Contains(x.SourceHawala.CorrespondentId.Value)));
        var search = filter.Search.Trim();
        if (search != "") query = query.Where(x => x.Number.ToString().Contains(search) || (x.ReferenceNumber != null && x.ReferenceNumber.Contains(search)) ||
            (x.SenderName != null && x.SenderName.Contains(search)) || (x.ReceiverName != null && x.ReceiverName.Contains(search)));
        var transfers = await query.OrderBy(x => x.Id).Select(x => new Transfer
        {
            Id = x.Id, Number = x.Number, Reference = x.ReferenceNumber ?? "", Type = x.HawalaType, Date = x.CreatedAt,
            CorrespondentId = x.CorrespondentId, Correspondent = x.Correspondent == null ? "صرافی" : x.Correspondent.Name,
            Periodic = x.Correspondent != null && x.Correspondent.CommissionMethod == "PeriodicPerLakh",
            SourceId = x.SourceHawala == null ? null : x.SourceHawala.CorrespondentId,
            Source = x.SourceHawala == null || x.SourceHawala.Correspondent == null ? "صرافی" : x.SourceHawala.Correspondent.Name,
            SourceDate = x.SourceHawala == null ? x.CreatedAt : x.SourceHawala.CreatedAt,
            ValidSource = x.SourceHawala != null && x.SourceHawala.HawalaType == "HawalaReceive" && x.SourceHawala.Status != "Cancel",
            LegacyOrigin = context.CorrespondentCommissionBatchItems.Any(item => item.HawalaId == x.SourceHawalaId && item.CommissionScope == "Origin" && item.IsActive && item.Batch.Status == "Posted"),
            LocationId = x.PaymentLocationId, Location = x.PaymentLocation == null ? "نامشخص" : x.PaymentLocation.Name,
            Sender = x.SenderName ?? "", Receiver = x.ReceiverName ?? "", Status = x.Status,
            Currency = x.HawalaType == "HawalaSend" ? x.ToCurrency.Code : x.FromCurrency.Code,
            Amount = x.HawalaType == "HawalaSend" ? x.ToAmount ?? x.FromAmount : x.FromAmount,
            DirectAmount = x.CommissionAmount, DirectCurrency = x.CommissionCurrency == null ? "" : x.CommissionCurrency.Code,
            AgentAmount = x.AgentCommissionAmount, AgentCurrency = x.AgentCommissionCurrency == null ? "" : x.AgentCommissionCurrency.Code,
            Bulk = context.HawalaImportRows.Any(r => r.HawalaId == x.Id || r.GeneratedSendHawalaId == x.Id || (x.SourceHawalaId.HasValue && r.HawalaId == x.SourceHawalaId))
        }).Take(20001).ToListAsync(cancellationToken);
        if (transfers.Count > 20000) throw new InvalidOperationException("بیش از 20000 حواله یافت شد؛ بازه یا فیلتر را محدودتر کنید. گزارش ناقص صادر نمی‌شود.");
        transfers = transfers.Where(x => filter.Registration == "" || (filter.Registration == "Bulk" ? x.Bulk : !x.Bulk)).ToList();
        var ids = transfers.Select(x => x.Id).ToArray();
        var saved = await context.CorrespondentCommissionBatchItems.AsNoTracking().Where(x => ids.Contains(x.HawalaId)).Select(x => new Saved
        {
            HawalaId = x.HawalaId, BatchId = x.BatchId, Scope = x.Batch.CommissionScope, BatchStatus = x.Batch.Status,
            Active = x.IsActive, CorrespondentId = x.Batch.CorrespondentId, Correspondent = x.Batch.Correspondent.Name,
            TransactionId = x.Batch.PostingTransactionId, Amount = x.SourceAmount, Currency = x.SourceCurrency.Code,
            Commission = x.CommissionAfn, Basis = x.AfnEquivalent, ExchangeRate = x.SourceToAfnRate,
            PerLakhRate = x.PerLakhRate ?? x.Batch.CommissionPerLakhAfn, RateDate = x.ValuationDate,
            LocationId = x.PaymentLocationId, Location = x.PaymentLocationName
        }).ToListAsync(cancellationToken);
        var recognized = await context.Database.SqlQuery<Recognition>($"""
            SELECT r.TransactionId, a.CorrespondentId FROM dbo.CorrespondentCommissionRecognitions r
            JOIN dbo.Accounts a ON a.Id = r.AccountId AND a.TenantId = r.TenantId
            WHERE r.TenantId = {context.CurrentTenantId} AND a.CorrespondentId IS NOT NULL
            """).ToListAsync(cancellationToken);
        var recognitionKeys = recognized.Select(x => (x.TransactionId, x.CorrespondentId)).ToHashSet();
        var ownLocation = await context.CompanySettings.AsNoTracking().Select(x => x.OwnPaymentLocationId).FirstOrDefaultAsync(cancellationToken);
        var rates = filter.Estimate ? await context.CorrespondentDailyCommissionRates.AsNoTracking().ToListAsync(cancellationToken) : [];
        var rateMap = rates.ToDictionary(x => (x.CorrespondentId, x.RateDate.Date), x => x.UsdToAfnRate);
        var byTransfer = saved.ToLookup(x => x.HawalaId);
        var rows = new List<HawalaCommissionReportRow>();
        foreach (var transfer in transfers)
        {
            var startCount = rows.Count;
            var existing = byTransfer[transfer.Id].ToList();
            foreach (var item in existing)
            {
                var type = Normalize(item.Scope, transfer.Type);
                var row = Create(transfer, type);
                row.CorrespondentId = item.CorrespondentId; row.Correspondent = item.Correspondent;
                row.Amount = item.Amount; row.Currency = item.Currency; row.BatchId = item.BatchId;
                row.CommissionStatus = item.BatchStatus == "Reversed" ? "Reversed" : recognitionKeys.Contains((item.TransactionId, item.CorrespondentId)) ? "Recognized" : "Calculated";
                row.Commission = item.Commission; row.CommissionCurrency = type == "Destination" ? item.Currency : "USD";
                row.BasisUsd = type == "Destination" ? null : item.Basis;
                row.ExchangeRate = item.ExchangeRate; row.PerLakhRate = item.PerLakhRate; row.RateDate = item.RateDate;
                row.LocationId = item.LocationId ?? transfer.LocationId; row.Location = item.Location ?? transfer.Location;
                if (item.Amount < 0) row.Note = "کسر کمیشن ثبت‌شده";
                else if (!item.Active) row.Note = "سهم غیرفعال؛ وضعیت و کسرهای ثبت‌شده جدا نشان داده می‌شوند";
                rows.Add(row);
            }
            void AddDirect(string type, decimal amount, string currency)
            {
                var row = Create(transfer, type); row.Commission = amount; row.CommissionCurrency = currency;
                row.CommissionStatus = transfer.Status == "Cancel" ? "Reversed" : "Direct";
                row.Note = "کمیشن ثبت‌شده در خود حواله؛ مستقل از محاسبات دوره‌ای"; rows.Add(row);
            }
            if (transfer.DirectAmount is > 0) AddDirect("Direct", transfer.DirectAmount.Value, transfer.DirectCurrency);
            if (transfer.Type == "HawalaSend" && transfer.AgentAmount is > 0) AddDirect("Destination", transfer.AgentAmount.Value, transfer.AgentCurrency);
            var types = transfer.Type == "HawalaReceive" ? new[] { "Incoming" } : transfer.Type == "HawalaSend" ? new[] { "Destination", "Forwarding" } : [];
            foreach (var type in types)
            {
                if (existing.Any(x => x.Active && x.BatchStatus == "Posted" && Normalize(x.Scope, transfer.Type) == type)) continue;
                if (type == "Incoming" && (!transfer.Periodic || transfer.DirectAmount is > 0)) continue;
                if (type == "Destination" && (transfer.AgentAmount.HasValue || !transfer.SourceId.HasValue || !transfer.ValidSource || !transfer.CorrespondentId.HasValue)) continue;
                if (type == "Forwarding" && (!transfer.SourceId.HasValue || !transfer.ValidSource || !transfer.CorrespondentId.HasValue || transfer.LegacyOrigin || (ownLocation.HasValue && transfer.LocationId == ownLocation))) continue;
                if (transfer.Status == "Cancel") continue;
                var row = Create(transfer, type); row.CommissionStatus = "Pending";
                row.Note = "محاسبه دوره‌ای ثبت نشده است";
                if (filter.Estimate)
                {
                    row.IsEstimate = true;
                    var location = filter.LocationRates.FirstOrDefault(x => x.LocationId == transfer.LocationId);
                    row.PerLakhRate = type == "Incoming" ? filter.IncomingRate : type == "Forwarding" ? location?.ForwardingRate ?? filter.ForwardingRate :
                        transfer.Currency == "AFN" ? location?.DestinationAfnRate ?? filter.DestinationAfnRate : location?.DestinationUsdRate ?? filter.DestinationUsdRate;
                    row.CommissionCurrency = type == "Destination" ? transfer.Currency : "USD";
                    if (transfer.Currency is not ("USD" or "AFN")) row.Note = "برآورد فقط برای AFN و USD پشتیبانی می‌شود";
                    else if (type == "Destination") { row.Commission = transfer.Amount / 100000m * row.PerLakhRate; row.Note = "برآورد با نرخ واردشده؛ بدون ثبت حسابداری"; }
                    else if (type == "Forwarding" && !ownLocation.HasValue)
                        row.Note = "محل پرداخت خود صرافی در تنظیمات مشخص نیست؛ برآورد ارسالی انجام نشد";
                    else
                    {
                        var sourceId = type == "Incoming" ? transfer.CorrespondentId : transfer.SourceId;
                        var day = (type == "Incoming" ? transfer.Date : transfer.SourceDate).ToLocalTime().Date;
                        row.RateDate = day;
                        if (transfer.Currency == "USD") { row.ExchangeRate = 1; row.BasisUsd = transfer.Amount; }
                        else if (sourceId.HasValue && rateMap.TryGetValue((sourceId.Value, day), out var rate) && rate > 0)
                        { row.ExchangeRate = rate; row.BasisUsd = transfer.Amount / rate; }
                        else row.Note = "نرخ روز نمایندگی فرستنده موجود نیست؛ برآورد انجام نشد";
                        if (row.BasisUsd.HasValue) { row.Commission = row.BasisUsd / 100000m * row.PerLakhRate; row.Note = "برآورد با نرخ همان روز؛ بدون ثبت حسابداری"; }
                    }
                }
                rows.Add(row);
            }
            if (rows.Count == startCount)
            { var row = Create(transfer, "None"); row.CommissionStatus = "None"; row.Note = "کمیشن دوره‌ای واجد شرایط یا ثبت‌شده ندارد"; rows.Add(row); }
        }
        // Scope filtering happens AFTER assigning the actual commission correspondent.
        rows = rows.Where(x => (filter.CommissionType == "" || x.CommissionType == filter.CommissionType) &&
            (filter.CommissionStatus == "" || x.CommissionStatus == filter.CommissionStatus) &&
            (filter.CorrespondentIds.Count == 0 || (x.CorrespondentId.HasValue && filter.CorrespondentIds.Contains(x.CorrespondentId.Value)))).ToList();
        // Round estimates as totals, never independently per transfer. Largest-remainder
        // allocation keeps detail/summary amounts equal and forwarding grouped by sending day.
        foreach (var group in rows.Where(x => x.IsEstimate && x.Commission.HasValue).GroupBy(x =>
            (x.CommissionType, x.CorrespondentId, x.CommissionCurrency, Day: x.CommissionType == "Forwarding" ? x.Date.ToLocalTime().Date : DateTime.MinValue)))
        {
            var extra = AmountValueHelper.RoundConvertedAmount(group.Sum(x => x.Commission!.Value)) - group.Sum(x => decimal.Floor(x.Commission!.Value));
            var ordered = group.OrderByDescending(x => x.Commission!.Value - decimal.Floor(x.Commission.Value)).ThenBy(x => x.HawalaId).ToList();
            for (var index = 0; index < ordered.Count; index++) ordered[index].Commission = decimal.Floor(ordered[index].Commission!.Value) + (index < extra ? 1 : 0);
        }
        var result = new HawalaCommissionReportResult { Rows = rows.OrderBy(x => x.Date).ThenBy(x => x.Number).ThenBy(x => x.CommissionType).ToList() };
        var transferMap = transfers.ToDictionary(x => x.Id);
        result.Amounts = rows.DistinctBy(x => x.HawalaId).GroupBy(x => (x.HawalaType, Currency: transferMap[x.HawalaId].Currency)).Select(g =>
            new HawalaReportAmount(g.Key.HawalaType, g.Key.Currency, g.Count(), g.Sum(x => transferMap[x.HawalaId].Amount))).ToList();
        result.Summaries = rows.Where(x => x.Commission.HasValue && x.CommissionCurrency != "" && x.CommissionStatus != "Reversed").GroupBy(x => (x.CommissionCurrency, x.IsEstimate)).Select(g =>
            new HawalaReportSummary(g.Key.CommissionCurrency, g.Key.IsEstimate, g.Where(x => x.CommissionType != "Destination").Sum(x => x.Commission!.Value), g.Where(x => x.CommissionType == "Destination").Sum(x => x.Commission!.Value))).ToList();
        result.Groups = rows.GroupBy(x => (Label: filter.GroupBy switch { "Correspondent" => x.Correspondent, "Location" => x.Location, "Type" => HawalaReportLabels.Type(x.CommissionType), _ => x.Date.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) }, x.CommissionType, x.CommissionCurrency, x.CommissionStatus, x.IsEstimate))
            .Select(g => new HawalaReportGroup(g.Key.Label, g.Key.CommissionType, g.Key.CommissionCurrency, g.Key.CommissionStatus, g.Key.IsEstimate, g.Select(x => x.HawalaId).Distinct().Count(), g.Any(x => x.Commission.HasValue) ? g.Sum(x => x.Commission ?? 0) : null)).ToList();
        return result;
    }

    private static string Normalize(string scope, string type) => scope switch { "Origin" or "Forwarding" => "Forwarding", "Incoming" => "Incoming", "Destination" => "Destination", _ => type == "HawalaReceive" ? "Incoming" : "Destination" };
    private static HawalaCommissionReportRow Create(Transfer h, string type) => new()
    {
        HawalaId = h.Id, Number = h.Number, Reference = h.Reference, Date = h.Date, HawalaType = h.Type,
        CorrespondentId = type == "Forwarding" ? h.SourceId : h.CorrespondentId,
        Correspondent = type == "Forwarding" ? h.Source : h.Correspondent, Source = h.Type == "HawalaReceive" ? h.Correspondent : h.Source,
        Destination = h.Type == "HawalaSend" ? h.Correspondent : "صرافی", LocationId = h.LocationId, Location = h.Location,
        Sender = h.Sender, Receiver = h.Receiver, Currency = h.Currency, Amount = h.Amount, Status = h.Status,
        BulkImport = h.Bulk, CommissionType = type
    };
    public async Task<SelectedRecordsFileDto> ExportAsync(HawalaCommissionReportFilter filter, CancellationToken cancellationToken = default)
    {
        var report = await GetAsync(filter, cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Commission details"); sheet.RightToLeft = true;
        var headers = new[] { "نمبر حواله", "نمبر متفرقه", "تاریخ میلادی", "نوع حواله", "نمایندگی کمیشن", "فرستنده اصلی", "مقصد", "فرستنده", "گیرنده", "محل پرداخت", "مبلغ", "ارز", "وضعیت حواله", "نوع ثبت", "نوع کمیشن", "وضعیت کمیشن", "برآورد", "کمیشن", "ارز کمیشن", "مبنای USD", "نرخ روز", "کمیشن هر لک", "روز نرخ میلادی", "محاسبه", "توضیح" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var index = 2;
        foreach (var row in report.Rows)
        {
            var values = new[] { row.Number.ToString(CultureInfo.InvariantCulture), row.Reference, row.Date.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture), row.HawalaType,
                row.Correspondent, row.Source, row.Destination, row.Sender, row.Receiver, row.Location, "", row.Currency, row.Status, row.BulkImport ? "گروهی" : "دستی", HawalaReportLabels.Type(row.CommissionType), HawalaReportLabels.State(row.CommissionStatus), row.IsEstimate ? "بلی" : "نه", "", row.CommissionCurrency, "", "", "", row.RateDate?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "", row.BatchId?.ToString(CultureInfo.InvariantCulture) ?? "", row.Note };
            for (var i = 0; i < values.Length; i++) sheet.Cell(index, i + 1).Value = values[i];
            sheet.Cell(index, 11).Value = row.Amount;
            if (row.Commission.HasValue) sheet.Cell(index, 18).Value = row.Commission.Value;
            if (row.BasisUsd.HasValue) sheet.Cell(index, 20).Value = row.BasisUsd.Value;
            if (row.ExchangeRate.HasValue) sheet.Cell(index, 21).Value = row.ExchangeRate.Value;
            if (row.PerLakhRate.HasValue) sheet.Cell(index, 22).Value = row.PerLakhRate.Value;
            foreach (var column in new[] { 11, 18, 20, 22 }) sheet.Cell(index, column).Style.NumberFormat.Format = "[$-en-US]#,##0";
            sheet.Cell(index, 21).Style.NumberFormat.Format = "[$-en-US]0.########"; index++;
        }
        sheet.SheetView.FreezeRows(1); sheet.Columns().Width = 22;
        var summary = workbook.AddWorksheet("Summary"); summary.RightToLeft = true;
        foreach (var (header, i) in new[] { "ارز", "برآورد", "دریافتی", "پرداختی", "خالص همان ارز", "مجموع کمیشن‌ها (دریافتی + پرداختی)" }.Select((x, i) => (x, i))) summary.Cell(1, i + 1).Value = header;
        index = 2;
        foreach (var row in report.Summaries)
        { summary.Cell(index, 1).Value = row.Currency; summary.Cell(index, 2).Value = row.Estimate ? "بلی" : "نه"; summary.Cell(index, 3).Value = row.Income; summary.Cell(index, 4).Value = row.Expense; summary.Cell(index, 5).Value = row.Net; summary.Cell(index, 6).Value = row.Total; index++; }
        summary.Columns().Width = 24; summary.Range(2, 3, Math.Max(2, index), 6).Style.NumberFormat.Format = "[$-en-US]#,##0";
        var totals = workbook.AddWorksheet("Hawala totals"); totals.RightToLeft = true;
        totals.Cell(1, 1).Value = "نوع حواله"; totals.Cell(1, 2).Value = "ارز"; totals.Cell(1, 3).Value = "تعداد حواله"; totals.Cell(1, 4).Value = "مجموع مبلغ";
        index = 2;
        foreach (var row in report.Amounts)
        { totals.Cell(index, 1).Value = row.HawalaType; totals.Cell(index, 2).Value = row.Currency; totals.Cell(index, 3).Value = row.Count; totals.Cell(index, 4).Value = row.Amount; index++; }
        totals.Columns().Width = 24; totals.Range(2, 3, Math.Max(2, index), 4).Style.NumberFormat.Format = "[$-en-US]#,##0";
        var breakdown = workbook.AddWorksheet("Breakdown"); breakdown.RightToLeft = true;
        var groupHeaders = new[] { "گروه (تاریخ میلادی)", "نوع کمیشن", "وضعیت", "برآورد", "تعداد حواله", "کمیشن", "ارز" };
        for (var i = 0; i < groupHeaders.Length; i++) breakdown.Cell(1, i + 1).Value = groupHeaders[i];
        index = 2;
        foreach (var row in report.Groups)
        { breakdown.Cell(index, 1).Value = row.Label; breakdown.Cell(index, 2).Value = HawalaReportLabels.Type(row.Type); breakdown.Cell(index, 3).Value = HawalaReportLabels.State(row.State); breakdown.Cell(index, 4).Value = row.Estimate ? "بلی" : "نه"; breakdown.Cell(index, 5).Value = row.Count; if (row.Commission.HasValue) breakdown.Cell(index, 6).Value = row.Commission.Value; breakdown.Cell(index, 7).Value = row.Currency; index++; }
        breakdown.Columns().Width = 24; breakdown.Range(2, 5, Math.Max(2, index), 6).Style.NumberFormat.Format = "[$-en-US]#,##0";
        using var output = new MemoryStream(); workbook.SaveAs(output);
        return new("HawalaCommissionReport.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", output.ToArray());
    }
    public sealed class Recognition { public long TransactionId { get; set; } public long CorrespondentId { get; set; } }
    private sealed class Saved
    {
        public long HawalaId { get; set; } public long BatchId { get; set; } public string Scope { get; set; } = "";
        public string BatchStatus { get; set; } = ""; public bool Active { get; set; } public long CorrespondentId { get; set; }
        public string Correspondent { get; set; } = ""; public long TransactionId { get; set; } public decimal Amount { get; set; }
        public string Currency { get; set; } = ""; public decimal Commission { get; set; } public decimal Basis { get; set; }
        public decimal ExchangeRate { get; set; } public decimal PerLakhRate { get; set; } public DateTime? RateDate { get; set; }
        public long? LocationId { get; set; } public string? Location { get; set; }
    }
    private sealed class Transfer
    {
        public long Id { get; set; } public long Number { get; set; } public string Reference { get; set; } = "";
        public string Type { get; set; } = ""; public DateTime Date { get; set; } public long? CorrespondentId { get; set; }
        public string Correspondent { get; set; } = ""; public bool Periodic { get; set; } public long? SourceId { get; set; }
        public string Source { get; set; } = ""; public DateTime SourceDate { get; set; } public long? LocationId { get; set; }
        public bool ValidSource { get; set; } public bool LegacyOrigin { get; set; }
        public string Location { get; set; } = ""; public string Sender { get; set; } = ""; public string Receiver { get; set; } = "";
        public string Status { get; set; } = ""; public string Currency { get; set; } = ""; public decimal Amount { get; set; }
        public decimal? DirectAmount { get; set; } public string DirectCurrency { get; set; } = "";
        public decimal? AgentAmount { get; set; } public string AgentCurrency { get; set; } = ""; public bool Bulk { get; set; }
    }
}
