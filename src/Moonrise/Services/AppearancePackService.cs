using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed partial class AppearancePackService
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<LanguagePack> LoadLanguages(string directory, Action<string>? reportError = null)
    {
        Directory.CreateDirectory(directory);
        var packs = new List<LanguagePack>
        {
            new() { Code = "ru", Name = "Русский" },
            new() { Code = "en", Name = "English" }
        };
        foreach (var path in Directory.EnumerateFiles(directory, "*.moonrise-language.json")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var pack = JsonSerializer.Deserialize<LanguagePack>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("The language pack is empty.");
                ValidateLanguage(pack);
                packs.RemoveAll(existing => string.Equals(existing.Code, pack.Code, StringComparison.OrdinalIgnoreCase));
                packs.Add(pack);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                reportError?.Invoke($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }
        return packs;
    }

    public IReadOnlyList<ThemePack> LoadThemes(string directory)
    {
        Directory.CreateDirectory(directory);
        return new List<ThemePack>
        {
            new() { Id = "moonlight", Name = "Moonlight", WindowBackground = "#070B15", Panel = "#0E1727", PanelSecondary = "#121D31", Border = "#273956", Muted = "#7182A1", Accent = "#8B5CF6", PrimaryText = "#F5F7FC" },
            new() { Id = "obsidian", Name = "Obsidian", WindowBackground = "#090B0F", Panel = "#12161C", PanelSecondary = "#171C23", Border = "#2A343F", Muted = "#737F8C", Accent = "#7189A5", PrimaryText = "#F1F3F5" },
            new() { Id = "aurora", Name = "Aurora", WindowBackground = "#061016", Panel = "#0B1C25", PanelSecondary = "#102630", Border = "#244552", Muted = "#708F98", Accent = "#29B8C7", PrimaryText = "#F1FAFB" }
        };
    }

    public string Translate(LanguagePack language, string english) =>
        language.Translations.TryGetValue(english, out var translation) && !string.IsNullOrWhiteSpace(translation)
            ? translation
            : english;

    private static void ValidateLanguage(LanguagePack pack)
    {
        if (pack.SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(pack.Code) ||
            !LanguageCodePattern().IsMatch(pack.Code) || string.IsNullOrWhiteSpace(pack.Name) || pack.Translations is null)
            throw new InvalidDataException("The language pack metadata is invalid.");
        if (pack.Translations.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
            throw new InvalidDataException("The language pack contains an empty translation.");
        foreach (var pair in pack.Translations.Where(pair => pair.Key.Contains('{')))
        {
            try
            {
                if (CompositeFormat.Parse(pair.Key).MinimumArgumentCount != CompositeFormat.Parse(pair.Value).MinimumArgumentCount)
                    throw new InvalidDataException($"Translation placeholders do not match: {pair.Key}");
            }
            catch (FormatException)
            {
                throw new InvalidDataException($"Invalid translation placeholders: {pair.Key}");
            }
        }
    }

    private static void ValidateTheme(ThemePack pack)
    {
        if (pack.SchemaVersion != CurrentSchemaVersion || !ThemeIdPattern().IsMatch(pack.Id) ||
            string.IsNullOrWhiteSpace(pack.Name))
            throw new InvalidDataException("The theme pack metadata is invalid.");
        foreach (var color in new[] { pack.WindowBackground, pack.Panel, pack.PanelSecondary, pack.Border, pack.Muted, pack.Accent })
            if (!ColorPattern().IsMatch(color)) throw new InvalidDataException("Theme colors must use #RRGGBB or #AARRGGBB.");
    }

    [GeneratedRegex("^[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCodePattern();
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdPattern();
    [GeneratedRegex("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
