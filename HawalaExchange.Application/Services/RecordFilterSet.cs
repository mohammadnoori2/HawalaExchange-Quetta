using System.Globalization;
using System.Reflection;

namespace HawalaExchange.Application.Services;

public sealed class RecordFilterRule
{
    public string Field { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Exact { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public DateTime FromValue { get => From ?? DateTime.MinValue; set => From = value == DateTime.MinValue ? null : value; }
    public DateTime ToValue { get => To ?? DateTime.MinValue; set => To = value == DateTime.MinValue ? null : value; }
    public bool Active => !string.IsNullOrWhiteSpace(Text) || Min.HasValue || Max.HasValue || From.HasValue || To.HasValue;
}
public sealed record RecordFilterField(string Name, string Label, Type Type, PropertyInfo Property)
{
    public bool IsDate => Type == typeof(DateTime);
    public bool IsBoolean => Type == typeof(bool);
    public bool IsNumber => Type == typeof(decimal) || Type == typeof(int) || Type == typeof(long) || Type == typeof(double);
}

/// <summary>Typed, allowlisted filters for complete in-memory datasets, before pagination.</summary>
public sealed class RecordFilterSet
{
    public List<RecordFilterRule> Rules { get; } = [];
    public string Search { get; set; } = "";
    public int Count => Rules.Count(x => x.Active);
    public string Error => Rules.Any(x => x.Min > x.Max || x.From?.Date > x.To?.Date) ? "بازه فیلتر معتبر نیست؛ حداقل باید کمتر از حداکثر باشد." : "";
    public void Clear() { Rules.Clear(); Search = ""; }
    public RecordFilterRule For(string field, bool exact = false)
    {
        var rule = Rules.FirstOrDefault(x => x.Field == field);
        if (rule is not null) return rule;
        rule = new RecordFilterRule { Field = field, Exact = exact };
        Rules.Add(rule);
        return rule;
    }
    public IEnumerable<T> Apply<T>(IEnumerable<T> items)
    {
        if (Error != "") return [];
        var fields = Fields<T>().ToDictionary(x => x.Name);
        var active = Rules.Where(x => x.Active).ToArray();
        var search = Search.Trim();
        return items.Where(item => (search.Length == 0 || fields.Values.Where(x => !x.IsBoolean && !x.IsDate).Any(x =>
            Convert.ToString(x.Property.GetValue(item), CultureInfo.InvariantCulture)?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)) &&
            active.All(rule => fields.TryGetValue(rule.Field, out var field) && Matches(field, field.Property.GetValue(item), rule)));
    }
    private static bool Matches(RecordFilterField field, object? value, RecordFilterRule rule)
    {
        if (value == null) return false;
        if (field.IsNumber)
        { var amount = Convert.ToDecimal(value, CultureInfo.InvariantCulture); return (!rule.Min.HasValue || amount >= rule.Min) && (!rule.Max.HasValue || amount <= rule.Max); }
        if (field.IsDate)
        {
            var date = (DateTime)value;
            // Match the same local date that AppDateTime displays. Do not reinterpret
            // calendar dates or unspecified timestamps as UTC.
            if (date.Kind == DateTimeKind.Utc) date = date.ToLocalTime();
            return (!rule.From.HasValue || date.Date >= rule.From.Value.Date) && (!rule.To.HasValue || date.Date <= rule.To.Value.Date);
        }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return rule.Exact || field.IsBoolean ? text.Equals(rule.Text.Trim(), StringComparison.OrdinalIgnoreCase) : text.Contains(rule.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    public static IReadOnlyList<RecordFilterField> Fields<T>() => Catalog<T>.Fields;
    private static class Catalog<T>
    {
        public static readonly IReadOnlyList<RecordFilterField> Fields = typeof(T).GetProperties().Where(x => x.GetIndexParameters().Length == 0 && Labels.ContainsKey(x.Name))
            .Select(x => new RecordFilterField(x.Name, Labels[x.Name], Nullable.GetUnderlyingType(x.PropertyType) ?? x.PropertyType, x))
            .Where(x => x.Type == typeof(string) || x.IsBoolean || x.IsDate || x.IsNumber).ToArray();
    }
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["CreatedAt"]="تاریخ ثبت", ["ModifiedAt"]="تاریخ ویرایش", ["PaidAt"]="تاریخ پرداخت", ["ClosedAt"]="تاریخ بستن",
        ["Code"]="کد", ["Name"]="نام", ["FullName"]="نام کامل", ["FatherName"]="نام پدر", ["CustomerCode"]="کد مشتری",
        ["CustomerType"]="نوع مشتری", ["PhoneNumber"]="شماره تماس", ["Phone"]="تماس", ["Address"]="آدرس", ["TazkiraNumber"]="نمبر تذکره", ["Remarks"]="یادداشت",
        ["AccountName"]="حساب", ["AccountCode"]="کد حساب", ["AccountType"]="نوع حساب", ["ReferenceType"]="نوع مرجع", ["ReferenceName"]="صاحب حساب",
        ["CustomerFullName"]="مشتری", ["CustomerName"]="مشتری", ["CorrespondentName"]="نمایندگی", ["BranchName"]="شعبه",
        ["CurrencyCode"]="ارز", ["CurrencyName"]="نام ارز", ["FromCurrencyCode"]="ارز مبدا", ["ToCurrencyCode"]="ارز مقصد",
        ["CommissionCurrencyCode"]="ارز کمیشن", ["AgentCommissionCurrencyCode"]="ارز کمیشن نمایندگی", ["SettlementCurrencyCode"]="ارز توافقی",
        ["Amount"]="مبلغ", ["FromAmount"]="مبلغ مبدا", ["ToAmount"]="مبلغ مقصد", ["ExchangeRate"]="نرخ تبدیل", ["Rate"]="نرخ", ["BuyRate"]="نرخ خرید", ["SellRate"]="نرخ فروش",
        ["TalabKar"]="طلبکاری", ["BadehKar"]="بدهکاری", ["Balance"]="مانده", ["Credit"]="طلبکاری", ["Debit"]="بدهکاری",
        ["BadehkarLimit"]="سقف بدهکاری", ["CurrentDebt"]="بدهکاری فعلی", ["AvailableLimit"]="حد باقی‌مانده", ["IsOverLimit"]="بالاتر از سقف",
        ["TransactionNo"]="شماره تراکنش", ["TransactionType"]="نوع تراکنش", ["Status"]="وضعیت", ["ReferenceNumber"]="نمبر متفرقه", ["Description"]="شرح",
        ["FromAccountName"]="حساب مبدا", ["ToAccountName"]="حساب مقصد", ["TransferMethod"]="روش انتقال", ["TransferDate"]="تاریخ انتقال",
        ["OperationType"]="نوع عملیات", ["OperationDate"]="تاریخ عملیات", ["OperationNumber"]="شماره عملیات", ["OperationNo"]="شماره عملیات", ["Notes"]="یادداشت", ["CashOrBankAccountName"]="حساب صندوق یا بانک",
        ["ExpenseDate"]="تاریخ مصرف", ["Title"]="عنوان", ["ExpenseAccountName"]="حساب مصرف", ["PaidFromAccountName"]="حساب پرداخت", ["CashAccountName"]="حساب صندوق",
        ["InvestmentDate"]="تاریخ سرمایه", ["InvestmentType"]="نوع سرمایه", ["InvestorName"]="نام سرمایه‌گذار", ["OwnerName"]="نام مالک", ["InvestmentNumber"]="شماره سرمایه",
        ["ReceivingAccountName"]="حساب دریافت", ["CapitalAccountName"]="حساب سرمایه", ["ProfitCurrencyCode"]="ارز مفاد", ["ProfitAmount"]="مفاد", ["ProfitRate"]="نرخ مفاد",
        ["IsActive"]="فعال", ["IsArchived"]="بایگانی", ["IsEnabled"]="فعال", ["IsDefault"]="پیش‌فرض", ["CommissionMethod"]="روش کمیشن", ["CommissionValue"]="نرخ کمیشن", ["IsWithdrawal"]="برداشت مالک", ["CancelledAt"]="تاریخ لغو", ["ProfitCurrencyAmount"]="مبلغ ارز مفاد",
        ["DecimalPlaces"]="اعشار ارز", ["Symbol"]="علامت ارز", ["QuotationPriority"]="اولویت نرخ", ["RateDate"]="تاریخ نرخ", ["EffectiveDate"]="تاریخ اعتبار نرخ", ["CreatedByName"]="ثبت‌کننده", ["SourceCurrencyCode"]="ارز اصلی", ["TargetCurrencyCode"]="ارز هدف",
        ["FileName"]="نام فایل", ["FileSize"]="اندازه فایل", ["FileType"]="نوع فایل", ["EntityType"]="نوع ارتباط سند", ["DocumentType"]="نوع سند", ["UploadedAt"]="تاریخ آپلود",
        ["Email"]="ایمیل", ["Country"]="کشور", ["City"]="شهر", ["ManagerName"]="مدیر", ["ContactPerson"]="شخص تماس",
        ["Number"]="نمبر حواله", ["SenderName"]="فرستنده", ["ReceiverName"]="گیرنده", ["PaymentLocationName"]="محل پرداخت", ["PaymentLocation"]="محل پرداخت",
        ["CommissionAmount"]="کمیشن", ["AgentCommissionAmount"]="کمیشن نمایندگی", ["HawalaType"]="نوع حواله", ["SettlementState"]="وضعیت تبدیل",
        ["IsSystemGenerated"]="ثبت خودکار", ["PeriodNumber"]="شماره دوره", ["PeriodFrom"]="شروع دوره", ["PeriodTo"]="ختم دوره", ["HawalaCount"]="تعداد حواله",
        ["TotalBaseAfn"]="مبنای کمیشن", ["TotalCommissionAfn"]="کمیشن محاسبه‌شده", ["TotalCommissionUsd"]="کمیشن USD", ["CommissionScope"]="نوع کمیشن",
        ["TotalRows"]="تعداد ردیف", ["ImportedRows"]="ردیف ثبت‌شده", ["FailedRows"]="ردیف ناموفق", ["SourceType"]="نوع عملیات", ["DocumentNumber"]="شماره سند", ["SummarySentence"]="شرح عملیات",
        ["UserName"]="نام کاربر", ["Role"]="نقش", ["TableName"]="بخش", ["Action"]="عملیات", ["MinimumBalance"]="حداقل موجودی", ["Threshold"]="حد هشدار",
        ["DealDate"]="تاریخ معامله", ["DealNumber"]="شماره معامله", ["AedAmount"]="مبلغ درهم", ["UsdAmount"]="مبلغ دالر", ["RemainingAmount"]="مبلغ باقی‌مانده",
        ["SourceCorrespondentName"]="نمایندگی فرستنده", ["DubaiCorrespondentName"]="نمایندگی دبی", ["OriginalAmount"]="مبلغ اصلی", ["ConvertedAmount"]="مبلغ تبدیل‌شده",
        ["TotalFinalUsd"]="مجموع USD نهایی", ["TotalDeclaredUsd"]="مجموع USD اظهارشده", ["TotalProfitUsd"]="مجموع مفاد USD", ["AedPerUsdRate"]="نرخ AED/USD", ["Note"]="یادداشت",
        ["ExchangeDate"]="تاریخ تبادله", ["ExternalFeeAmount"]="کمیشن بیرونی", ["CostAmount"]="بهای تمام‌شده", ["RealizedProfit"]="مفاد تحقق‌یافته", ["ExchangeProfitAmount"]="مفاد تبادله", ["DeferredAmount"]="مفاد منتظر", ["ProfitStatus"]="وضعیت مفاد",
        ["CurrentBalance"]="مانده فعلی", ["AvailableDebt"]="سقف باقی‌مانده", ["IsBelowMinimum"]="کمبود موجودی", ["NotifyAllUsers"]="اطلاع به همه", ["ShowInApp"]="اعلان داخل سیستم", ["UpdatedAt"]="تاریخ تغییر",
        ["Date"]="تاریخ", ["FromCurrency"]="ارز مبدا", ["ToCurrency"]="ارز مقصد", ["Commission"]="کمیشن", ["CommissionCurrency"]="ارز کمیشن", ["RowCount"]="تعداد ردیف", ["ConfirmedAt"]="تاریخ تأیید", ["UploadedBy"]="آپلودکننده",
        ["TotalCommission"]="مجموع کمیشن", ["TotalAgentCommission"]="مجموع کمیشن نمایندگی", ["NetCommission"]="خالص کمیشن", ["TotalDebit"]="مجموع بدهکار", ["TotalCredit"]="مجموع طلبکار", ["BalanceType"]="وضعیت مانده"
    };
    public static string DisplayOption(string field, string value) => field switch
    {
        "TransactionType" or "SourceType" or "OperationType" => OperationLabel(value),
        "HawalaType" => AppDisplayText.HawalaType(value), "Status" => StatusLabel(value), "AccountType" => AppDisplayText.AccountType(value),
        "CommissionScope" => HawalaExchange.Application.DTOs.HawalaReportLabels.Type(value == "Origin" ? "Forwarding" : value),
        "CustomerType" => value == "RegisteredReceiver" ? "گیرنده ثبت‌شده" : "مشتری",
        "CommissionMethod" => value == "PeriodicPerLakh" ? "دوره‌ای / هر لک" : value,
        _ => value
    };
    private static string OperationLabel(string value)
    { var label = AppDisplayText.TransactionType(value); return label == "عملیات مالی دیگر" ? value : label; }
    private static string StatusLabel(string value) => value switch
    {
        "Imported" => "ثبت‌شده", "Draft" => "پیش‌نویس", "Confirmed" => "تأییدشده", "Failed" => "ناموفق", "Previewed" => "پیش‌نمایش",
        _ => AppDisplayText.Status(value) is var label && label != "وضعیت دیگر" ? label : value
    };
}
