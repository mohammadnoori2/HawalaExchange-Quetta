using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Moq;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class HawalaActivityDescriptionTests(SqlServerPerformanceFixture fixture)
{
    [Theory]
    [InlineData("HawalaReceive", "دریافتی", "manual")]
    [InlineData("HawalaSend", "ارسالی", "manual")]
    [InlineData("HawalaReceive", "دریافتی", "empty")]
    [InlineData("HawalaSend", "ارسالی", "import")]
    public async Task Both_reports_export_only_hawala_parties_location_and_manual_note(string kind, string label, string noteKind)
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var key = Guid.NewGuid().ToString("N");
        var customer = new Customer { CustomerCode = "DESC-C-" + key, FullName = "Description customer" };
        var correspondent = new Correspondent { Code = "DESC-R-" + key, Name = "Description correspondent" };
        var place = new PaymentLocation { Name = "کابل برچی", NormalizedName = "DESC-" + key, Address = "Kabul", CreatedBy = fixture.UserId };
        context.AddRange(customer, correspondent, place); await context.SaveChangesAsync();
        var customerAccount = new Account { AccountCode = customer.CustomerCode, AccountName = customer.FullName, AccountType = "Customer", CustomerId = customer.Id };
        var correspondentAccount = new Account { AccountCode = correspondent.Code, AccountName = correspondent.Name, AccountType = "Correspondent", CorrespondentId = correspondent.Id };
        context.AddRange(customerAccount, correspondentAccount); await context.SaveChangesAsync();
        var day = new DateTime(2049, 1, 1);
        var fileName = key + ".xlsx";
        var note = noteKind == "manual" ? "لطفاً با گیرنده تماس بگیرید" : noteKind == "import" ? "آپلود گروهی از فایل " + fileName : null;
        var hawala = new Hawala { Number = 397000000 + place.Id, HawalaType = kind, SenderName = "عبدالله", ReceiverName = "عبدالجمیل", PaymentLocationId = place.Id,
            CorrespondentId = correspondent.Id, FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 100, ToAmount = 100, Notes = note, CreatedBy = fixture.UserId, CreatedAt = day.ToUniversalTime() };
        context.Hawalas.Add(hawala); await context.SaveChangesAsync();
        foreach (var account in new[] { customerAccount, correspondentAccount })
            context.LedgerEntries.Add(new LedgerEntry { AccountId = account.Id, CurrencyId = 2, HawalaId = hawala.Id, CreatedAt = day.ToUniversalTime(), TalabKar = 100, Description = "شرح حسابداری خودکار نباید نمایش داده شود" });
        if (noteKind == "import") context.HawalaImportBatches.Add(new HawalaImportBatch { FileName = fileName, FileHash = key, CorrespondentId = correspondent.Id, CreatedBy = fixture.UserId, RowCount = 1,
            Rows = [new HawalaImportRow { ExcelRowNumber = 2, HawalaId = hawala.Id }] });
        await context.SaveChangesAsync();
        string html = "";
        var converter = new Mock<IConverter>();
        converter.Setup(x => x.Convert(It.IsAny<IDocument>())).Callback<IDocument>(doc => html = ((HtmlToPdfDocument)doc).Objects[0].HtmlContent).Returns("%PDF"u8.ToArray());
        var service = new ExportService(context, converter.Object);
        var expected = $"حواله {label} از عبدالله به عبدالجمیل؛ محل پرداخت: کابل برچی" + (noteKind == "manual" ? "؛ یادداشت: " + note : "");
        foreach (var isCorrespondent in new[] { false, true })
        {
            var filter = new ExportFilterDto { CustomerId = isCorrespondent ? null : customer.Id, CorrespondentId = isCorrespondent ? correspondent.Id : null,
                FromDate = day.ToUniversalTime(), ToDate = day.AddDays(1).ToUniversalTime().AddTicks(-1), Search = "عبدالله" };
            var report = await service.GetCustomerActivitiesPreviewAsync(filter);
            Assert.Equal(expected, Assert.Single(report.Rows).DisplayDescription);
            var excel = await service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Excel);
            using var workbook = new XLWorkbook(new MemoryStream(excel.Content));
            Assert.Equal(expected, workbook.Worksheet(1).Cell(6, 3).GetString());
            await service.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Pdf);
            Assert.Contains(expected, html); Assert.DoesNotContain("شرح حسابداری خودکار", html); Assert.DoesNotContain(fileName, html);
        }
        Assert.Equal(note, (await context.Hawalas.FindAsync(hawala.Id))!.Notes);
    }
}
