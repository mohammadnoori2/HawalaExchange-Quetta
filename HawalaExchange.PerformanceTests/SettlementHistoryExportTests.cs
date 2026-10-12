using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Services;
using Moq;
using WkHtmlToPdfDotNet;

namespace HawalaExchange.PerformanceTests;

public sealed class SettlementHistoryExportTests
{
    [Fact]
    public async Task Excel_keeps_all_currencies_typed_rates_and_previous_snapshots_in_separate_sheets()
    {
        var exporter=Create(new SynchronizedConverter(new PdfTools()));
        var file=await exporter.ExportAsync(1,10,ExportFormat.Excel);
        using var book=new XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal(3,book.Worksheets.Count);Assert.All(book.Worksheets,s=>Assert.False(s.RightToLeft));
        Assert.Equal("PKR",book.Worksheet("Details").Cell(3,8).GetString());
        Assert.Equal(228m,book.Worksheet("Details").Cell(3,9).GetValue<decimal>());
        Assert.Equal(XLDataType.Number,book.Worksheet("Details").Cell(3,9).DataType);
        Assert.Equal(65m,book.Worksheet("Previous_1").Cell(2,9).GetValue<decimal>());
        Assert.Empty(book.Worksheet("Previous_1").Cell(2,3).GetString());
        Assert.Equal("0",book.Worksheet("Summary").Cell(3,2).Style.NumberFormat.Format);
        Assert.Equal("=literal",book.Worksheet("Details").Cell(2,4).GetString());Assert.False(book.Worksheet("Details").Cell(2,4).HasFormula);
        var directory=Environment.GetEnvironmentVariable("HAWALA_SETTLEMENT_EXPORT_TEST_DIR");
        if(!string.IsNullOrWhiteSpace(directory))await File.WriteAllBytesAsync(Path.Combine(directory,"settlement-history.xlsx"),file.Content);
    }
    [Fact]
    public async Task Pdf_uses_native_converter_with_all_currencies_and_history_revisions()
    {
        var file=await Create(new SynchronizedConverter(new PdfTools())).ExportAsync(1,10,ExportFormat.Pdf);
        Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString(file.Content,0,4));Assert.True(file.Content.Length>1000);
        var directory=Environment.GetEnvironmentVariable("HAWALA_SETTLEMENT_EXPORT_TEST_DIR");
        if(!string.IsNullOrWhiteSpace(directory))await File.WriteAllBytesAsync(Path.Combine(directory,"settlement-history.pdf"),file.Content);
    }
    private static CorrespondentSettlementHistoryExportService Create(WkHtmlToPdfDotNet.Contracts.IConverter converter)
    {
        var day=new DateTime(2026,10,11);
        var afn=new CorrespondentSettlementHistoryItemDto{HawalaId=1,HawalaNumber=2172,ReferenceNumber="A44559",SenderName="=literal",ReceiverName="گیرنده",PaymentLocationName="کابل برچی",RateDate=day,SourceCurrencyId=1,SourceCurrencyCode="AFN",SourceAmount=100000,ExchangeRate=66,TargetAmount=1515,PrincipalTargetAmount=1515.151515m,UsdAmount=1515.151515m};
        var pkr=new CorrespondentSettlementHistoryItemDto{HawalaId=2,HawalaNumber=2173,ReferenceNumber="A44560",SenderName="فرستنده",ReceiverName="گیرنده",PaymentLocationName="قندوز",RateDate=day,SourceCurrencyId=4,SourceCurrencyCode="PKR",SourceAmount=60000,ExchangeRate=228,TargetAmount=263,PrincipalTargetAmount=263.157894m,UsdAmount=263.157894m};
        var previous=new CorrespondentSettlementHistoryItemDto{HawalaId=1,HawalaNumber=2172,RateDate=day,SourceCurrencyId=1,SourceCurrencyCode="AFN",SourceAmount=100000,ExchangeRate=65,TargetAmount=1538,PrincipalTargetAmount=1538.461538m,UsdAmount=1538.461538m};
        var history=new CorrespondentSettlementHistoryDto{ConversionId=10,CreatedAt=day.AddHours(20),UserName="کاربر آزمایشی",Status="Active",TargetCurrencyCode="USD",HawalaCount=2,Items=[afn,pkr],Revisions=[new(){CreatedAt=day,UserName="کاربر",Items=[previous]},new(){CreatedAt=day.AddHours(20),UserName="کاربر",Items=[afn,pkr]}]};
        var service=new Mock<ICorrespondentSettlementService>();service.Setup(x=>x.GetHistoryDetailsAsync(1,10,It.IsAny<CancellationToken>())).ReturnsAsync(history);
        var settings=new Mock<ICompanySettingService>();settings.Setup(x=>x.GetAsync()).ReturnsAsync(new CompanySettingDto{CompanyName="صرافی آزمایشی",UsePersianCalendar=true});
        return new(service.Object,settings.Object,converter);
    }
}
