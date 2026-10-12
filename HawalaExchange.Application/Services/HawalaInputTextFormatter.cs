using System.Globalization;
using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Services;

/// <summary>Seven headerless import columns, separated by tabs for pasting into Excel.</summary>
public static class HawalaInputTextFormatter
{
    public static string Format(IEnumerable<SelectedRecordDto> rows) => string.Join("\r\n", rows.Select(row => string.Join('\t',
        Identifier(row.Number), Identifier(row.ReferenceNumber), Cell(row.SenderName), Cell(row.ReceiverName),
        Cell(row.PaymentLocationName), row.Amount.ToString("0.####", CultureInfo.InvariantCulture), Cell(row.CurrencyCode))));

    private static string Identifier(string number)
        => number.All(char.IsAsciiDigit) && (number.Length > 15 || (number.Length > 1 && number[0] == '0'))
            ? "'" + Cell(number) : Cell(number);

    private static string Cell(string? value)
    {
        var text = (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        // Clipboard text must not become an executable Excel formula.
        return text.TrimStart().StartsWithAnyFormulaPrefix() ? "'" + text : text;
    }

    private static bool StartsWithAnyFormulaPrefix(this string text)
        => text.Length > 0 && text[0] is '=' or '+' or '-' or '@';
}
