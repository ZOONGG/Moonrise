using System.Text.RegularExpressions;

namespace Moonrise.Services;

public enum MoonriseDeepLinkAction
{
    Catalog,
    Package,
    Install
}

public sealed record MoonriseDeepLink(MoonriseDeepLinkAction Action, string? PackageId)
{
    private static readonly Regex PackageIdPattern = new(
        @"\A[A-Za-z0-9](?:[A-Za-z0-9._-]{0,127})\z",
        RegexOptions.CultureInvariant);

    public static bool TryParse(string? value, out MoonriseDeepLink? deepLink, out string error)
    {
        deepLink = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
        {
            error = "The Moonrise link is empty or too long.";
            return false;
        }

        var schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
        var rawPathStart = schemeSeparator < 0 ? -1 : value.IndexOf('/', schemeSeparator + 3);
        if (rawPathStart >= 0)
        {
            foreach (var rawSegment in value[rawPathStart..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                string decodedSegment;
                try
                {
                    decodedSegment = Uri.UnescapeDataString(rawSegment);
                }
                catch (UriFormatException)
                {
                    error = "The Moonrise link contains invalid escaping.";
                    return false;
                }

                if (decodedSegment is "." or "..")
                {
                    error = "The Moonrise link contains a traversal segment.";
                    return false;
                }
            }
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "moonrise", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "The Moonrise link is invalid.";
            return false;
        }

        var action = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (action == "catalog" && segments.Length == 0)
        {
            deepLink = new MoonriseDeepLink(MoonriseDeepLinkAction.Catalog, null);
            return true;
        }

        if ((action == "package" || action == "install") && segments.Length == 1)
        {
            string packageId;
            try
            {
                packageId = Uri.UnescapeDataString(segments[0]);
            }
            catch (UriFormatException)
            {
                error = "The package identifier contains invalid escaping.";
                return false;
            }

            if (!PackageIdPattern.IsMatch(packageId))
            {
                error = "The package identifier contains unsupported characters.";
                return false;
            }

            deepLink = new MoonriseDeepLink(
                action == "package" ? MoonriseDeepLinkAction.Package : MoonriseDeepLinkAction.Install,
                packageId);
            return true;
        }

        error = "The Moonrise link action is not supported.";
        return false;
    }
}
