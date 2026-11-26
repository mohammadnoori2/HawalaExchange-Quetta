using System.Globalization;

namespace HawalaExchange.Application.DTOs;

public sealed record HawalaIdentifierSearch(string Kind = "Reference", string Value = "")
{
    public bool IsActive => !string.IsNullOrWhiteSpace(Value);
    public long? Serial => Kind == "Serial" && long.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    public static HawalaIdentifierSearch Create(string kind, string? value)
    {
        if (kind is not ("Reference" or "Serial")) throw new ArgumentException("نوع شماره معتبر نیست.");
        var term = value?.Trim() ?? "";
        if (kind == "Serial" && term.Length > 0)
        {
            term = string.Concat(term.Select(c => char.IsDigit(c) ? ((int)char.GetNumericValue(c)).ToString(CultureInfo.InvariantCulture) : c.ToString()));
            if (!long.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
                throw new ArgumentException("نمبر مسلسل باید یک عدد صحیح مثبت باشد.");
            term = number.ToString(CultureInfo.InvariantCulture);
        }
        if (term.Length > 200) throw new ArgumentException("شماره واردشده بیش از حد طولانی است.");
        return new(kind, term);
    }
}
