namespace HawalaExchange.Application.Security;

public sealed record PermissionModule(string Key,string Name,string[] Actions);

public static class PermissionCatalog
{
    private static PermissionModule Crud(string key,string name)=>new(key,name,["View","Create","Edit","Delete","Export"]);
    public static readonly IReadOnlyList<PermissionModule> Modules = [
        new("Dashboard","داشبورد",["View"]), Crud("Hawalas","حواله‌ها"),
        new("HawalaExecution","اجرای حواله و تاریخچه اجرا",["View","Execute","Export"]),
        new("BulkImports","آپلود گروهی",["View","Create","Edit","Delete","Export"]),
        Crud("Customers","مشتریان"),Crud("Correspondents","نمایندگی‌ها"),Crud("Accounts","حساب‌ها"),
        Crud("PaymentLocations","محل‌های پرداخت"),Crud("Branches","شعبه‌ها"),Crud("Currencies","ارزها"),
        Crud("Deposits","رسید و برد"),Crud("Transfers","انتقال‌ها"),Crud("Exchanges","تبادله اسعار"),
        new("AedDeals","معاملات درهم",["View","Create","Edit","Delete","Reverse","Export"]),
        Crud("Capital","سرمایه و برداشت مالک"),Crud("Expenses","مصارف"),Crud("Ledger","دفتر کل"),
        new("Journal","روزنامچه",["View","Edit","Close","Export"]),
        new("Periods","بستن دوره نمایندگی",["View","Close","Export"]),
        new("IncomingCommission","کمیشن دریافتی",["View","Calculate","Reverse","Export"]),
        new("ForwardingCommission","کمیشن ارسالی",["View","Calculate","Reverse","Export"]),
        new("DestinationCommission","کمیشن پرداختی",["View","Calculate","Reverse","Export"]),
        new("Settlement","تبدیل به ارز توافقی و نرخ‌های نمایندگی",["View","Create","Edit","Reverse","Export"]),
        new("Reports","گزارش‌های عمومی",["View","Export"]),
        new("HawalaReports","گزارش حواله و کمیشن",["View","Export"]),
        new("CustomerReports","گزارش فعالیت مشتری",["View","Export"]),
        new("CorrespondentReports","گزارش فعالیت نمایندگی",["View","Export"]),
        new("AccountReports","گزارش فعالیت حساب",["View","Export"]),
        new("BalanceSheet","بیلانس شیت",["View","Export"]),new("ProfitLoss","مفاد و ضرر",["View","Export"]),
        Crud("Rates","نرخ تبادله"),Crud("AccountLimits","سقف بدهکاری"),Crud("CashAlerts","هشدار موجودی صندوق"),
        Crud("Documents","اسناد"),new("Settings","تنظیمات شرکت",["View","Edit"]),
        new("Users","کاربران",["View","Create","Edit","Delete","ManagePermissions"]),new("AuditLogs","لاگ‌های سیستم",["View","Export"]),
        Crud("Transactions","معاملات")
    ];
    public static readonly HashSet<string> Keys=Modules.SelectMany(m=>m.Actions.Select(a=>$"{m.Key}.{a}")).ToHashSet(StringComparer.Ordinal);
    public static string ActionName(string action)=>action switch {"View"=>"مشاهده","Create"=>"ایجاد","Edit"=>"ویرایش","Delete"=>"حذف / لغو","Export"=>"خروجی / چاپ / اشتراک","Execute"=>"اجرای حواله","Calculate"=>"محاسبه کمیشن","Reverse"=>"برگشت عملیات","Close"=>"بستن","ManagePermissions"=>"تغییر سطح دسترسی",_=>action};
    public static HashSet<string> Normalize(IEnumerable<string> permissions)
    {
        var keys=permissions.ToHashSet(StringComparer.Ordinal);
        if(keys.Any(x=>!Keys.Contains(x)))throw new InvalidOperationException("صلاحیت انتخاب‌شده معتبر نیست.");
        foreach(var key in keys.ToArray())keys.Add(key[..key.LastIndexOf('.')]+".View");
        return keys;
    }
    public static HashSet<string> Defaults(IEnumerable<string> roles)
    {
        var set=roles.ToHashSet(StringComparer.Ordinal);
        if(set.Contains("Admin")||set.Contains("SuperAdmin"))return new(Keys,StringComparer.Ordinal);
        if(set.Contains("Manager"))return new(Keys,StringComparer.Ordinal);
        if(set.Contains("Supervisor"))return Keys.Where(x=>x.EndsWith(".View")||x.EndsWith(".Export")).ToHashSet(StringComparer.Ordinal);
        if(set.Contains("Cashier"))return Normalize(new[]{"Dashboard.View","Hawalas.View","Hawalas.Create","Hawalas.Export","HawalaExecution.Execute","Customers.View","Customers.Create","Customers.Edit","Correspondents.View","Accounts.View","PaymentLocations.View","Exchanges.Create","Deposits.Create","Transfers.Create","Journal.View","Rates.View"});
        return new(StringComparer.Ordinal);
    }
    public static string? PagePermission(string path)
    {
        path=path.Split('?')[0].Trim('/').ToLowerInvariant();
        if(path.StartsWith("platform")||path.StartsWith("tenants")||path is "profile" or "login" or "error" or "not-found"||path.StartsWith("account/"))return null;
        if(path=="register")return "Users.Create";
        if(path.StartsWith("correspondents/")||path.StartsWith("commissions/"))
        {
            if(path.Contains("hawala-import"))return "BulkImports.View";
            if(path.EndsWith("outgoing-commission"))return "DestinationCommission.View";
            if(path.EndsWith("forwarding-commission"))return "ForwardingCommission.View";
            if(path.EndsWith("commission")||path.StartsWith("commissions/"))return "IncomingCommission.View";
            if(path.Contains("settlement-history"))return "Settlement.View";
            if(path.Contains("/periods"))return "Periods.View";
            if(path.EndsWith("/range"))return "CorrespondentReports.View";
        }
        var module=path.Split('/')[0] switch
        {""=>"Dashboard","hawala-transactions" or "hawala-receive"=>"Hawalas","hawala-pending"=>"HawalaExecution","customers"=>"Customers","correspondents"=>"Correspondents","accounts"=>"Accounts","payment-locations"=>"PaymentLocations","branches"=>"Branches","currencies"=>"Currencies","deposit-withdraw"=>"Deposits","transfers"=>"Transfers","money-exchange"=>"Exchanges","aed-transactions"=>"AedDeals","capital-investment"=>"Capital","expenses"=>"Expenses","ledger"=>"Ledger","daily-journal"=>"Journal","exchangerates"=>"Rates","account-limits"=>"AccountLimits","cash-balance-alerts"=>"CashAlerts","documents"=>"Documents","company-settings"=>"Settings","users"=>"Users","auditlogs"=>"AuditLogs","transactions"=>"Transactions","reports"=>path switch{"reports/hawalas"=>"HawalaReports","reports/customer-activities"=>"CustomerReports","reports/correspondent-activities"=>"CorrespondentReports","reports/account-activities"=>"AccountReports","reports/balance-sheet"=>"BalanceSheet","reports/profit-loss"=>"ProfitLoss",_=>"Reports"},_=>null};
        return module is null?null:module+".View";
    }
}
