using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;
public class FinancialReceiptTests
{
    static MoneyExchangeOperationDto Exchange()=>new(){Id=17,ExchangeDate=new DateTime(2026,11,24,9,32,0),FromAccountName="SECRET_SELL_ACCOUNT",ToAccountName="SECRET_BUY_ACCOUNT",FromCurrencyCode="USD",ToCurrencyCode="AFN",FromAmount=100,ToAmount=6514,ExchangeRate=65.14m,RateBaseCurrencyCode="USD",RateQuoteCurrencyCode="AFN",RealizedProfit=987654,Description="یادداشت دستی"};
    [Fact]
    public void Exchange_receipt_omits_accounts_and_profit_and_preserves_rate_precision()
    {
        var dto=Exchange();var receipt=FinancialReceiptFactory.Exchange(dto);var content=JsonSerializer.Serialize(receipt);
        Assert.DoesNotContain("SECRET_",content);Assert.DoesNotContain("987654",content);
        Assert.Equal("1 USD = 65.14 AFN",receipt.Rows.Single(x=>x.Label=="نرخ تبدیل").Value);
        Assert.Equal("100 USD",receipt.Rows.Single(x=>x.Label=="مبلغ فروش").Value);
        Assert.Equal("6,514 AFN",receipt.Rows.Single(x=>x.Label=="مبلغ دریافت").Value);
        Assert.Equal("SECRET_SELL_ACCOUNT",dto.FromAccountName);
    }
    [Theory]
    [InlineData("Deposit","رسید دریافت پول")]
    [InlineData("Withdraw","رسید برداشت پول")]
    public void Operation_receipts_distinguish_deposit_and_withdrawal(string type,string title)
    {
        var receipt=FinancialReceiptFactory.Operation(new(){Id=1,OperationType=type,Amount=1234.5m,CurrencyCode="AFN",AccountName="نمایندگی",CashOrBankAccountName="صندوق"});
        Assert.Equal(title,receipt.Title);Assert.Equal("1,235 AFN",receipt.Rows.Single(x=>x.Amount).Value);
        Assert.Contains(receipt.Rows,x=>x.Value=="نمایندگی");Assert.Contains(receipt.Rows,x=>x.Value=="صندوق");
    }
    [Fact]
    public void Preview_and_reprint_state_do_not_change_the_source_document()
    {
        var dto=Exchange();var state=new FinancialReceiptPreview();var document=FinancialReceiptFactory.Exchange(dto);
        state.Show(document);state.Close();state.Show(document);Assert.Same(document,state.Current);Assert.Equal(100,dto.FromAmount);Assert.Equal(6514,dto.ToAmount);
        Assert.Throws<InvalidOperationException>(()=>FinancialReceiptFactory.Operation(new(){OperationType="Unknown"}));
    }
    [Theory]
    [InlineData("exchange")]
    [InlineData("deposit")]
    [InlineData("withdraw")]
    [InlineData("expense")]
    public async Task Actual_print_markup_encodes_notes_and_renders_summary_receipts(string kind)
    {
        FinancialReceiptDocument receipt=kind switch {
            "exchange"=>FinancialReceiptFactory.Exchange(Exchange()),
            "expense"=>FinancialReceiptFactory.Expense(new(){Id=9,ExpenseDate=DateTime.UtcNow,Title="کرایه دفتر",PaidFromAccountName="صندوق",CurrencyCode="AFN",Amount=4500}),
            _=>FinancialReceiptFactory.Operation(new(){Id=12,OperationType=kind=="deposit"?"Deposit":"Withdraw",OperationDate=DateTime.UtcNow,AccountName="نماینده: متفرقه کابل",CashOrBankAccountName="صندوق",CurrencyCode="AFN",Amount=17100})};
        receipt=receipt with {Note="یادداشت دستی <script>alert(1)</script>"};
        using var services=new ServiceCollection().AddLogging().AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,Mock.Of<ICurrentTenant>())).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var rendered=await renderer.RenderComponentAsync<FinancialOperationReceipt>(ParameterView.FromDictionary(new Dictionary<string,object?> { ["Document"]=receipt,["Company"]=new CompanySettingDto {CompanyName="صرافی آزمایشی",Address="کابل",PhoneNumber="0700000000"} }));
            var html=rendered.ToHtmlString();Assert.Contains("hawala-receipt",html);Assert.Contains("receipt-table",html);Assert.DoesNotContain("<script",html);Assert.DoesNotContain("SECRET_",html);
            var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"HawalaExchange.Web")))directory=directory.Parent;
            var output=Path.Combine(directory!.FullName,"HawalaExchange.Web","obj","financial-receipts-qa");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,kind+".html"),html);
        });
    }
}
