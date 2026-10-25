using System.Text;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;

namespace HawalaExchange.Application.Helpers;

/// <summary>Shares current customer balances only; never transaction rows or pending commissions.</summary>
public static class CustomerBalanceShareText
{
    public static string Build(string? customerName, IEnumerable<BalanceDto>? balances)
    {
        if (string.IsNullOrWhiteSpace(customerName) || balances is null) return string.Empty;
        var rows = balances.Where(x => !string.IsNullOrWhiteSpace(x.CurrencyCode))
            .OrderBy(x => x.CurrencyCode, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rows.Length == 0) return string.Empty;

        var text = new StringBuilder().AppendLine("مانده فعلی حساب مشتری")
            .AppendLine($"مشتری: {SingleLine(customerName)}");
        foreach (var row in rows)
        {
            var balance = AmountValueHelper.RoundConvertedAmount(row.Balance);
            var status = balance > 0 ? "طلبکار" : balance < 0 ? "بدهکار" : "تسویه";
            text.AppendLine($"{SingleLine(row.CurrencyCode)}: {AmountValueHelper.Format(Math.Abs(balance))} ({status})");
        }
        return text.ToString().TrimEnd();
    }

    public static string WhatsAppUrl(string text) => "https://wa.me/?text=" + Uri.EscapeDataString(text);

    private static string SingleLine(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
