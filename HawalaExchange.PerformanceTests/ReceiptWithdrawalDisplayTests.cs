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

public sealed class AccountOperationDisplayTests
{
    [Theory]
    [InlineData("Deposit", "رسید", "badge-success")]
    [InlineData("Withdraw", "برد", "badge-danger")]
    public void Name_badge_and_manual_note_follow_actual_operation_type(string type, string name, string badge)
    {
        Assert.Equal(name, AccountOperationDisplay.Name(type)); Assert.Equal(badge, AccountOperationDisplay.Badge(type));
        var text = AccountOperationDisplay.Description(type, "طرف", "صندوق", 10000, "AFN", "یادداشت دستی");
        Assert.StartsWith(name, text); Assert.EndsWith("؛ یادداشت: یادداشت دستی", text); Assert.Contains("10,000 AFN", text);
        Assert.DoesNotContain("یادداشت:", AccountOperationDisplay.Description(type, "طرف", "صندوق", 100, "USD", type == "Deposit" ? "ثبت رسید" : "ثبت برد"));
    }
}

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class ReceiptWithdrawalDisplayTests(SqlServerPerformanceFixture fixture)
{
    [Theory]
    [InlineData("Customer", "Deposit")]
    [InlineData("Customer", "Withdraw")]
    [InlineData("Correspondent", "Deposit")]
    [InlineData("Correspondent", "Withdraw")]
    public async Task Account_party_and_cash_show_actual_type_and_consistent_description_without_reposting(string partyType, string type)
    {
        await using var context = fixture.CreateContext(); using var bypass = context.BypassSubscriptionEnforcement();
        var key = Guid.NewGuid().ToString("N");
        var customer = new Customer { CustomerCode = "RW-C-" + key, FullName = "مشتری عبدالله" };
        var correspondent = new Correspondent { Code = "RW-R-" + key, Name = "نمایندگی کابل برچی" };
        context.AddRange(customer, correspondent); await context.SaveChangesAsync();
        var party = new Account { AccountCode = "RW-P-" + key, AccountName = partyType == "Customer" ? customer.FullName : correspondent.Name,
            AccountType = partyType, CustomerId = partyType == "Customer" ? customer.Id : null, CorrespondentId = partyType == "Correspondent" ? correspondent.Id : null };
        var cash = new Account { AccountCode = "RW-CASH-" + key, AccountName = "صندوق مرکزی", AccountType = "Cash" };
        context.AddRange(party, cash); await context.SaveChangesAsync();
        var day = new DateTime(2050, 1, 10);
        var operation = new AccountMoneyOperation { OperationType = type, AccountId = party.Id, CashOrBankAccountId = cash.Id,
            CurrencyId = 2, Amount = 10000, Description = "بابت تصفیه حساب <دستی>", OperationDate = day.ToUniversalTime(), CreatedBy = fixture.UserId };
        context.AccountMoneyOperations.Add(operation); await context.SaveChangesAsync();
        foreach (var account in new[] { party, cash })
        {
            var credit = type == "Deposit" ? account.Id == party.Id : account.Id == cash.Id;
            context.LedgerEntries.Add(new LedgerEntry { AccountId = account.Id, AccountMoneyOperationId = operation.Id, CurrencyId = 2,
                TalabKar = credit ? 10000 : 0, BadehKar = credit ? 0 : 10000, CreatedAt = day.ToUniversalTime(), Description = "شرح سیستمی با شماره " + operation.Id });
        }
        await context.SaveChangesAsync(); var count = await context.LedgerEntries.CountAsync(); context.ChangeTracker.Clear();
        var expected = AccountOperationDisplay.Description(type, party.AccountName, cash.AccountName, 10000, "USD", operation.Description);
        var journal = new JournalService(context);
        foreach (var account in new[] { party, cash })
        {
            var page = await journal.GetAccountOperationsPageAsync(new() { AccountId = account.Id, FromDate = day, ToDate = day });
            var item = Assert.Single(page.Items);
            Assert.Equal(type, item.MoneyOperationType); Assert.Equal(expected, item.SummarySentence);
            Assert.Equal("واریز / برداشت / پرداخت", item.SourceType); // edit/delete routing stays stable
            var details = await journal.GetAccountOperationDetailsAsync(account.Id, item.OperationKey);
            Assert.Equal(expected, details!.SummarySentence); Assert.Equal(type, details.MoneyOperationType);
        }
        string html = "";
        var converter = new Mock<IConverter>();
        converter.Setup(x => x.Convert(It.IsAny<IDocument>())).Callback<IDocument>(doc => html = ((HtmlToPdfDocument)doc).Objects[0].HtmlContent).Returns("%PDF"u8.ToArray());
        var exporter = new ExportService(context, converter.Object);
        var report = await exporter.GetCustomerActivitiesPreviewAsync(new ExportFilterDto { CustomerId = partyType == "Customer" ? customer.Id : null,
            CorrespondentId = partyType == "Correspondent" ? correspondent.Id : null, FromDate = day.ToUniversalTime(), ToDate = day.AddDays(1).ToUniversalTime().AddTicks(-1), Search = "تصفیه" });
        var row = Assert.Single(report.Rows); Assert.Equal(type, row.MoneyOperationType); Assert.Equal(expected, row.DisplayDescription);
        var excel = await exporter.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Excel);
        using var workbook = new XLWorkbook(new MemoryStream(excel.Content));
        Assert.Equal(expected, workbook.Worksheet(1).Cell(6, 3).GetString());
        await exporter.ExportCustomerActivitiesPreviewAsync(report, ExportFormat.Pdf);
        Assert.Contains(type == "Deposit" ? "#e6f7e6" : "#fde8e8", html); Assert.Contains("&lt;دستی&gt;", html); Assert.DoesNotContain("شرح سیستمی", html);
        Assert.Equal(count, await context.LedgerEntries.CountAsync()); Assert.Empty(context.ChangeTracker.Entries());
    }
}
