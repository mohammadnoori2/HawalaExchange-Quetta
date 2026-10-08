using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Services;

public static class CorrespondentSearch
{
    public static bool Matches(CorrespondentDto correspondent, string? search, IEnumerable<string>? accountCodes = null)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term)) return true;
        return Contains(correspondent.Code, term) || Contains(correspondent.Name, term) ||
            Contains(correspondent.PhoneNumber, term) || Contains(correspondent.Address, term) ||
            Contains(correspondent.Remarks, term) || Contains(correspondent.Country, term) ||
            Contains(correspondent.City, term) || accountCodes?.Any(code => Contains(code, term)) == true;
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
