using System.Globalization;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Helpers;

namespace HawalaExchange.Web.Services;

public sealed record FinancialReceiptRow(string Label, string Value, bool Amount = false, bool Ltr = false);
public sealed record FinancialReceiptDocument(long Id, string Title, string Module, DateTime Date,
    IReadOnlyList<FinancialReceiptRow> Rows, string? Note, string Signature);

public static class FinancialReceiptFactory
{
    private static string Amount(decimal value,string currency) => $"{MoneyFormatHelper.Format(value)} {currency}".Trim();
    private static string? Note(string? value,params string[] defaults) => string.IsNullOrWhiteSpace(value) || defaults.Contains(value.Trim()) ? null : value.Trim();
    public static FinancialReceiptDocument Exchange(MoneyExchangeOperationDto dto)
    {
        // Account names, balances, costs and profit are deliberately absent from the printable model.
        var rate=dto.ExchangeRate.ToString("0.########",CultureInfo.InvariantCulture);
        var quotation=string.IsNullOrWhiteSpace(dto.RateBaseCurrencyCode)||string.IsNullOrWhiteSpace(dto.RateQuoteCurrencyCode)
            ? rate : $"1 {dto.RateBaseCurrencyCode} = {rate} {dto.RateQuoteCurrencyCode}";
        return new(dto.Id,"رسید تبدیل پول","Exchanges",dto.ExchangeDate,
            [new("مبلغ فروش",Amount(dto.FromAmount,dto.FromCurrencyCode),true,true),new("مبلغ دریافت",Amount(dto.ToAmount,dto.ToCurrencyCode),true,true),new("نرخ تبدیل",quotation,false,true)],
            Note(dto.Description,"ثبت تبدیل پول"),"امضاء یا اثر انگشت");
    }
    public static FinancialReceiptDocument Operation(AccountMoneyOperationDto dto)
    {
        if(dto.OperationType is not ("Deposit" or "Withdraw"))throw new InvalidOperationException("نوع رسید معتبر نیست.");
        var deposit=dto.OperationType=="Deposit";
        return new(dto.Id,deposit?"رسید دریافت پول":"رسید برداشت پول","Deposits",dto.OperationDate,
            [new("حساب",dto.AccountName),new(deposit?"صندوق / بانک دریافت":"صندوق / بانک پرداخت",dto.CashOrBankAccountName),new("مبلغ",Amount(dto.Amount,dto.CurrencyCode),true,true)],
            Note(dto.Description,"ثبت رسید","ثبت برد"),deposit?"امضاء تحویل‌دهنده / دریافت‌کننده":"امضاء یا اثر انگشت دریافت‌کننده");
    }
    public static FinancialReceiptDocument Expense(ExpenseDto dto) => new(dto.Id,"رسید مصرف","Expenses",dto.ExpenseDate,
        [new("عنوان مصرف",dto.Title),new("حساب پرداخت",dto.PaidFromAccountName),new("مبلغ",Amount(dto.Amount,dto.CurrencyCode),true,true)],
        Note(dto.Description),"امضاء یا اثر انگشت دریافت‌کننده");
}

public interface IFinancialReceiptPreview
{
    FinancialReceiptDocument? Current { get; }
    event Action? Changed;
    void Show(FinancialReceiptDocument document);
    void Close();
}
public sealed class FinancialReceiptPreview : IFinancialReceiptPreview
{
    public FinancialReceiptDocument? Current {get;private set;}
    public event Action? Changed;
    public void Show(FinancialReceiptDocument document) { Current=document;Changed?.Invoke(); }
    public void Close() { Current=null;Changed?.Invoke(); }
}
