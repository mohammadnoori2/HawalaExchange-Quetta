using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.Infrastructure.Services;

public sealed class CorrespondentSettlementHistoryExportService(ICorrespondentSettlementService settlements,ICompanySettingService settings,IConverter converter)
    : ICorrespondentSettlementHistoryExportService
{
    public async Task<SelectedRecordsFileDto> ExportAsync(long correspondentId,long conversionId,ExportFormat format)
    {
        var data=await settlements.GetHistoryDetailsAsync(correspondentId,conversionId);
        var company=await settings.GetAsync();
        string Date(DateTime date){if(!company.UsePersianCalendar)return date.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture);var calendar=new PersianCalendar();return FormattableString.Invariant($"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}");}
        static string N(decimal value)=>value.ToString("N0",CultureInfo.InvariantCulture);
        static string R(decimal value)=>value.ToString("0.########",CultureInfo.InvariantCulture);
        static string E(string? value)=>WebUtility.HtmlEncode(value??"");
        var name=$"Correspondent_{correspondentId}_Settlement_{conversionId}";
        var headers=new[]{"روز حواله","نمبر حواله","متفرقه","فرستنده","گیرنده","محل پرداخت","مبلغ قبل تبدیل","ارز مبدأ","نرخ","مبلغ تبدیل حسابداری","ارز توافقی","اصل حواله به ارز توافقی","مبنای USD کمیشن"};
        if(format==ExportFormat.Excel)
        {
            using var book=new XLWorkbook();var summary=book.AddWorksheet("Summary");summary.RightToLeft=false;
            summary.Cell(1,1).Value=company.CompanyName;summary.Cell(2,1).Value="تاریخچه تبدیل نمایندگی";summary.Cell(2,2).Value=correspondentId;
            summary.Cell(3,1).Value="شماره عملیات";summary.Cell(3,2).Value=conversionId;
            summary.Cell(4,1).Value="تاریخ اجرا";summary.Cell(4,2).Value=Date(data.CreatedAt.ToLocalTime());
            summary.Cell(5,1).Value="کاربر";summary.Cell(5,2).Value=data.UserName;
            summary.Cell(6,1).Value="وضعیت";summary.Cell(6,2).Value=Status(data.Status);
            var sh=new[]{"روز حواله","ارز مبدأ","مجموع قبل تبدیل","مجموع حسابداری به ارز توافقی","ارز توافقی","اصل حواله تبدیل‌شده","مبنای USD"};
            for(var c=0;c<sh.Length;c++)summary.Cell(8,c+1).Value=sh[c];
            var r=9;foreach(var g in data.Items.GroupBy(x=>new{x.RateDate,x.SourceCurrencyCode}))
            {
                summary.Cell(r,1).Value=Date(g.Key.RateDate);summary.Cell(r,2).Value=g.Key.SourceCurrencyCode;
                summary.Cell(r,3).Value=g.Sum(x=>x.SourceAmount);summary.Cell(r,4).Value=g.Sum(x=>x.TargetAmount);summary.Cell(r,5).Value=data.TargetCurrencyCode;
                if(g.All(x=>x.PrincipalTargetAmount.HasValue))summary.Cell(r,6).Value=g.Sum(x=>x.PrincipalTargetAmount!.Value);
                if(g.All(x=>x.UsdAmount.HasValue))summary.Cell(r,7).Value=g.Sum(x=>x.UsdAmount!.Value);
                r++;
            }
            void Detail(string title,IReadOnlyList<CorrespondentSettlementHistoryItemDto> items)
            {
                var sheet=book.AddWorksheet(title);sheet.RightToLeft=false;
                for(var c=0;c<headers.Length;c++)sheet.Cell(1,c+1).Value=headers[c];
                var row=2;foreach(var item in items)
                {sheet.Cell(row,1).Value=Date(item.RateDate);sheet.Cell(row,2).Value=item.HawalaNumber?.ToString(CultureInfo.InvariantCulture)??"";sheet.Cell(row,3).Value=item.ReferenceNumber;sheet.Cell(row,4).Value=item.SenderName;sheet.Cell(row,5).Value=item.ReceiverName;sheet.Cell(row,6).Value=item.PaymentLocationName;sheet.Cell(row,7).Value=item.SourceAmount;sheet.Cell(row,8).Value=item.SourceCurrencyCode;sheet.Cell(row,9).Value=item.ExchangeRate;sheet.Cell(row,10).Value=item.TargetAmount;sheet.Cell(row,11).Value=data.TargetCurrencyCode;if(item.PrincipalTargetAmount.HasValue)sheet.Cell(row,12).Value=item.PrincipalTargetAmount.Value;if(item.UsdAmount.HasValue)sheet.Cell(row,13).Value=item.UsdAmount.Value;row++;}
                sheet.SheetView.FreezeRows(1);sheet.Row(1).Style.Font.Bold=true;sheet.Range(1,1,row-1,headers.Length).SetAutoFilter();sheet.Column(9).Style.NumberFormat.Format="[$-en-US]0.########";
            }
            Detail("Details",data.Items);
            for(var i=0;i<data.Revisions.Count-1;i++)Detail($"Previous_{i+1}",data.Revisions[i].Items);
            foreach(var sheet in book.Worksheets)
            {
                sheet.ShowGridLines=false;sheet.CellsUsed().Style.Font.FontName="Arial";sheet.CellsUsed().Style.Font.FontSize=10;
                sheet.CellsUsed().Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
                sheet.CellsUsed().Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
                sheet.CellsUsed().Style.NumberFormat.Format="0";
                foreach(var column in sheet.Name=="Summary"?new[]{3,4,6,7}:new[]{7,10,12,13})
                    sheet.Column(column).Style.NumberFormat.Format="[$-en-US]#,##0";
                if(sheet.Name!="Summary")sheet.Column(9).Style.NumberFormat.Format="[$-en-US]0.########";
                sheet.Columns().AdjustToContents(9,38);
                var header=sheet.Range(sheet.Name=="Summary"?8:1,1,sheet.Name=="Summary"?8:1,sheet.Name=="Summary"?7:headers.Length);
                header.Style.Font.Bold=true;header.Style.Fill.BackgroundColor=XLColor.FromHtml("#e2e8f0");
                header.Style.Alignment.WrapText=true;header.Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
                sheet.Row(sheet.Name=="Summary"?8:1).Height=45;
                if(sheet.Name=="Summary")sheet.Column(1).Width=30;
            }
            using var stream=new MemoryStream();book.SaveAs(stream);return new(name+".xlsx","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",stream.ToArray());
        }
        if(format!=ExportFormat.Pdf)throw new InvalidOperationException("نوع خروجی معتبر نیست.");
        var html=new StringBuilder("<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><style>body{font-family:Tahoma,Arial;font-size:10px}table{width:100%;border-collapse:collapse;margin-bottom:16px}th,td{border:1px solid #cbd5e1;padding:5px;text-align:center}th{background:#e2e8f0}thead{display:table-header-group}tr{page-break-inside:avoid}.n{direction:ltr}h3{page-break-after:avoid}</style><body>");
        html.Append($"<h2>{E(company.CompanyName)}</h2><h3>تبدیل نمایندگی #{correspondentId} — عملیات #{conversionId}</h3><p>{E(Date(data.CreatedAt.ToLocalTime()))} · {E(data.UserName)} · {E(Status(data.Status))} · ارز توافقی: {E(data.TargetCurrencyCode)}</p>");
        void Table(IReadOnlyList<CorrespondentSettlementHistoryItemDto> items)
        {
            foreach(var day in items.GroupBy(x=>x.RateDate))
            {
                html.Append($"<h3>{E(Date(day.Key))}</h3><p>");
                foreach(var g in day.GroupBy(x=>x.SourceCurrencyCode))html.Append($"{N(g.Sum(x=>x.SourceAmount))} {E(g.Key)} → {N(g.Sum(x=>x.TargetAmount))} {E(data.TargetCurrencyCode)} ؛ ");
                html.Append("</p><table><thead><tr><th>نمبر</th><th>متفرقه</th><th>فرستنده</th><th>گیرنده</th><th>محل پرداخت</th><th>مبلغ اصلی</th><th>نرخ</th><th>مبلغ تبدیل</th><th>اصل به ارز توافقی</th><th>مبنای USD</th></tr></thead><tbody>");
                foreach(var row in day)html.Append($"<tr><td>{row.HawalaNumber}</td><td>{E(row.ReferenceNumber)}</td><td>{E(row.SenderName)}</td><td>{E(row.ReceiverName)}</td><td>{E(row.PaymentLocationName)}</td><td class='n'>{N(row.SourceAmount)} {E(row.SourceCurrencyCode)}</td><td class='n'>{R(row.ExchangeRate)}</td><td class='n'>{N(row.TargetAmount)} {E(data.TargetCurrencyCode)}</td><td class='n'>{(row.PrincipalTargetAmount.HasValue?N(row.PrincipalTargetAmount.Value):"—")}</td><td class='n'>{(row.UsdAmount.HasValue?N(row.UsdAmount.Value):"—")}</td></tr>");
                html.Append("</tbody></table>");
            }
        }
        Table(data.Items);
        foreach(var previous in data.Revisions.Take(Math.Max(0,data.Revisions.Count-1))){html.Append($"<h3>سابقه قبل از اصلاح: {E(Date(previous.CreatedAt.ToLocalTime()))} · {E(previous.UserName)}</h3>");Table(previous.Items);}
        html.Append("</body></html>");
        var doc=new HtmlToPdfDocument{GlobalSettings={PaperSize=PaperKind.A4,Orientation=Orientation.Landscape,DocumentTitle=name,Margins=new MarginSettings{Top=10,Bottom=10,Left=8,Right=8}},Objects={new ObjectSettings{HtmlContent=html.ToString(),WebSettings={DefaultEncoding="utf-8"},FooterSettings={Center="[page] / [toPage]",FontSize=8}}}};
        return new(name+".pdf","application/pdf",converter.Convert(doc));
    }
    private static string Status(string value)=>value switch{"Active"=>"فعال","Adjusted"=>"اصلاح‌شده","Reversed"=>"برگشت‌شده",_=>value};
}
