using System.Globalization;
using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Services;

public static class AccountSearch
{
    public static bool Matches(AccountDto account, string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term)) return true;
        return Contains(account.AccountCode, term) || Contains(account.AccountName, term) ||
            Contains(account.ReferenceName, term) || Contains(account.ReferenceType, term) ||
            Contains(account.ReferenceId?.ToString(CultureInfo.InvariantCulture), term);
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
