using System.Text.RegularExpressions;

namespace Moonrise.Services;

public static partial class TokenRedactor
{
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var redacted = BearerPattern().Replace(text, "$1<redacted>");
        return NamedSecretPattern().Replace(redacted, "$1<redacted>");
    }

    [GeneratedRegex("(?i)((?:access[_-]?token|refresh[_-]?token|client[_-]?token|session[_-]?token|token|password|authorization|credential)[\\w.-]*[\\s\\\"]*[:=][\\s\\\"]*)([^\\s,;\\\"}]+)")]
    private static partial Regex NamedSecretPattern();

    [GeneratedRegex("(?i)(\\bbearer\\s+)([^\\s,;\\\"]+)")]
    private static partial Regex BearerPattern();
}
