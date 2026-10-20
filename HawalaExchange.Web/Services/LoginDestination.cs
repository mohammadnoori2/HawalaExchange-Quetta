namespace HawalaExchange.Web.Services;

/// <summary>Chooses an account-appropriate local landing page; does not grant access to that page.</summary>
public static class LoginDestination
{
    public static string Home(bool platformUser, string baseUri) =>
        new Uri(new Uri(baseUri, UriKind.Absolute), platformUser ? "platform" : "").AbsolutePath;

    public static string Resolve(bool platformUser, string? returnUrl, string baseUri)
    {
        var fallback = Home(platformUser, baseUri);
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl.Any(char.IsControl) || returnUrl.Contains('\\'))
            return fallback;
        var candidate = returnUrl.Trim();
        if (candidate.StartsWith("//", StringComparison.Ordinal)) return fallback;
        var origin = new Uri(baseUri, UriKind.Absolute);
        if (!Uri.TryCreate(origin, candidate, out var target) ||
            target.Scheme != origin.Scheme || target.Host != origin.Host || target.Port != origin.Port ||
            !string.IsNullOrEmpty(target.UserInfo)) return fallback;

        var path = Uri.UnescapeDataString(target.AbsolutePath);
        var basePath = Uri.UnescapeDataString(origin.AbsolutePath);
        // Reject ambiguous separators/controls and double-encoded paths instead of browser-dependent redirects.
        if (path.Contains('\\') || path.Contains('%') || path.Any(char.IsControl) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            !path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return fallback;
        var route = path[basePath.Length..].TrimEnd('/');
        var platformRoute = IsSection(route, "platform") || IsSection(route, "tenants");
        var sharedProfile = IsSection(route, "Account/Manage") || route.Equals("profile", StringComparison.OrdinalIgnoreCase);
        if (IsSection(route, "Account") && !sharedProfile ||
            route.Equals("login", StringComparison.OrdinalIgnoreCase) || route.Equals("logout", StringComparison.OrdinalIgnoreCase))
            return fallback;
        if (platformUser ? !platformRoute && !sharedProfile : platformRoute) return fallback;
        return target.PathAndQuery + target.Fragment;
    }

    private static bool IsSection(string route, string prefix) =>
        route.Equals(prefix, StringComparison.OrdinalIgnoreCase) || route.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
