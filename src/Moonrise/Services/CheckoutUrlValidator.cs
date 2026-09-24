namespace Moonrise.Services;

public static class CheckoutUrlValidator
{
    public static bool TryValidateCheckout(string methodId, string? value, out Uri? uri) =>
        methodId.ToLowerInvariant() switch
        {
            "telegram_stars" => TryValidateTelegramCheckout(value, out uri),
            "direct_crypto" => TryValidateDirectCrypto(value, out uri),
            _ => Fail(out uri)
        };

    public static bool TryValidateTelegramCheckout(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.IdnHost, "t.me", StringComparison.OrdinalIgnoreCase) ||
            !parsed.IsDefaultPort ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            !parsed.AbsolutePath.StartsWith("/$", StringComparison.Ordinal) ||
            parsed.AbsolutePath.Length <= 2)
            return false;
        uri = parsed;
        return true;
    }


    public static bool TryValidateDirectCrypto(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !new[] { "bitcoin", "ethereum", "solana", "litecoin", "dogecoin" }
                .Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    private static bool Fail(out Uri? uri)
    {
        uri = null;
        return false;
    }
}
