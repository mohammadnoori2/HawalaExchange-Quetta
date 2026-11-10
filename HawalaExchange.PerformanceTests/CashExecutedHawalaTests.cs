using System.Text.Json;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace HawalaExchange.PerformanceTests;
[Collection(SqlServerPerformanceCollection.Name)]
public sealed class CashExecutedHawalaTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Cash_proof_filters_pagination_totals_reversals_and_exports_are_consistent()
    {
        await using var db=fixture.CreateContext(); using var bypass=db.BypassSubscriptionEnforcement();
        await using var tx=await db.Database.BeginTransactionAsync();
        var cash=await db.Accounts.FirstAsync(x=>x.AccountType=="Cash");
        var bank=new Account{AccountCode="CASH-REPORT-BANK",AccountName="Bank",AccountType="Bank"};db.Accounts.Add(bank);await db.SaveChangesAsync();
        var day=new DateTime(2052,3,15);var at=new DateTime(2052,3,14,19,30,0,DateTimeKind.Utc);long number=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Hawala Row(string type="HawalaReceive")=>new(){Number=number++,HawalaType=type,CorrespondentId=fixture.DestinationCorrespondent.Id,
            FromCurrencyId=1,ToCurrencyId=1,FromAmount=100,ToAmount=100,AgentCommissionAmount=8,AgentCommissionCurrencyId=2,
            CreatedAt=at.AddDays(-20),CreatedBy=fixture.UserId,PaidAt=at,PaidBy=fixture.UserId,PaidFromAccountId=cash.Id,
            ReceiverName="<Cash receiver>",Status="Paid",PaymentLocationId=fixture.OwnLocation.Id};
        var direct=Row();var reversed=Row();var bankPaid=Row();bankPaid.PaidFromAccountId=bank.Id;var noLedger=Row();var before=Row();before.PaidAt=at.AddTicks(-1);
        var sent=Row("HawalaSend");var historical=Row("HawalaSend");var billCancelled=Row("HawalaSend");
        db.Hawalas.AddRange(direct,reversed,bankPaid,noLedger,before,sent,historical,billCancelled);await db.SaveChangesAsync();
        LedgerEntry Credit(Hawala h,long account,long currency,decimal amount,string description)=>new(){HawalaId=h.Id,AccountId=account,CurrencyId=currency,TalabKar=amount,CreatedAt=h.PaidAt!.Value,Description=description};
        foreach(var h in new[]{direct,reversed,bankPaid,before}){
            db.LedgerEntries.AddRange(Credit(h,h.PaidFromAccountId!.Value,1,100,$"حواله دریافتی {h.Id}: پرداخت حواله از حساب انتخاب‌شده"),Credit(h,h.PaidFromAccountId.Value,2,8,$"حواله دریافتی {h.Id}: کمیشن قابل پرداخت به عامل پرداخت"));
        }
        db.LedgerEntries.AddRange(new LedgerEntry{HawalaId=reversed.Id,AccountId=cash.Id,CurrencyId=1,BadehKar=100,CreatedAt=at.AddHours(1),Description="reverse"},new LedgerEntry{HawalaId=reversed.Id,AccountId=cash.Id,CurrencyId=2,BadehKar=8,CreatedAt=at.AddHours(1),Description="reverse"});
        AgentPaymentBill Bill(Hawala h,string status)=>new(){CorrespondentId=fixture.DestinationCorrespondent.Id,Status=status,CreatedBy=fixture.UserId,PaidAt=status=="Historical"?null:at,PaidBy=fixture.UserId,CancelledAt=status=="Cancelled"?at.AddHours(2):null,Items=[new(){HawalaId=h.Id,IsActive=status!="Cancelled"}],SnapshotJson="{}"};
        var paidBill=Bill(sent,"Paid");var oldBill=Bill(historical,"Historical");var cancelledBill=Bill(billCancelled,"Cancelled");db.AgentPaymentBills.AddRange(paidBill,oldBill,cancelledBill);await db.SaveChangesAsync();
        foreach(var pair in new[]{(paidBill,sent),(oldBill,historical),(cancelledBill,billCancelled)}){
            var (bill,h)=pair;var credits=new[]{Credit(h,cash.Id,1,100,$"پرداخت بیل عامل #{bill.Id} به Agent"),Credit(h,cash.Id,2,8,$"پرداخت بیل عامل #{bill.Id} به Agent")};
            if(bill.Status!="Historical"){db.LedgerEntries.AddRange(credits);await db.SaveChangesAsync();}
            bill.SnapshotJson=JsonSerializer.Serialize(new{IsHistoricalReceipt=bill.Status=="Historical",RecipientName="Agent",PaymentAccounts=new[]{new{CurrencyId=1,AccountId=cash.Id},new{CurrencyId=2,AccountId=cash.Id}},PaymentLedgerEntryIds=credits.Select(x=>x.Id),Items=new[]{new{HawalaId=h.Id,h.Number,h.ReferenceNumber,h.ReceiverName,h.SenderName,PaymentLocation=fixture.OwnLocation.Name,CurrencyId=1,Amount=100,CommissionCurrencyId=2,AgentCommission=8}}});
            if(bill.Status=="Cancelled")db.LedgerEntries.AddRange(new LedgerEntry{HawalaId=h.Id,AccountId=cash.Id,CurrencyId=1,BadehKar=100,CreatedAt=at.AddHours(2),Description=$"برگشت پرداخت بیل عامل #{bill.Id}"},new LedgerEntry{HawalaId=h.Id,AccountId=cash.Id,CurrencyId=2,BadehKar=8,CreatedAt=at.AddHours(2),Description=$"برگشت پرداخت بیل عامل #{bill.Id}"});
        }
        await db.SaveChangesAsync();var ledgerCount=await db.LedgerEntries.CountAsync();
        HtmlToPdfDocument? pdfDocument=null;var pdf=new Mock<IConverter>();pdf.Setup(x=>x.Convert(It.IsAny<IDocument>())).Callback<IDocument>(x=>pdfDocument=(HtmlToPdfDocument)x).Returns("%PDF-test"u8.ToArray());
        var service=new CashExecutedHawalaService(db,pdf.Object);var filter=new CashExecutedHawalaFilter{From=day,To=day};
        var report=await service.GetAsync(filter,pageSize:1);Assert.Equal(2,report.HawalaCount);Assert.Equal(2,report.PaymentCount);Assert.Single(report.Page.Items);
        Assert.Equal(200,report.Totals.Single(x=>x.Currency=="AFN").Principal);Assert.Equal(16,report.Totals.Single(x=>x.Currency=="USD").Commission);
        Assert.All(report.Totals,x=>Assert.Equal(2,x.Count));
        Assert.NotEqual(report.Page.Items.First().Payment.EventKey,(await service.GetAsync(filter,2,1)).Page.Items.First().Payment.EventKey);
        filter.Mode="Bill";Assert.Equal(sent.Id,Assert.Single((await service.GetAsync(filter)).Page.Items).Payment.HawalaId);
        filter.Mode="Direct";Assert.Equal(direct.Id,Assert.Single((await service.GetAsync(filter)).Page.Items).Payment.HawalaId);
        filter.Search=direct.Number.ToString();Assert.Equal(1,(await service.GetAsync(filter)).HawalaCount);filter.Search="";
        filter.CorrespondentId=fixture.SourceCorrespondent.Id;Assert.Equal(0,(await service.GetAsync(filter)).HawalaCount);filter.CorrespondentId=null;
        filter.LocationId=fixture.RemoteLocation.Id;Assert.Equal(0,(await service.GetAsync(filter)).HawalaCount);filter.LocationId=null;
        filter.Mode="";filter.CurrencyId=2;Assert.Equal(16,Assert.Single((await service.GetAsync(filter)).Totals).Total);
        filter.CurrencyId=null;filter.AccountId=bank.Id;Assert.Equal(0,(await service.GetAsync(filter)).HawalaCount);filter.AccountId=null;
        filter.State="Reversed";var returned=await service.GetAsync(filter);Assert.Equal(2,returned.HawalaCount);Assert.Empty(returned.Totals);Assert.Equal(200,returned.ReversedTotals.Single(x=>x.Currency=="AFN").Principal);
        filter.State="Paid";var file=await service.ExportAsync(filter,ExportFormat.Excel);using var book=new XLWorkbook(new MemoryStream(file.Content));Assert.Equal(200,book.Worksheet("Summary").Cell(2,3).GetValue<decimal>());
        await service.ExportAsync(filter,ExportFormat.Pdf);Assert.Contains("&lt;Cash receiver&gt;",pdfDocument!.Objects.Single().HtmlContent);Assert.DoesNotContain("<Cash receiver>",pdfDocument.Objects.Single().HtmlContent);
        Assert.Equal(ledgerCount,await db.LedgerEntries.CountAsync());using(db.UseTenantScope(999999))Assert.Equal(0,(await service.GetAsync(filter)).HawalaCount);
    }
    [Fact]
    public async Task Bill_same_currency_requires_full_payment_and_reissue_does_not_double_active_totals()
    {
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var cash=await db.Accounts.FirstAsync(x=>x.AccountType=="Cash");var at=new DateTime(2053,3,15,8,0,0,DateTimeKind.Utc);
        var h=new Hawala{Number=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),HawalaType="HawalaSend",CorrespondentId=fixture.DestinationCorrespondent.Id,FromCurrencyId=1,ToCurrencyId=1,FromAmount=100,ToAmount=100,AgentCommissionAmount=8,AgentCommissionCurrencyId=1,Status="Paid",CreatedBy=fixture.UserId,CreatedAt=at};
        db.Hawalas.Add(h);await db.SaveChangesAsync();
        AgentPaymentBill Bill()=>new(){CorrespondentId=fixture.DestinationCorrespondent.Id,Status="Paid",CreatedBy=fixture.UserId,PaidAt=at,PaidBy=fixture.UserId,SnapshotJson="{}",Items=[new(){HawalaId=h.Id}]};
        var bill=Bill();db.AgentPaymentBills.Add(bill);await db.SaveChangesAsync();
        var credit=new LedgerEntry{HawalaId=h.Id,AccountId=cash.Id,CurrencyId=1,TalabKar=100,CreatedAt=at,Description="bill cash"};db.LedgerEntries.Add(credit);await db.SaveChangesAsync();
        string Snapshot(long id)=>JsonSerializer.Serialize(new{RecipientName="Agent",PaymentAccounts=new[]{new{CurrencyId=1,AccountId=cash.Id}},PaymentLedgerEntryIds=new[]{id},Items=new[]{new{HawalaId=h.Id,h.Number,CurrencyId=1,Amount=100,CommissionCurrencyId=1,AgentCommission=8}}});
        bill.SnapshotJson=Snapshot(credit.Id);await db.SaveChangesAsync();var service=new CashExecutedHawalaService(db);var filter=new CashExecutedHawalaFilter{From=at.Date,To=at.Date};
        Assert.Equal(0,(await service.GetAsync(filter)).HawalaCount);
        credit.TalabKar=108;await db.SaveChangesAsync();var report=await service.GetAsync(filter);Assert.Equal(108,Assert.Single(report.Totals).Total);Assert.Single(report.Page.Items.Single().Amounts);
        bill.Status="Cancelled";bill.CancelledAt=at.AddHours(1);bill.Items[0].IsActive=false;
        db.LedgerEntries.Add(new(){HawalaId=h.Id,AccountId=cash.Id,CurrencyId=1,BadehKar=108,CreatedAt=at.AddHours(1),Description=$"برگشت پرداخت بیل عامل #{bill.Id}"});await db.SaveChangesAsync();
        var replacement=Bill();db.AgentPaymentBills.Add(replacement);await db.SaveChangesAsync();var second=new LedgerEntry{HawalaId=h.Id,AccountId=cash.Id,CurrencyId=1,TalabKar=108,CreatedAt=at,Description="bill cash reissue"};db.LedgerEntries.Add(second);await db.SaveChangesAsync();replacement.SnapshotJson=Snapshot(second.Id);await db.SaveChangesAsync();
        report=await service.GetAsync(filter);Assert.Equal(1,report.HawalaCount);Assert.Equal(108,Assert.Single(report.Totals).Total);
        filter.State="All";report=await service.GetAsync(filter);Assert.Equal(1,report.HawalaCount);Assert.Equal(2,report.PaymentCount);Assert.Equal(108,Assert.Single(report.Totals).Total);Assert.Equal(108,Assert.Single(report.ReversedTotals).Total);
    }
}
