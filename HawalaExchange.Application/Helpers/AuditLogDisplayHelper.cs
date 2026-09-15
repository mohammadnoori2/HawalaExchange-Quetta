using HawalaExchange.Application.DTOs;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HawalaExchange.Application.Helpers;

public static class AuditLogDisplayHelper
{
    private static readonly IReadOnlyDictionary<string, string> EntityNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hawalas"] = "حواله",
            ["Customers"] = "مشتری",
            ["Correspondents"] = "نمایندگی",
            ["Accounts"] = "حساب",
            ["Transactions"] = "تراکنش",
            ["TransactionDetails"] = "جزئیات تراکنش",
            ["LedgerEntries"] = "ثبت دفتر کل",
            ["Transfers"] = "انتقال بین حساب‌ها",
            ["Currencies"] = "ارز",
            ["ExchangeRates"] = "نرخ ارز",
            ["Expenses"] = "مصرف",
            ["Documents"] = "سند",
            ["Branches"] = "شعبه",
            ["PaymentLocations"] = "محل پرداخت",
            ["CapitalInvestments"] = "سرمایه",
            ["AccountMoneyOperations"] = "رسید یا برد",
            ["MoneyExchangeOperations"] = "تبادل ارز",
            ["AccountBadehkarLimits"] = "سقف بدهکاری",
            ["CashBalanceAlertSettings"] = "هشدار موجودی صندوق",
            ["CashBalanceAlertRecipients"] = "دریافت‌کننده هشدار موجودی صندوق",
            ["CashBalanceAlerts"] = "هشدار کمبود موجودی صندوق",
            ["CompanySettings"] = "تنظیمات شرکت",
            ["CorrespondentSettlementConversions"] = "تبدیل مانده نمایندگی",
            ["CorrespondentSettlementConversionItems"] = "جزئیات تبدیل مانده نمایندگی",
            ["CorrespondentSettlementConversionHawalas"] = "حواله تبدیل‌شده به ارز توافقی",
            ["CorrespondentSettlementConversionHawalaItems"] = "نرخ ارز توافقی حواله",
            ["AedDeals"] = "معاملات درهم",
            ["AedDealConversions"] = "تبدیل‌های معامله درهم",
            ["Users"] = "کاربر",
            ["CashDailyBalances"] = "مانده روزانه صندوق",
            ["ApplicationUsers"] = "کاربر",
            ["AspNetUsers"] = "کاربر"
        };

    private static readonly IReadOnlyDictionary<string, string> FieldNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = "شناسه", ["Number"] = "شماره حواله", ["HawalaType"] = "نوع حواله",
            ["SenderName"] = "نام فرستنده", ["SenderFatherName"] = "نام پدر فرستنده",
            ["SenderPhone"] = "شماره تماس فرستنده", ["SenderTazkiraNumber"] = "شماره تذکره فرستنده",
            ["SenderTazkiraImagePath"] = "تصویر تذکره فرستنده", ["SenderAddress"] = "آدرس فرستنده",
            ["ReceiverName"] = "نام گیرنده", ["ReceiverFatherName"] = "نام پدر گیرنده",
            ["ReceiverPhone"] = "شماره تماس گیرنده", ["ReceiverTazkiraNumber"] = "شماره تذکره گیرنده",
            ["ReceiverTazkiraImagePath"] = "تصویر تذکره گیرنده", ["ReceiverAddress"] = "آدرس گیرنده",
            ["FromAmount"] = "مبلغ ارسالی", ["ToAmount"] = "مبلغ پرداختی", ["ExchangeRate"] = "نرخ تبدیل",
            ["CommissionAmount"] = "کارمزد مشتری", ["AgentCommissionAmount"] = "کارمزد نمایندگی",
            ["FromCurrencyId"] = "ارز ارسالی", ["ToCurrencyId"] = "ارز پرداختی",
            ["CommissionCurrencyId"] = "ارز کارمزد مشتری", ["AgentCommissionCurrencyId"] = "ارز کارمزد نمایندگی",
            ["CorrespondentId"] = "نمایندگی", ["PaymentLocationId"] = "محل پرداخت",
            ["Status"] = "وضعیت", ["Notes"] = "یادداشت", ["ReferenceNumber"] = "شماره مرجع",
            ["PaidAt"] = "زمان اجرا یا پرداخت", ["PaidBy"] = "اجرا یا پرداخت‌کننده",
            ["PaidFromAccountId"] = "حساب پرداخت", ["CancelledAt"] = "زمان لغو",
            ["CancelledBy"] = "لغوکننده", ["CancelReason"] = "دلیل لغو",
            ["CreatedAt"] = "زمان ایجاد", ["UpdatedAt"] = "زمان ویرایش", ["ModifiedAt"] = "زمان ویرایش",
            ["CreatedBy"] = "ایجادکننده", ["UpdatedBy"] = "ویرایش‌کننده", ["ModifiedBy"] = "ویرایش‌کننده",
            ["IsActive"] = "فعال", ["IsArchived"] = "بایگانی‌شده", ["IsSystemGenerated"] = "ایجادشده توسط سیستم",
            ["Name"] = "نام", ["FullName"] = "نام کامل", ["FatherName"] = "نام پدر",
            ["PhoneNumber"] = "شماره تماس", ["Phone"] = "شماره تماس", ["Address"] = "آدرس",
            ["Code"] = "کد", ["AccountCode"] = "کد حساب", ["AccountName"] = "نام حساب",
            ["AccountType"] = "نوع حساب", ["CustomerCode"] = "کد مشتری", ["CurrencyId"] = "ارز",
            ["Amount"] = "مبلغ", ["Description"] = "توضیحات", ["TransactionNo"] = "شماره تراکنش",
            ["TransactionId"] = "تراکنش", ["AccountId"] = "حساب", ["BranchId"] = "شعبه",
            ["Debit"] = "بدهکار", ["Credit"] = "طلبکار", ["BadehKar"] = "بدهکار", ["TalabKar"] = "طلبکار",
            ["UserName"] = "نام کاربری", ["Email"] = "ایمیل", ["EmailConfirmed"] = "تأیید ایمیل",
            ["LockoutEnd"] = "پایان قفل حساب", ["AccessFailedCount"] = "تعداد ورود ناموفق"
            , ["CustomerId"] = "مشتری", ["SourceHawalaId"] = "حواله اصلی",
            ["ReversedTransactionId"] = "تراکنش برگشتی", ["TransferId"] = "انتقال",
            ["HawalaId"] = "حواله", ["ExpenseId"] = "مصرف", ["CapitalInvestmentId"] = "سرمایه",
            ["AccountMoneyOperationId"] = "رسید یا برد", ["MoneyExchangeOperationId"] = "تبادل ارز",
            ["OperationType"] = "نوع عملیات", ["OperationDate"] = "تاریخ عملیات",
            ["CashOrBankAccountId"] = "حساب صندوق یا بانک", ["IsDeleted"] = "حذف‌شده",
            ["InvestmentDate"] = "تاریخ سرمایه‌گذاری", ["ProfitCurrencyId"] = "ارز مفاد",
            ["ProfitCurrencyAmount"] = "مبلغ مفاد", ["ReceivingAccountId"] = "حساب دریافت‌کننده",
            ["CapitalAccountId"] = "حساب سرمایه", ["JournalDate"] = "تاریخ دفتر روزانه",
            ["OpeningBalance"] = "مانده آغاز روز", ["ClosingBalance"] = "مانده پایان روز",
            ["IsClosed"] = "روز بسته‌شده", ["ClosedAt"] = "زمان بستن روز",
            ["CurrencyCode"] = "کد ارز", ["Symbol"] = "علامت ارز",
            ["FromAccountId"] = "حساب مبدأ", ["ToAccountId"] = "حساب مقصد",
            ["TransferMethod"] = "روش انتقال", ["Remarks"] = "توضیحات",
            ["TransactionType"] = "نوع تراکنش", ["CustomerFullName"] = "نام کامل مشتری",
            ["TransferAmount"] = "مبلغ انتقال", ["EffectiveDate"] = "تاریخ اعتبار نرخ",
            ["Rate"] = "نرخ", ["FileName"] = "نام فایل", ["FilePath"] = "فایل",
            ["ContentType"] = "نوع فایل", ["FileSizeBytes"] = "اندازه فایل",
            ["UploadedAt"] = "زمان بارگذاری", ["EntityType"] = "بخش مرتبط", ["EntityId"] = "شماره بخش مرتبط",
            ["BadehkarLimit"] = "سقف بدهکاری", ["LocalUserName"] = "نام کاربری",
            ["MinimumBalance"] = "حداقل موجودی", ["CurrentBalance"] = "موجودی فعلی",
            ["NotifyAllUsers"] = "نمایش به همه کاربران", ["ShowInApp"] = "نمایش داخل برنامه",
            ["SettingId"] = "تنظیم هشدار", ["UserId"] = "دریافت‌کننده هشدار",
            ["TriggeredAt"] = "زمان شروع هشدار", ["LastCheckedAt"] = "آخرین بررسی",
            ["ResolvedAt"] = "زمان برطرف‌شدن هشدار",
            ["IsPlatformUser"] = "کاربر مدیریت مرکزی", ["LastLoginAt"] = "آخرین ورود",
            ["ReferenceType"] = "نوع شخص مرتبط", ["ReferenceId"] = "شخص مرتبط",
            ["CompanyName"] = "نام شرکت", ["LogoPath"] = "نشان شرکت",
            ["WhatsAppNumber"] = "شماره واتساپ", ["TelegramUserName"] = "نام کاربری تلگرام",
            ["FooterNote"] = "یادداشت پایین رسید", ["DefaultProfitCurrencyId"] = "ارز پیش‌فرض مفاد",
            ["ConversionId"] = "سند تبدیل", ["SourceCurrencyId"] = "ارز قبلی",
            ["ExchangeRate"] = "نرخ تبدیل", ["TargetTalabKar"] = "طلب تبدیل‌شده",
            ["TargetBadehKar"] = "بدهی تبدیل‌شده", ["SettlementHawalaItemId"] = "جزئیات تبدیل حواله",
            ["SourceAmount"] = "مبلغ قبلی", ["SettlementCurrencyId"] = "ارز توافقی",
            ["SettlementAmount"] = "مبلغ تبدیل‌شده", ["ConversionRate"] = "نرخ تبدیل",
            ["SettlementNumber"] = "شماره سند تبدیل", ["ConversionDate"] = "تاریخ تبدیل",
            ["Title"] = "عنوان مصرف", ["ExpenseDate"] = "تاریخ مصرف",
            ["DecimalPlaces"] = "تعداد رقم اعشار", ["QuotationPriority"] = "اولویت نمایش نرخ",
            ["ContactPerson"] = "نام مسئول", ["Country"] = "کشور", ["City"] = "شهر"
        };

    public static string EntityName(string? tableName) =>
        !string.IsNullOrWhiteSpace(tableName) && EntityNames.TryGetValue(tableName, out var name)
            ? name
            : "بخش دیگر سیستم";

    public static string FieldName(string propertyName)
    {
        if (propertyName == "__Description") return "توضیحات عملیات";
        if (propertyName == "__PreviousDescription") return "توضیحات قبلی";
        var parts = propertyName.Split('\u001f');
        if (parts.Length == 3)
            return $"{FieldName(parts[2])} در {EntityName(parts[0])} شماره {parts[1]}";
        if (FieldNames.TryGetValue(propertyName, out var name)) return name;
        return "مشخصات تکمیلی";
    }

    public static IReadOnlyList<AuditFieldChange> GetChanges(AuditLogDto log)
    {
        var oldValues = Parse(log.OldValue);
        var newValues = Parse(log.NewValue);
        if (oldValues == null && newValues == null)
        {
            return [new AuditFieldChange("توضیحات", log.OldValue, log.NewValue)];
        }

        var keys = (oldValues?.Keys ?? Enumerable.Empty<string>())
            .Union(newValues?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase)
            .Where(key => !key.Split('\u001f').Last().StartsWith("__", StringComparison.Ordinal))
            .OrderBy(FieldName)
            .ToList();

        return keys.Select(key => new AuditFieldChange(
            FieldName(key),
            oldValues != null && oldValues.TryGetValue(key, out var oldValue) ? Format(oldValue, key) : null,
            newValues != null && newValues.TryGetValue(key, out var newValue) ? Format(newValue, key) : null)).ToList();
    }

    public static string Summary(AuditLogDto log)
    {
        var oldValues = Parse(log.OldValue);
        var newValues = Parse(log.NewValue);
        var subject = EntityName(log.TableName);
        var recordName = FindRecordName(log, oldValues, newValues);
        var action = log.Action?.ToUpperInvariant();

        if (action == "UPDATE")
        {
            var clauses = GetPrimaryChanges(log, oldValues, newValues)
                .Take(4)
                .Select(change => $"{SummaryFieldName(change.PropertyName)} از {change.OldValue} به {change.NewValue}")
                .ToList();
            if (clauses.Count > 0)
                return $"در {subject} {recordName}، {JoinPersian(clauses)} تغییر کرد.";
        }

        if (action == "CREATE")
        {
            var createSummary = CreateSummary(log, newValues, recordName);
            if (!string.IsNullOrWhiteSpace(createSummary)) return createSummary;
        }

        if (action == "DELETE") return $"{subject} {recordName} حذف شد.";
        if (action == "ARCHIVE") return $"{subject} {recordName} بایگانی شد.";
        if (action == "UNARCHIVE") return $"{subject} {recordName} دوباره از بایگانی خارج شد.";
        if (action == "CANCEL")
        {
            var reason = FindPrimaryValue(log, newValues, "CancelReason") ?? FindPrimaryValue(log, oldValues, "CancelReason");
            return $"{subject} {recordName} لغو شد.{(string.IsNullOrWhiteSpace(reason) ? "" : $" دلیل: {reason}")}";
        }

        var description = FindValue(newValues, "__Description");
        if (!string.IsNullOrWhiteSpace(description)) return ReplaceTechnicalTerms(description);
        var verb = action switch
        {
            "CREATE" => "ثبت شد",
            "UPDATE" => "تغییر کرد",
            "DELETE" => "حذف شد",
            "CANCEL" => "لغو شد",
            "ARCHIVE" => "بایگانی شد",
            "UNARCHIVE" => "از بایگانی خارج شد",
            _ => "تغییر کرد"
        };
        return $"{subject} {recordName} {verb}.";
    }

    private static string FindRecordName(
        AuditLogDto log,
        Dictionary<string, JsonElement>? oldValues,
        Dictionary<string, JsonElement>? newValues)
    {
        if (log.TableName == "Hawalas")
        {
            var number = FindValue(newValues, "__RecordNumber") ?? FindValue(oldValues, "__RecordNumber");
            if (!string.IsNullOrWhiteSpace(number)) return $"شماره {number}";
        }

        var value = FindPrimaryValue(log, newValues, "__DisplayName") ??
                    FindPrimaryValue(log, oldValues, "__DisplayName");
        if (!string.IsNullOrWhiteSpace(value)) return $"«{value}»";

        value = FindValue(newValues, "Number") ?? FindValue(oldValues, "Number");
        return string.IsNullOrWhiteSpace(value) ? $"شماره {log.RecordId}" : $"شماره {value}";
    }

    private static string? CreateSummary(
        AuditLogDto log,
        Dictionary<string, JsonElement>? values,
        string recordName)
    {
        string? V(string property) => FindPrimaryValue(log, values, property);
        var amount = V("Amount");
        var currency = V("CurrencyId");
        var amountWithCurrency = AmountWithCurrency(amount, currency);

        return log.TableName switch
        {
            "Hawalas" => $"حواله {recordName} ثبت شد. فرستنده {V("SenderName") ?? "وارد نشده"}، گیرنده {V("ReceiverName") ?? "وارد نشده"} و مبلغ {AmountWithCurrency(V("FromAmount"), V("FromCurrencyId"))} است.",
            "Customers" => $"مشتری جدید به نام {V("FullName") ?? recordName.Trim('«', '»')} ثبت شد.",
            "Correspondents" => $"نمایندگی {V("Name") ?? recordName.Trim('«', '»')} ثبت شد.",
            "Accounts" => $"حساب {V("AccountName") ?? recordName.Trim('«', '»')} ساخته شد.",
            "Currencies" => $"ارز {V("Name") ?? recordName.Trim('«', '»')} ثبت شد.",
            "Branches" => $"شعبه {V("Name") ?? recordName.Trim('«', '»')} ثبت شد.",
            "PaymentLocations" => $"محل پرداخت {V("Name") ?? recordName.Trim('«', '»')} ثبت شد.",
            "Expenses" => $"مصرف {V("Title") ?? recordName.Trim('«', '»')} به مبلغ {amountWithCurrency} ثبت شد.",
            "CapitalInvestments" => $"سرمایه به مبلغ {amountWithCurrency} ثبت شد.",
            "Transfers" => $"{amountWithCurrency} از حساب {V("FromAccountId") ?? "نامشخص"} به حساب {V("ToAccountId") ?? "نامشخص"} انتقال شد.",
            "AccountMoneyOperations" when V("OperationType") == "رسید" => $"{amountWithCurrency} به حساب {V("AccountId") ?? "نامشخص"} رسید شد.",
            "AccountMoneyOperations" => $"{amountWithCurrency} از حساب {V("AccountId") ?? "نامشخص"} برد شد.",
            "MoneyExchangeOperations" => $"{AmountWithCurrency(V("FromAmount"), V("FromCurrencyId"))} به {AmountWithCurrency(V("ToAmount"), V("ToCurrencyId"))} تبدیل شد.",
            "ExchangeRates" => $"نرخ {V("FromCurrencyId") ?? "ارز اول"} به {V("ToCurrencyId") ?? "ارز دوم"} با نرخ {V("Rate") ?? "نامشخص"} ثبت شد.",
            "AccountBadehkarLimits" => $"سقف بدهکاری حساب {V("AccountId") ?? "نامشخص"} به مبلغ {AmountWithCurrency(V("BadehkarLimit"), V("CurrencyId"))} تعیین شد.",
            "CashBalanceAlertSettings" => $"هشدار موجودی صندوق {V("AccountId") ?? "نامشخص"} برای ارز {V("CurrencyId") ?? "نامشخص"} روی حداقل {V("MinimumBalance") ?? "نامشخص"} تنظیم شد.",
            "Documents" => $"سند {V("FileName") ?? recordName.Trim('«', '»')} بارگذاری شد.",
            "Transactions" => $"{V("TransactionType") ?? "عملیات مالی"} با شماره {V("TransactionNo") ?? log.RecordId.ToString(CultureInfo.InvariantCulture)} ثبت شد.",
            "CashDailyBalances" => $"مانده آغاز روز حساب {V("AccountId") ?? "نامشخص"} به مبلغ {AmountWithCurrency(V("OpeningBalance"), V("CurrencyId"))} ثبت شد.",
            "CompanySettings" => "تنظیمات صرافی ثبت شد.",
            "Users" or "AspNetUsers" => $"کاربر جدید به نام {V("FullName") ?? recordName.Trim('«', '»')} ساخته شد.",
            _ => null
        };
    }

    private static string AmountWithCurrency(string? amount, string? currency) =>
        $"{amount ?? "مبلغ نامشخص"}{(string.IsNullOrWhiteSpace(currency) ? "" : $" {currency}")}";

    private static string? FindPrimaryValue(
        AuditLogDto log,
        Dictionary<string, JsonElement>? values,
        string propertyName)
    {
        if (values == null) return null;
        var exactKey = $"{log.TableName}\u001f{log.RecordId}\u001f{propertyName}";
        if (values.TryGetValue(exactKey, out var exactValue)) return Format(exactValue, exactKey);
        if (values.TryGetValue(propertyName, out var directValue)) return Format(directValue, propertyName);
        return null;
    }

    private static string? FindValue(Dictionary<string, JsonElement>? values, string propertyName)
    {
        if (values == null) return null;
        var item = values.FirstOrDefault(x => x.Key.Split('\u001f').Last() == propertyName);
        return string.IsNullOrEmpty(item.Key) ? null : Format(item.Value, item.Key);
    }

    private static IEnumerable<SummaryChange> GetPrimaryChanges(
        AuditLogDto log,
        Dictionary<string, JsonElement>? oldValues,
        Dictionary<string, JsonElement>? newValues)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy", "UpdatedAt", "UpdatedBy", "RowVersion"
        };
        var keys = (oldValues?.Keys ?? Enumerable.Empty<string>())
            .Union(newValues?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            var parts = key.Split('\u001f');
            var propertyName = parts.Last();
            if (propertyName.StartsWith("__", StringComparison.Ordinal) || ignored.Contains(propertyName)) continue;
            if (parts.Length == 3 && (!parts[0].Equals(log.TableName, StringComparison.OrdinalIgnoreCase) ||
                                      parts[1] != log.RecordId.ToString(CultureInfo.InvariantCulture))) continue;

            var oldText = oldValues != null && oldValues.TryGetValue(key, out var oldValue)
                ? FormatSummaryValue(log, oldValues, oldValue, key, propertyName)
                : "وارد نشده";
            var newText = newValues != null && newValues.TryGetValue(key, out var newValue)
                ? FormatSummaryValue(log, newValues, newValue, key, propertyName)
                : "وارد نشده";
            if (oldText == newText) continue;
            yield return new SummaryChange(propertyName, oldText, newText);
        }
    }

    private static string FormatSummaryValue(
        AuditLogDto log,
        Dictionary<string, JsonElement> values,
        JsonElement value,
        string key,
        string propertyName)
    {
        var formatted = Format(value, key);
        var currencyProperty = propertyName switch
        {
            "FromAmount" => "FromCurrencyId",
            "ToAmount" => "ToCurrencyId",
            "CommissionAmount" => "CommissionCurrencyId",
            "AgentCommissionAmount" => "AgentCommissionCurrencyId",
            "ProfitCurrencyAmount" => "ProfitCurrencyId",
            "Amount" or "BadehkarLimit" or "MinimumBalance" or "CurrentBalance" or
                "OpeningBalance" or "ClosingBalance" => "CurrencyId",
            _ => null
        };
        if (currencyProperty == null) return formatted;
        var currency = FindPrimaryValue(log, values, currencyProperty);
        return AmountWithCurrency(formatted, currency);
    }

    private static string SummaryFieldName(string propertyName) => propertyName switch
    {
        "SenderName" => "فرستنده",
        "ReceiverName" => "گیرنده",
        "SenderFatherName" => "نام پدر فرستنده",
        "ReceiverFatherName" => "نام پدر گیرنده",
        "FromAmount" or "ToAmount" or "Amount" => "مبلغ",
        "Status" => "وضعیت",
        _ => FieldName(propertyName)
    };

    private static string JoinPersian(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        2 => $"{items[0]} و {items[1]}",
        _ => $"{string.Join("، ", items.Take(items.Count - 1))} و {items[^1]}"
    };

    private static Dictionary<string, JsonElement>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{')) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json); }
        catch (JsonException) { return null; }
    }

    private static string Format(JsonElement value, string key)
    {
        var propertyName = key.Split('\u001f').Last();
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return TranslateValue(propertyName, text);
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "وارد نشده",
            JsonValueKind.True => "بلی",
            JsonValueKind.False => "خیر",
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number.ToString("#,##0.####", CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static string TranslateValue(string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "وارد نشده";
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            return date.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (propertyName is "FilePath" or "LogoPath" or "SenderTazkiraImagePath" or "ReceiverTazkiraImagePath")
            return "فایل ثبت‌شده";

        return propertyName switch
        {
            "Status" => TranslateStatus(value),
            "HawalaType" => TranslateHawalaType(value),
            "TransactionType" => TranslateTransactionType(value),
            "AccountType" => TranslateAccountType(value),
            "OperationType" => value switch { "Deposit" => "رسید", "Withdraw" or "Payment" => "برد", _ => "عملیات حساب" },
            "TransferMethod" => value switch { "Cash" => "نقدی", "Bank" or "BankTransfer" => "بانکی", "Account" or "AccountTransfer" => "بین حساب‌ها", "Hawala" => "حواله", _ => "روش دیگر" },
            "ReferenceType" or "EntityType" => value switch { "Customer" => "مشتری", "Correspondent" => "نمایندگی", "Account" => "حساب", "Transaction" => "تراکنش", _ => "بخش دیگر" },
            _ => ReplaceTechnicalTerms(value)
        };
    }

    private static string ReplaceTechnicalTerms(string value) => value
        .Replace("HawalaReceive", "حواله دریافتی", StringComparison.OrdinalIgnoreCase)
        .Replace("HawalaSend", "حواله ارسالی", StringComparison.OrdinalIgnoreCase)
        .Replace("HawalaOther", "حواله متفرقه", StringComparison.OrdinalIgnoreCase)
        .Replace("OpeningBalance", "موجودی اولیه", StringComparison.OrdinalIgnoreCase)
        .Replace("MoneyExchange", "تبادل ارز", StringComparison.OrdinalIgnoreCase)
        .Replace("CorrespondentSettlementConversion", "تبدیل مانده نمایندگی", StringComparison.OrdinalIgnoreCase)
        .Replace("Pending", "در انتظار", StringComparison.OrdinalIgnoreCase)
        .Replace("Cancelled", "لغوشده", StringComparison.OrdinalIgnoreCase)
        .Replace("Completed", "تکمیل‌شده", StringComparison.OrdinalIgnoreCase);

    private static string TranslateStatus(string value) => value switch
    {
        "Pending" => "در انتظار",
        "Paid" => "پرداخت‌شده",
        "Completed" => "تکمیل‌شده",
        "Executed" => "اجراشده",
        "Cancel" or "Cancelled" => "لغوشده",
        "Active" => "فعال",
        "Inactive" => "غیرفعال",
        "Suspended" => "تعلیق‌شده",
        "Expired" => "منقضی‌شده",
        "Trial" => "آزمایشی",
        "Deleted" => "حذف‌شده",
        _ => "وضعیت دیگر"
    };

    private static string TranslateHawalaType(string value) => value switch
    {
        "HawalaSend" => "ارسالی", "HawalaReceive" => "دریافتی", "HawalaOther" => "متفرقه", _ => "نوع دیگر"
    };

    private static string TranslateTransactionType(string value) => value switch
    {
        "Exchange" or "MoneyExchange" => "تبادل ارز", "Transfer" => "انتقال بین حساب‌ها",
        "Expense" => "ثبت مصرف", "Adjustment" => "اصلاح حساب", "OpeningBalance" => "موجودی اولیه",
        "HawalaSend" => "حواله ارسالی", "HawalaReceive" => "حواله دریافتی", "HawalaOther" => "حواله متفرقه",
        "CapitalInvestment" => "ثبت سرمایه", "Deposit" => "رسید", "Withdraw" or "Payment" => "برد",
        "CorrespondentSettlementConversion" => "تبدیل مانده نمایندگی", _ => "عملیات مالی دیگر"
    };

    private static string TranslateAccountType(string value) => value.ToLowerInvariant() switch
    {
        "cash" => "صندوق", "bank" => "بانک", "customer" => "مشتری", "correspondent" => "نمایندگی",
        "income" => "درآمد", "expense" => "مصرف", "equity" => "سرمایه", "pendinghawala" => "حواله‌های اجرا نشده",
        "currencyconversionclearing" => "حساب واسط تبدیل ارز", _ => "نوع دیگر حساب"
    };
}

public sealed record AuditFieldChange(string FieldName, string? OldValue, string? NewValue);
internal sealed record SummaryChange(string PropertyName, string OldValue, string NewValue);
