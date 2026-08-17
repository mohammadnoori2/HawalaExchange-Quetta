using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;

namespace HawalaExchange.Web.Helpers;

public static class HawalaShareHelper
{
    public static string BuildText(HawalaDto hawala)
    {
        var createdDate = PersianDateHelper.ToPersianDate(hawala.CreatedAt);

        var paymentLocation =
            hawala.PaymentLocationName ??
            hawala.PaymentLocationAddress ??
            hawala.PaymentLocation ??
            "-";
        var toCurrencyName = GetCurrencyName(
            hawala.ToCurrencyName,
            hawala.ToCurrencyCode);
        var commissionCurrencyName = GetCurrencyName(
            hawala.CommissionCurrencyName,
            hawala.CommissionCurrencyCode);

        var noteLine = HasUserNote(hawala)
            ? $"{Environment.NewLine}یادداشت: {hawala.Notes}"
            : string.Empty;

        return
$@"نمبر حواله: {hawala.Number}
نمبر متفرقه: {hawala.ReferenceNumber ?? "-"}
تاریخ: {createdDate}
فرستنده: {hawala.SenderName ?? "-"}
گیرنده: {hawala.ReceiverName ?? "-"}
نام پدر گیرنده: {hawala.ReceiverFatherName ?? "-"}
محل پرداخت: {paymentLocation}
مبلغ پرداختی: {(hawala.ToAmount.HasValue ? MoneyFormatHelper.Format(hawala.ToAmount.Value) : "-")} {toCurrencyName}
مبلغ به حروف: {(hawala.ToAmount.HasValue ? DariNumberToWords.ToWords(hawala.ToAmount.Value) : "-")} {toCurrencyName}
کارمزد: {(hawala.CommissionAmount.HasValue ? MoneyFormatHelper.Format(hawala.CommissionAmount.Value) : "-")} {commissionCurrencyName}
نوت: این رسید جهت معلومات مشتری است و هیچگاه ارزش پولی ندارد.{noteLine}";
    }

    private static string GetCurrencyName(string? name, string? code)
    {
        return !string.IsNullOrWhiteSpace(name)
            ? name
            : code ?? "-";
    }

    private static bool HasUserNote(HawalaDto hawala) =>
        !string.IsNullOrWhiteSpace(hawala.Notes) &&
        !(hawala.IsSystemGenerated &&
          hawala.SourceHawalaId.HasValue &&
          hawala.Notes.StartsWith(
              "حواله ارسالی خودکار بابت پرداخت حواله دریافتی شماره",
              StringComparison.Ordinal));
}
