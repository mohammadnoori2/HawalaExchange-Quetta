namespace HawalaExchange.Application.Services;

/// <summary>
/// Converts internal codes to simple user-facing Dari text. Internal values must
/// never be shown directly in pages, reports, receipts or journal descriptions.
/// </summary>
public static class AppDisplayText
{
    public static string TransactionType(string? value) => value switch
    {
        "Exchange" or "MoneyExchange" => "تبدیل ارز",
        "Transfer" => "انتقال پول بین حساب‌ها",
        "Expense" => "ثبت مصرف",
        "Adjustment" => "اصلاح حساب",
        "OpeningBalance" => "ثبت موجودی اولیه",
        "HawalaSend" => "حواله ارسالی",
        "HawalaReceive" => "حواله دریافتی",
        "HawalaOther" => "حواله متفرقه",
        "CapitalInvestment" => "ثبت سرمایه",
        "Deposit" => "رسید",
        "Withdraw" or "Payment" => "برد",
        "CorrespondentSettlementConversion" => "تبدیل مانده نمایندگی به ارز توافقی",
        null or "" => "نوع مشخص نشده",
        _ => "عملیات مالی دیگر"
    };

    public static string Status(string? value, string? hawalaType = null) => value switch
    {
        "Pending" when hawalaType == "HawalaReceive" => "هنوز اجرا نشده",
        "Pending" => "در انتظار اجرا",
        "Paid" => "پرداخت و تکمیل شده",
        "Cancel" or "Cancelled" => "لغو شده",
        "Active" => "فعال",
        "Inactive" => "غیرفعال",
        "Suspended" => "موقتاً متوقف شده",
        "Expired" => "مدت آن پایان یافته",
        "Trial" => "دوره آزمایشی",
        null or "" => "وضعیت مشخص نشده",
        _ => "وضعیت دیگر"
    };

    public static string HawalaType(string? value) => value switch
    {
        "HawalaSend" => "حواله ارسالی",
        "HawalaReceive" => "حواله دریافتی",
        "HawalaOther" => "حواله متفرقه",
        null or "" => "نوع حواله مشخص نشده",
        _ => "نوع دیگر حواله"
    };

    public static string AccountType(string? value) => value?.ToLowerInvariant() switch
    {
        "cash" => "صندوق",
        "bank" => "حساب بانکی",
        "customer" or "مشتری" => "حساب مشتری",
        "correspondent" or "نماینده" or "نمایندگی" => "حساب نمایندگی",
        "income" => "حساب درآمد",
        "expense" => "حساب مصرف",
        "equity" => "حساب سرمایه",
        "pendinghawala" => "حساب حواله‌های اجرا نشده",
        "currencyconversionclearing" => "حساب داخلی تسویه تبدیل ارز",
        null or "" => "نوع حساب مشخص نشده",
        _ => "حساب دیگر"
    };

    public static string AuditAction(string? value) => value switch
    {
        "CREATE" => "ایجاد رکورد جدید",
        "UPDATE" => "ویرایش اطلاعات",
        "DELETE" => "حذف اطلاعات",
        "ARCHIVE" => "انتقال به بایگانی",
        "UNARCHIVE" => "خروج از بایگانی",
        "CANCEL" => "لغو عملیات",
        "CONVERT_HAWALAS_TO_SETTLEMENT" => "تبدیل حواله‌ها به ارز توافقی",
        "CONVERT_BALANCE_TO_SETTLEMENT" => "تبدیل مانده نمایندگی به ارز توافقی",
        null or "" => "عملیات مشخص نشده",
        _ => "عملیات مدیریتی"
    };

    public static string TransferMethod(string? value) => value?.ToLowerInvariant() switch
    {
        "cash" => "پرداخت نقدی",
        "bank" or "banktransfer" => "انتقال بانکی",
        "account" or "accounttransfer" => "انتقال بین حساب‌ها",
        "hawala" => "از طریق حواله",
        null or "" => "روش انتقال مشخص نشده",
        _ => "روش انتقال دیگر"
    };

    public static string ProfitStatus(string? value) => value?.ToLowerInvariant() switch
    {
        "calculated" or "realized" => "مفاد محاسبه شده",
        "pending" => "محاسبه مفاد در انتظار است",
        "notapplicable" or "none" => "این عملیات مفاد جداگانه ندارد",
        null or "" => "وضعیت مفاد مشخص نشده",
        _ => "وضعیت مفاد دیگر"
    };

    public static string ReferenceType(string? value) => value?.ToLowerInvariant() switch
    {
        "customer" => "مشتری",
        "correspondent" => "نمایندگی",
        null or "" => "بدون شخص مرتبط",
        _ => "مرجع دیگر"
    };
}
