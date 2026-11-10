using HawalaExchange.Application.DTOs;
namespace HawalaExchange.Application.Services;
public static class MoneyExchangeMode
{
    public const string CorrespondentCredit="CorrespondentCredit";
    public const string CorrespondentDebt="CorrespondentDebt";
    public static bool IsCorrespondent(string? type)=>type is CorrespondentCredit or CorrespondentDebt;
    public static bool UsesPartyLedger(string? type)=>type=="Customer" || type==CorrespondentCredit;
    public static string Label(string? type)=>type switch {
        "Customer"=>"مشتری",CorrespondentCredit=>"تبدیل طلبکاری نمایندگی",CorrespondentDebt=>"تبدیل بدهکاری نمایندگی",_=>"سرمایه صراف" };
    public static string ForAccount(string? accountType)=>accountType switch {
        "Customer"=>"Customer","Correspondent"=>CorrespondentCredit,_=>"Treasury" };
    public static bool Allows(string type,AccountDto account)=>type=="Customer" ? account.AccountType=="Customer" :
        IsCorrespondent(type) ? account.AccountType=="Correspondent" : account.AccountType is "Cash" or "Bank";
}
