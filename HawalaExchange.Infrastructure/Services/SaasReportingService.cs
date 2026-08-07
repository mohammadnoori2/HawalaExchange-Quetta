using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class SaasReportingService(ApplicationDbContext context) : ISaasReportingService
{
    public async Task<SaasManagementReportDto> GetReportAsync(SaasReportFilterDto filter, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var from = ToUtc(filter.FromDate) ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);
        var to = (ToUtc(filter.ToDate)?.Date.AddDays(1)) ?? now.Date.AddDays(1);
        var query = context.Tenants.AsNoTracking().Where(x => !x.IsArchived);
        if (filter.FromDate.HasValue) query = query.Where(x => x.CreatedAt >= from);
        if (filter.ToDate.HasValue) query = query.Where(x => x.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = filter.Search.Trim(); query = query.Where(x => x.Name.Contains(term) || (x.LegalName != null && x.LegalName.Contains(term))); }

        var rows = await query.Select(x => new SaasTenantReportRowDto
        {
            TenantId = x.Id, TenantName = x.Name, TenantIsActive = x.IsActive, CreatedAt = x.CreatedAt,
            PlanId = x.Subscriptions.OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id).Select(s => (long?)s.PlanId).FirstOrDefault(),
            PlanName = x.Subscriptions.OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id).Select(s => s.Plan.Name).FirstOrDefault() ?? "بدون پلن",
            Status = x.Subscriptions.OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id).Select(s => (SubscriptionStatus?)s.Status).FirstOrDefault(),
            EndAt = x.Subscriptions.OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id).Select(s => (DateTime?)s.EndAt).FirstOrDefault(),
            SubscriptionPrice = x.Subscriptions.OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id).Select(s => s.AgreedPrice).FirstOrDefault(),
            CurrencyCode = x.Subscriptions.OrderByDescending(s => s.StartAt).ThenByDescending(s => s.Id).Select(s => s.CurrencyCode).FirstOrDefault() ?? "",
            Users = context.Users.IgnoreQueryFilters().Count(u => u.TenantId == x.Id && !u.IsPlatformUser && u.IsActive),
            Branches = context.Branches.IgnoreQueryFilters().Count(b => b.TenantId == x.Id && !b.IsArchived),
            Transactions = context.Transactions.IgnoreQueryFilters().Count(t => t.TenantId == x.Id && t.CreatedAt >= from && t.CreatedAt < to),
            StorageBytes = context.Documents.IgnoreQueryFilters().Where(d => d.TenantId == x.Id).Sum(d => (long?)d.FileSizeBytes) ?? 0,
            Outstanding = context.SubscriptionInvoices.Where(i => i.TenantId == x.Id && (i.Status == SubscriptionInvoiceStatus.Issued || i.Status == SubscriptionInvoiceStatus.PartiallyPaid || i.Status == SubscriptionInvoiceStatus.Overdue)).Sum(i => (decimal?)(i.TotalAmount - i.PaidAmount)) ?? 0
        }).ToListAsync(cancellationToken);

        if (filter.PlanId.HasValue)
        {
            rows = rows.Where(x => x.PlanId == filter.PlanId).ToList();
        }
        if (filter.Status.HasValue) rows = rows.Where(x => x.Status == filter.Status).ToList();
        if (!string.IsNullOrWhiteSpace(filter.CurrencyCode)) rows = rows.Where(x => x.CurrencyCode.Equals(filter.CurrencyCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        rows = Sort(rows, filter.SortBy, filter.SortDescending).ToList();
        var totalRows = rows.Count;

        var tenantCreated = await context.Tenants.AsNoTracking().Where(x => !x.IsArchived && x.CreatedAt >= from && x.CreatedAt < to).Select(x => x.CreatedAt).ToListAsync(cancellationToken);
        var userCreated = await context.Users.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsPlatformUser && x.CreatedAt >= from && x.CreatedAt < to).Select(x => x.CreatedAt).ToListAsync(cancellationToken);
        var subscriptionCreated = await context.TenantSubscriptions.AsNoTracking().Where(x => x.CreatedAt >= from && x.CreatedAt < to).Select(x => x.CreatedAt).ToListAsync(cancellationToken);
        var months = Enumerable.Range(0, Math.Max(1, ((to.Year - from.Year) * 12 + to.Month - from.Month) + 1))
            .Select(i => new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(i)).Take(36).ToList();
        var growth = months.Select(m => new SaasGrowthPointDto
        {
            Month = m, NewTenants = tenantCreated.Count(x => x.Year == m.Year && x.Month == m.Month),
            NewUsers = userCreated.Count(x => x.Year == m.Year && x.Month == m.Month), NewSubscriptions = subscriptionCreated.Count(x => x.Year == m.Year && x.Month == m.Month)
        }).ToList();

        var invoices = await context.SubscriptionInvoices.AsNoTracking().Where(x => x.IssuedAt >= from && x.IssuedAt < to && x.Status != SubscriptionInvoiceStatus.Cancelled)
            .Select(x => new { x.IssuedAt, x.CurrencyCode, x.TotalAmount, x.PaidAmount }).ToListAsync(cancellationToken);
        var payments = await context.SubscriptionPayments.AsNoTracking().Where(x => x.Status == SubscriptionPaymentStatus.Paid && x.PaidAt >= from && x.PaidAt < to)
            .Select(x => new { PaidAt = x.PaidAt!.Value, x.CurrencyCode, x.Amount }).ToListAsync(cancellationToken);
        var revenue = invoices.Select(x => (x.IssuedAt.Year, x.IssuedAt.Month, x.CurrencyCode)).Concat(payments.Select(x => (x.PaidAt.Year, x.PaidAt.Month, x.CurrencyCode))).Distinct()
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.CurrencyCode).Select(k => new SaasRevenuePointDto
            {
                Month = new DateTime(k.Year, k.Month, 1), CurrencyCode = k.CurrencyCode,
                Billed = invoices.Where(x => x.IssuedAt.Year == k.Year && x.IssuedAt.Month == k.Month && x.CurrencyCode == k.CurrencyCode).Sum(x => x.TotalAmount),
                Collected = payments.Where(x => x.PaidAt.Year == k.Year && x.PaidAt.Month == k.Month && x.CurrencyCode == k.CurrencyCode).Sum(x => x.Amount),
                Outstanding = invoices.Where(x => x.IssuedAt.Year == k.Year && x.IssuedAt.Month == k.Month && x.CurrencyCode == k.CurrencyCode).Sum(x => x.TotalAmount - x.PaidAmount)
            }).ToList();
        if (!string.IsNullOrWhiteSpace(filter.CurrencyCode)) revenue = revenue.Where(x => x.CurrencyCode.Equals(filter.CurrencyCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        var overdue = await context.SubscriptionInvoices.AsNoTracking().Where(x => x.Status == SubscriptionInvoiceStatus.Overdue && x.DueAt >= from && x.DueAt < to)
            .OrderBy(x => x.DueAt).Select(x => new SaasOverdueReportRowDto { InvoiceNumber=x.InvoiceNumber,TenantName=x.Tenant.Name,DueAt=x.DueAt,Total=x.TotalAmount,Paid=x.PaidAmount,Balance=x.TotalAmount-x.PaidAmount,CurrencyCode=x.CurrencyCode }).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(filter.CurrencyCode)) overdue = overdue.Where(x => x.CurrencyCode.Equals(filter.CurrencyCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var pageSize = Math.Clamp(filter.PageSize, 10, 1000); var page = Math.Max(1, filter.Page);
        return new SaasManagementReportDto
        {
            GeneratedAt=now, TotalTenants=totalRows, ActiveTenants=rows.Count(x => x.TenantIsActive),InactiveTenants=rows.Count(x => !x.TenantIsActive),
            ExpiredTenants=rows.Count(x => x.Status==SubscriptionStatus.Expired),SuspendedTenants=rows.Count(x => x.Status==SubscriptionStatus.Suspended),TotalUsers=rows.Sum(x=>x.Users),TotalRows=totalRows,
            Growth=growth,Revenue=revenue,OverdueInvoices=overdue,Tenants=rows.Skip((page-1)*pageSize).Take(pageSize).ToList()
        };
    }

    public async Task<SaasReportExportDto> ExportExcelAsync(SaasReportFilterDto filter, CancellationToken cancellationToken = default)
    {
        var exportFilter = new SaasReportFilterDto { FromDate=filter.FromDate,ToDate=filter.ToDate,PlanId=filter.PlanId,Status=filter.Status,CurrencyCode=filter.CurrencyCode,Search=filter.Search,SortBy=filter.SortBy,SortDescending=filter.SortDescending,Page=1,PageSize=100 };
        var first = await GetReportAsync(exportFilter, cancellationToken); var all = first.Tenants.ToList();
        for (var page = 2; all.Count < first.TotalRows; page++) { exportFilter.Page=page; var next=await GetReportAsync(exportFilter,cancellationToken); if(next.Tenants.Count==0)break; all.AddRange(next.Tenants); }
        first.Tenants=all;
        return new SaasReportExportDto { Content=BuildWorkbook(first,filter),FileName=$"saas-management-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx",ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
    }

    private static IEnumerable<SaasTenantReportRowDto> Sort(IEnumerable<SaasTenantReportRowDto> rows, string sort, bool desc)
    {
        Func<SaasTenantReportRowDto, object> key = sort switch { "created" => x=>x.CreatedAt, "end"=>x=>x.EndAt??DateTime.MaxValue, "users"=>x=>x.Users, "revenue"=>x=>x.SubscriptionPrice, "outstanding"=>x=>x.Outstanding, _=>x=>x.TenantName };
        return desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
    }
    private static DateTime? ToUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value,DateTimeKind.Utc) : null;

    private static byte[] BuildWorkbook(SaasManagementReportDto report, SaasReportFilterDto filter)
    {
        using var stream=new MemoryStream(); using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            Entry(zip,"[Content_Types].xml",ContentTypes(6)); Entry(zip,"_rels/.rels",RootRelationships()); Entry(zip,"xl/workbook.xml",WorkbookXml()); Entry(zip,"xl/_rels/workbook.xml.rels",WorkbookRelationships(6)); Entry(zip,"xl/styles.xml",Styles());
            Entry(zip,"xl/worksheets/sheet1.xml",Sheet(new[]{new[]{"گزارش مدیریتی SaaS",""},new[]{"تاریخ تولید",report.GeneratedAt.ToString("yyyy-MM-dd HH:mm")},new[]{"بازه",$"{filter.FromDate:yyyy-MM-dd} تا {filter.ToDate:yyyy-MM-dd}"},new[]{"کل صرافی‌ها",report.TotalTenants.ToString()},new[]{"فعال",report.ActiveTenants.ToString()},new[]{"غیرفعال",report.InactiveTenants.ToString()},new[]{"منقضی",report.ExpiredTenants.ToString()},new[]{"تعلیق",report.SuspendedTenants.ToString()},new[]{"کل کاربران",report.TotalUsers.ToString()}},titleRows:1));
            Entry(zip,"xl/worksheets/sheet2.xml",Sheet(new[]{new[]{"صرافی","وضعیت صرافی","پلن","وضعیت اشتراک","ایجاد","پایان","کاربران","شعبه‌ها","تراکنش‌ها","فضای اسناد (MB)","قیمت","ارز","مطالبات"}}.Concat(report.Tenants.Select(x=>new[]{x.TenantName,x.TenantIsActive?"فعال":"غیرفعال",x.PlanName,x.Status?.ToString()??"-",x.CreatedAt.ToString("yyyy-MM-dd"),x.EndAt?.ToString("yyyy-MM-dd")??"-",x.Users.ToString(),x.Branches.ToString(),x.Transactions.ToString(),(x.StorageBytes/1048576m).ToString(CultureInfo.InvariantCulture),x.SubscriptionPrice.ToString(CultureInfo.InvariantCulture),x.CurrencyCode,x.Outstanding.ToString(CultureInfo.InvariantCulture)})).ToArray(),numericColumns:[6,7,8,9,10,12]));
            Entry(zip,"xl/worksheets/sheet3.xml",Sheet(new[]{new[]{"ماه","ارز","فاکتور شده","وصول شده","باقی‌مانده"}}.Concat(report.Revenue.Select(x=>new[]{x.Month.ToString("yyyy-MM"),x.CurrencyCode,x.Billed.ToString(CultureInfo.InvariantCulture),x.Collected.ToString(CultureInfo.InvariantCulture),x.Outstanding.ToString(CultureInfo.InvariantCulture)})).ToArray(),numericColumns:[2,3,4]));
            Entry(zip,"xl/worksheets/sheet4.xml",Sheet(new[]{new[]{"شماره فاکتور","صرافی","سررسید","مبلغ","پرداخت","باقی‌مانده","ارز"}}.Concat(report.OverdueInvoices.Select(x=>new[]{x.InvoiceNumber,x.TenantName,x.DueAt.ToString("yyyy-MM-dd"),x.Total.ToString(CultureInfo.InvariantCulture),x.Paid.ToString(CultureInfo.InvariantCulture),x.Balance.ToString(CultureInfo.InvariantCulture),x.CurrencyCode})).ToArray(),numericColumns:[3,4,5]));
            Entry(zip,"xl/worksheets/sheet5.xml",Sheet(new[]{new[]{"ماه","صرافی جدید","کاربر جدید","اشتراک جدید"}}.Concat(report.Growth.Select(x=>new[]{x.Month.ToString("yyyy-MM"),x.NewTenants.ToString(),x.NewUsers.ToString(),x.NewSubscriptions.ToString()})).ToArray(),numericColumns:[1,2,3]));
            Entry(zip,"xl/worksheets/sheet6.xml",Sheet(new[]{new[]{"کنترل","مقدار واقعی","مقدار مورد انتظار","وضعیت"},new[]{"تعداد ردیف‌های صرافی",report.Tenants.Count.ToString(),report.TotalRows.ToString(),report.Tenants.Count==report.TotalRows?"OK":"FAIL"},new[]{"مبالغ منفی مطالبات",report.Tenants.Count(x=>x.Outstanding<0).ToString(),"0",report.Tenants.All(x=>x.Outstanding>=0)?"OK":"FAIL"}}));
        } return stream.ToArray();
    }
    private static void Entry(ZipArchive zip,string path,string content){var e=zip.CreateEntry(path,CompressionLevel.Fastest);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));w.Write(content);}
    private static string Esc(string? x)=>SecurityElement.Escape(x??"")??"";
    private static string Sheet(string[][] rows,int[]? numericColumns=null,int titleRows=0)
    {
        var n=numericColumns?.ToHashSet()??[];
        var columnCount=Math.Max(1,rows.Max(x=>x.Length));
        var b=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView rightToLeft=\"1\" showGridLines=\"0\" workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"18\"/><cols>");
        for(var c=0;c<columnCount;c++)
        {
            var maxLength=rows.Select(x=>c<x.Length?x[c]?.Length??0:0).DefaultIfEmpty(0).Max();
            var width=Math.Clamp(maxLength+3,10,32);
            b.Append($"<col min=\"{c+1}\" max=\"{c+1}\" width=\"{width}\" customWidth=\"1\"/>");
        }
        b.Append("</cols><sheetData>");
        for(var r=0;r<rows.Length;r++){b.Append($"<row r=\"{r+1}\">");for(var c=0;c<rows[r].Length;c++){var cell=$"{Col(c)}{r+1}";var style=r<titleRows?1:r==0?2:n.Contains(c)?3:0;if(n.Contains(c)&&decimal.TryParse(rows[r][c],NumberStyles.Any,CultureInfo.InvariantCulture,out var value))b.Append($"<c r=\"{cell}\" s=\"{style}\"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>");else b.Append($"<c r=\"{cell}\" s=\"{style}\" t=\"inlineStr\"><is><t>{Esc(rows[r][c])}</t></is></c>");}b.Append("</row>");}
        b.Append("</sheetData><autoFilter ref=\"A1:").Append(Col(columnCount-1)).Append(Math.Max(1,rows.Length)).Append("\"/><pageSetup orientation=\"landscape\" paperSize=\"9\" fitToWidth=\"1\" fitToHeight=\"0\"/></worksheet>");return b.ToString();
    }
    private static string Col(int i){var s="";for(i++;i>0;i=(i-1)/26)s=(char)('A'+(i-1)%26)+s;return s;}
    private static string ContentTypes(int n)=>"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"+string.Concat(Enumerable.Range(1,n).Select(i=>$"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))+"</Types>";
    private static string RootRelationships()=>"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
    private static string WorkbookXml()=>"<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView/></bookViews><sheets><sheet name=\"خلاصه\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"صرافی‌ها\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"درآمد\" sheetId=\"3\" r:id=\"rId3\"/><sheet name=\"معوقات\" sheetId=\"4\" r:id=\"rId4\"/><sheet name=\"رشد\" sheetId=\"5\" r:id=\"rId5\"/><sheet name=\"کنترل‌ها\" sheetId=\"6\" r:id=\"rId6\"/></sheets><calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>";
    private static string WorkbookRelationships(int n)=>"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"+string.Concat(Enumerable.Range(1,n).Select(i=>$"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>"))+$"<Relationship Id=\"rId{n+1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
    private static string Styles()=>"<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"3\"><font><sz val=\"10\"/><name val=\"Tahoma\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"16\"/><name val=\"Tahoma\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"10\"/><name val=\"Tahoma\"/></font></fonts><fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF17365D\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2F75B5\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"2\"><border/><border><bottom style=\"thin\"><color rgb=\"FFD9E2F3\"/></bottom></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"right\"/></xf><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>";
}
