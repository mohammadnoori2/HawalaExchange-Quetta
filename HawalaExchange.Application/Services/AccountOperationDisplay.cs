namespace HawalaExchange.Application.Services;

public static class AccountOperationDisplay
{
    public static string Name(string? type) => type switch { "Deposit" => "رسید", "Withdraw" => "برد", _ => "عملیات حساب" };
    public static string Badge(string? type) => type switch { "Deposit" => "badge-success", "Withdraw" => "badge-danger", _ => "badge-secondary" };
    public static string Description(string type, string party, string cash, decimal amount, string currency, string? note)
    {
        party = string.IsNullOrWhiteSpace(party) ? "نامشخص" : party.Trim();
        cash = string.IsNullOrWhiteSpace(cash) ? "نامشخص" : cash.Trim();
        var text = type switch { "Deposit" => $"رسید از حساب {party} به حساب {cash}",
            "Withdraw" => $"برد از حساب {cash} برای حساب {party}", _ => $"عملیات حساب میان حساب {party} و حساب {cash}" };
        text += $"، مبلغ {AmountValueHelper.Format(amount)} {currency}";
        note = note?.Trim();
        var defaultNote = type == "Deposit" ? "ثبت رسید" : type == "Withdraw" ? "ثبت برد" : null;
        return string.IsNullOrWhiteSpace(note) || note == defaultNote ? text : $"{text}؛ یادداشت: {note}";
    }
}
