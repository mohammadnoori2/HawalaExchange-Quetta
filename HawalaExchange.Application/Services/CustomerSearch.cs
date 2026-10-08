using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Services;

public static class CustomerSearch
{
    public static bool Matches(CustomerDto customer, string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term)) return true;
        return Contains(customer.CustomerCode, term) || Contains(customer.FullName, term) ||
            Contains(customer.FatherName, term) || Contains(customer.PhoneNumber, term) ||
            Contains(customer.TazkiraNumber, term) || Contains(customer.Address, term) ||
            Contains(customer.Remarks, term);
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
