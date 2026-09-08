using System.Text;
using System.Text.RegularExpressions;

namespace HawalaExchange.Application.Services
{
    public static partial class PaymentLocationNameNormalizer
    {
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value
                .Normalize(NormalizationForm.FormKC)
                .Replace('\u064A', '\u06CC')
                .Replace('\u0643', '\u06A9')
                .Replace("\u200C", string.Empty)
                .Replace("\u200D", string.Empty);

            return MultipleWhitespaceRegex()
                .Replace(normalized.Trim(), " ")
                .ToLowerInvariant();
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex MultipleWhitespaceRegex();
    }
}
