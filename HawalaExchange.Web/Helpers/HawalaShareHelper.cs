using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Web.Helpers;

public static class HawalaShareHelper
{
    public static string BuildText(HawalaDto hawala, CompanySettingDto? company = null)
    {
        var companyName = string.IsNullOrWhiteSpace(company?.CompanyName)
            ? "حواله"
            : company.CompanyName;

        var title = hawala.HawalaType switch
        {
            "HawalaSend" => "حواله ارسالی",
            "HawalaReceive" => "حواله دریافتی",
            "HawalaOther" => "حواله متفرقه",
            _ => "حواله"
        };

        var createdDate = PersianDateHelper.ToPersianDateTime12(hawala.CreatedAt);

        var paymentLocation =
            hawala.PaymentLocationName ??
            hawala.PaymentLocationAddress ??
            hawala.PaymentLocation ??
            "-";

        return
$@"{companyName}
{title}

نمبر حواله: {hawala.Number}
وضعیت: {hawala.StatusName}
تاریخ: {createdDate}

فرستنده: {hawala.SenderName ?? "-"}
نام پدر فرستنده: {hawala.SenderFatherName ?? "-"}
تلفن فرستنده: {hawala.SenderPhone ?? "-"}

گیرنده: {hawala.ReceiverName ?? "-"}
نام پدر گیرنده: {hawala.ReceiverFatherName ?? "-"}
تلفن گیرنده: {hawala.ReceiverPhone ?? "-"}

نماینده: {hawala.CorrespondentName ?? "-"}
محل پرداخت: {paymentLocation}

مبلغ حواله: {hawala.FromAmount:N2} {hawala.FromCurrencyCode}
مبلغ دریافتی/پرداختی: {(hawala.ToAmount.HasValue ? hawala.ToAmount.Value.ToString("N2") : "-")} {hawala.ToCurrencyCode}
نرخ تبدیل: {(hawala.ExchangeRate.HasValue ? hawala.ExchangeRate.Value.ToString("N4") : "-")}

کارمزد: {(hawala.CommissionAmount.HasValue ? hawala.CommissionAmount.Value.ToString("N2") : "-")} {hawala.CommissionCurrencyCode}
کارمزد نماینده: {(hawala.AgentCommissionAmount.HasValue ? hawala.AgentCommissionAmount.Value.ToString("N2") : "-")} {hawala.AgentCommissionCurrencyCode}

شماره مرجع: {hawala.ReferenceNumber ?? "-"}
یادداشت: {hawala.Notes ?? "-"}";
    }
}